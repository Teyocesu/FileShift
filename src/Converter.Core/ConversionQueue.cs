using System.Collections.Concurrent;

namespace FileShift.Core;

public sealed class ConversionQueue : IDisposable, IAsyncDisposable
{
    private readonly IReadOnlyList<IConversionEngineAdapter> _adapters;
    private readonly IOutputIntegrityValidator? _validator;
    private readonly object _gate = new();
    private int _maxConcurrency;
    private int _running;
    private bool _disposed;
    private TaskCompletionSource _capacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, ConversionJob> _jobs = new();
    private readonly Dictionary<string, JobExecution> _executions = new();

    private sealed class JobExecution
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public TaskCompletionSource<ConversionJob> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public event Action<ConversionJob>? JobUpdated;

    public ConversionQueue(
        IEnumerable<IConversionEngineAdapter> adapters,
        int maxConcurrency = 4,
        IOutputIntegrityValidator? validator = null)
    {
        _adapters = adapters.ToList();
        _validator = validator;
        _maxConcurrency = Math.Clamp(maxConcurrency, 1, 16);
    }

    public void UpdateMaxConcurrency(int maxConcurrency)
    {
        lock (_gate)
        {
            _maxConcurrency = Math.Clamp(maxConcurrency, 1, 16);
            SignalCapacityChanged();
        }
    }

    public IReadOnlyCollection<ConversionJob> Jobs => _jobs.Values
        .OrderByDescending(j => j.CreatedAt)
        .ToList();

    public ConversionJob? GetJob(string jobId) =>
        _jobs.TryGetValue(jobId, out var job) ? job : null;

    public void ClearCompletedJobs()
    {
        foreach (var kv in _jobs)
        {
            if (kv.Value.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            {
                _jobs.TryRemove(kv.Key, out _);
            }
        }
    }

    public ConversionJob Enqueue(string inputPath, string targetFormat, ConversionOptions? options = null)
    {
        var normalizedTarget = FormatMatrix.NormalizeFormatId(targetFormat);
        var ext = Path.GetExtension(inputPath).TrimStart('.').ToLowerInvariant();
        var job = new ConversionJob
        {
            InputPath = Path.GetFullPath(inputPath),
            SourceFormat = FormatMatrix.NormalizeFormatId(ext),
            TargetFormat = normalizedTarget,
            Options = options ?? new ConversionOptions()
        };

        var execution = new JobExecution();
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _jobs[job.Id] = job;
            _executions.Add(job.Id, execution);
        }

        _ = Task.Run(() => ExecuteJobAsync(job, execution));
        return job;
    }

    public bool CancelJob(string jobId)
    {
        lock (_gate)
        {
            if (!_executions.TryGetValue(jobId, out var execution) ||
                execution.Cancellation.IsCancellationRequested)
                return false;

            execution.Cancellation.Cancel();
            return true;
        }
    }

    public Task<ConversionJob> WaitForJobAsync(string jobId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_executions.TryGetValue(jobId, out var execution))
                return execution.Completion.Task.WaitAsync(ct);
            if (_jobs.TryGetValue(jobId, out var job))
                return Task.FromResult(job);
            throw new KeyNotFoundException($"Trabajo desconocido: {jobId}");
        }
    }

    private void SignalCapacityChanged()
    {
        _capacityChanged.TrySetResult();
        _capacityChanged = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private async Task AcquireCapacityAsync(CancellationToken ct)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                ct.ThrowIfCancellationRequested();
                if (_running < _maxConcurrency)
                {
                    _running++;
                    return;
                }
                changed = _capacityChanged.Task;
            }
            await changed.WaitAsync(ct);
        }
    }

    private async Task ExecuteJobAsync(ConversionJob job, JobExecution execution)
    {
        var ct = execution.Cancellation.Token;
        var status = JobStatus.Failed;
        string? stagingDirectory = null;
        bool gateAcquired = false;
        try
        {
            await AcquireCapacityAsync(ct);
            gateAcquired = true;
            ct.ThrowIfCancellationRequested();

            job.Status = JobStatus.Preparing;
            job.StartedAt = DateTimeOffset.UtcNow;
            Notify(job);

            if (!File.Exists(job.InputPath))
            {
                throw new FileNotFoundException($"El archivo de entrada no existe: {job.InputPath}");
            }

            // INV-01: Record original SHA-256 before any inspection or conversion
            job.InputSizeBytes = new FileInfo(job.InputPath).Length;
            job.OriginalSha256 = await SafeFilePolicy.ComputeSha256Async(job.InputPath, ct);

            var inspection = MagicByteInspector.InspectFile(job.InputPath);
            if (!inspection.IsValid)
            {
                throw new InvalidDataException(inspection.FailureReason ?? "Formato de entrada inválido o corrupto.");
            }

            job.SourceFormat = inspection.DetectedFormat;

            if (!FormatMatrix.CanConvert(job.SourceFormat, job.TargetFormat, libreOfficeInstalled: true))
            {
                throw new NotSupportedException(
                    $"Conversión no soportada: {job.SourceFormat.ToUpperInvariant()} → {job.TargetFormat.ToUpperInvariant()}.");
            }

            var targetDescriptor = FormatMatrix.Formats[job.TargetFormat];
            var outputDirectory = SafeFilePolicy.GetOutputDirectory(job.InputPath, job.Options.CustomOutputDirectory);
            stagingDirectory = Path.Combine(outputDirectory, $".fileshift.tmp.{Guid.NewGuid():N}");
            Directory.CreateDirectory(stagingDirectory);
            var fileName = Path.GetFileNameWithoutExtension(job.InputPath) + "." + targetDescriptor.Extension;
            var tempPath = Path.Combine(stagingDirectory, fileName);

            var adapter = ResolveAdapter(job.SourceFormat, job.TargetFormat);
            if (!await adapter.IsAvailableAsync(ct))
            {
                throw new InvalidOperationException($"El motor requerido '{adapter.EngineName}' no está disponible en el sistema.");
            }

            job.EngineUsed = adapter.EngineName;
            job.Status = JobStatus.Running;
            job.ProgressPercent = 15;
            Notify(job);

            var progress = new InlineProgress(p =>
            {
                job.ProgressPercent = Math.Clamp(p, 1, 95);
                Notify(job);
            });

            await adapter.ConvertAsync(job, tempPath, progress, ct);
            ct.ThrowIfCancellationRequested();

            job.Status = JobStatus.Validating;
            job.ProgressPercent = 96;
            Notify(job);

            var stagedPaths = new[] { tempPath }.Concat(
                Directory.EnumerateFiles(stagingDirectory).Where(path => path != tempPath)
                    .Order(StringComparer.Ordinal)).ToArray();
            foreach (var stagedPath in stagedPaths)
                await ValidateTemporaryOutputBeforePublishAsync(stagedPath, job.TargetFormat, job, ct);

            void VerifyOriginal()
            {
                using var original = File.OpenRead(job.InputPath);
                job.PostOriginalSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(original));
                job.OriginalIntactVerified = job.OriginalSha256 == job.PostOriginalSha256;
                if (!job.OriginalIntactVerified)
                    throw new InvalidOperationException("Violación de INV-01: el SHA-256 del archivo original no coincide.");
            }

            VerifyOriginal();
            var outputSize = new FileInfo(tempPath).Length;
            var published = SafeFilePolicy.PublishOutputs(
                stagedPaths, outputDirectory, job.InputPath, job.Options.ConflictPolicy, VerifyOriginal, ct);
            job.OutputPath = published.FirstOrDefault();
            job.AdditionalOutputPaths.AddRange(published.Skip(1));
            if (published.Count == 0)
                job.ProbeSummary = "Trabajo omitido porque un destino ya existía (Skip).";
            else
                job.OutputSizeBytes = outputSize;
            job.ProgressPercent = 100;
            status = JobStatus.Completed;
        }
        catch (OperationCanceledException)
        {
            await VerifyOriginalOnAbortAsync(job);
            job.OutputPath = null;
            status = JobStatus.Cancelled;
            job.ErrorMessage = "Operación cancelada por el usuario.";
        }
        catch (Exception ex)
        {
            await VerifyOriginalOnAbortAsync(job);
            job.OutputPath = null;
            job.ErrorMessage = ex.Message;
        }
        finally
        {
            if (stagingDirectory is not null)
            {
                try { Directory.Delete(stagingDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            lock (_gate)
            {
                if (gateAcquired)
                {
                    _running--;
                    SignalCapacityChanged();
                }
                execution.Cancellation.Dispose();
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.Status = status;
                execution.Completion.TrySetResult(job);
                _executions.Remove(job.Id);
            }
            Notify(job);
        }
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    private async Task ValidateTemporaryOutputBeforePublishAsync(
        string tempPath,
        string expectedTargetFormat,
        ConversionJob job,
        CancellationToken ct)
    {
        if (!File.Exists(tempPath))
        {
            throw new InvalidDataException("El motor no generó el archivo temporal esperado.");
        }

        var tempInfo = new FileInfo(tempPath);
        if (tempInfo.Length == 0)
        {
            throw new InvalidDataException("El archivo temporal generado está vacío (0 bytes).");
        }

        // 1. Magic byte & format match check
        var sig = MagicByteInspector.InspectFile(tempPath);
        if (!sig.IsValid)
        {
            throw new InvalidDataException($"El archivo temporal no tiene una cabecera válida: {sig.FailureReason}");
        }

        var normalizedDetected = FormatMatrix.NormalizeFormatId(sig.DetectedFormat);
        var normalizedExpected = FormatMatrix.NormalizeFormatId(expectedTargetFormat);

        // Allow m4a/mp4 container equivalence when target is aac/m4a
        bool formatMatches = string.Equals(normalizedDetected, normalizedExpected, StringComparison.OrdinalIgnoreCase)
            || (normalizedExpected == "aac" && normalizedDetected is "aac" or "mp3" or "m4a")
            || (normalizedExpected == "ogg" && normalizedDetected is "ogg" or "opus");

        if (!formatMatches)
        {
            throw new InvalidDataException(
                $"El archivo generado tiene formato '{normalizedDetected.ToUpperInvariant()}', pero se esperaba '{normalizedExpected.ToUpperInvariant()}'.");
        }

        // 2. Structural end-of-file / trailer check for formats with mandatory trailers (PNG IEND, JPEG EOI, GIF trailer, PDF %%EOF)
        await ValidateBinaryStructureTrailerAsync(tempPath, normalizedExpected, ct);

        // 3. Deep engine-level integrity verification (identify / ffprobe / gs) if configured
        if (_validator is not null)
        {
            var deepResult = await _validator.ValidateOutputAsync(tempPath, normalizedExpected, ct);
            if (!deepResult.IsValid)
            {
                throw new InvalidDataException(
                    deepResult.FailureReason ?? "El archivo temporal no superó la validación profunda de estructura.");
            }
            job.ProbeSummary = deepResult.Summary;
        }
        else
        {
            job.ProbeSummary = $"{normalizedExpected.ToUpperInvariant()} verificado ({tempInfo.Length} bytes)";
        }
    }

    private static async Task ValidateBinaryStructureTrailerAsync(string filePath, string format, CancellationToken ct)
    {
        await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        long len = fs.Length;

        if (format == "png")
        {
            // Every valid PNG must end with IEND chunk: 00 00 00 00 49 45 4E 44 AE 42 60 82 (12 bytes)
            if (len < 24)
                throw new InvalidDataException("Archivo PNG truncado (tamaño menor al mínimo estructural).");
            byte[] tail = new byte[12];
            fs.Seek(-12, SeekOrigin.End);
            await fs.ReadExactlyAsync(tail, ct);
            if (tail[4] != (byte)'I' || tail[5] != (byte)'E' || tail[6] != (byte)'N' || tail[7] != (byte)'D')
            {
                throw new InvalidDataException("Archivo PNG corrupto o truncado: falta el bloque final IEND.");
            }
        }
        else if (format == "jpeg")
        {
            // Every valid JPEG must end with EOI marker FF D9
            if (len < 16)
                throw new InvalidDataException("Archivo JPEG truncado.");
            byte[] tail = new byte[2];
            fs.Seek(-2, SeekOrigin.End);
            await fs.ReadExactlyAsync(tail, ct);
            if (tail[0] != 0xFF || tail[1] != 0xD9)
            {
                throw new InvalidDataException("Archivo JPEG corrupto o truncado: falta el marcador EOI (FF D9).");
            }
        }
        else if (format == "gif")
        {
            if (len < 14)
                throw new InvalidDataException("Archivo GIF truncado.");
            byte[] tail = new byte[1];
            fs.Seek(-1, SeekOrigin.End);
            await fs.ReadExactlyAsync(tail, ct);
            if (tail[0] != 0x3B)
            {
                throw new InvalidDataException("Archivo GIF corrupto o truncado: falta el byte final 0x3B.");
            }
        }
        else if (format == "pdf")
        {
            if (len < 32)
                throw new InvalidDataException("Archivo PDF truncado.");
            int tailLen = (int)Math.Min(512, len);
            byte[] tail = new byte[tailLen];
            fs.Seek(-tailLen, SeekOrigin.End);
            await fs.ReadExactlyAsync(tail, ct);
            string tailText = System.Text.Encoding.ASCII.GetString(tail);
            if (!tailText.Contains("%%EOF", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Archivo PDF corrupto o incompleto: falta el marcador %%EOF.");
            }
        }
    }

    private static async Task VerifyOriginalOnAbortAsync(ConversionJob job)
    {
        try
        {
            if (File.Exists(job.InputPath) && !string.IsNullOrWhiteSpace(job.OriginalSha256))
            {
                job.PostOriginalSha256 = await SafeFilePolicy.ComputeSha256Async(job.InputPath);
                job.OriginalIntactVerified = string.Equals(
                    job.OriginalSha256,
                    job.PostOriginalSha256,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            job.OriginalIntactVerified = false;
        }
    }

    private IConversionEngineAdapter ResolveAdapter(string sourceFormat, string targetFormat)
    {
        var srcCat = FormatMatrix.Formats[sourceFormat].Category;
        string requiredEngine = srcCat switch
        {
            MediaCategory.Document => "LibreOffice",
            MediaCategory.Pdf => "PDFEngine",
            MediaCategory.Image or MediaCategory.Svg => "ImageMagick",
            MediaCategory.Video or MediaCategory.Audio => "FFmpeg",
            _ => "ImageMagick"
        };

        return _adapters.FirstOrDefault(a =>
                   string.Equals(a.EngineName, requiredEngine, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"Motor requerido no registrado: {requiredEngine}");
    }

    private void Notify(ConversionJob job)
    {
        if (JobUpdated is not { } updated) return;
        foreach (Action<ConversionJob> subscriber in updated.GetInvocationList())
        {
            try { subscriber(job); }
            catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.ToString()); }
        }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        Task[] pending;
        lock (_gate)
        {
            _disposed = true;
            pending = _executions.Values.Select(execution => (Task)execution.Completion.Task).ToArray();
            foreach (var execution in _executions.Values)
                execution.Cancellation.Cancel();
        }
        await Task.WhenAll(pending).ConfigureAwait(false);
    }
}

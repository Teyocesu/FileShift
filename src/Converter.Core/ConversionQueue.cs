using System.Collections.Concurrent;

namespace FileShift.Core;

public sealed class ConversionQueue : IDisposable
{
    private readonly IReadOnlyList<IConversionEngineAdapter> _adapters;
    private readonly IOutputIntegrityValidator? _validator;
    private SemaphoreSlim _concurrencyGate;
    private readonly ConcurrentDictionary<string, ConversionJob> _jobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobTokens = new();

    public event Action<ConversionJob>? JobUpdated;

    public ConversionQueue(
        IEnumerable<IConversionEngineAdapter> adapters,
        int maxConcurrency = 4,
        IOutputIntegrityValidator? validator = null)
    {
        _adapters = adapters.ToList();
        _validator = validator;
        _concurrencyGate = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 16));
    }

    public void UpdateMaxConcurrency(int maxConcurrency)
    {
        _concurrencyGate = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 16));
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

        _jobs[job.Id] = job;
        var cts = new CancellationTokenSource();
        _jobTokens[job.Id] = cts;

        Notify(job);
        _ = ExecuteJobAsync(job, cts.Token);
        return job;
    }

    public bool CancelJob(string jobId)
    {
        if (_jobTokens.TryRemove(jobId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();
            if (_jobs.TryGetValue(jobId, out var job) &&
                job.Status is not (JobStatus.Completed or JobStatus.Failed))
            {
                job.Status = JobStatus.Cancelled;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ErrorMessage = "Operación cancelada por el usuario sin publicar resultados parciales.";
                Notify(job);
            }
            return true;
        }
        return false;
    }

    public async Task<ConversionJob> WaitForJobAsync(string jobId, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_jobs.TryGetValue(jobId, out var job) &&
                job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            {
                return job;
            }
            await Task.Delay(35, ct);
        }
        throw new OperationCanceledException();
    }

    private async Task ExecuteJobAsync(ConversionJob job, CancellationToken ct)
    {
        string? tempPath = null;
        bool gateAcquired = false;
        try
        {
            await _concurrencyGate.WaitAsync(ct);
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

            if (!FormatMatrix.CanConvert(job.SourceFormat, job.TargetFormat))
            {
                throw new NotSupportedException(
                    $"Conversión no soportada: {job.SourceFormat.ToUpperInvariant()} → {job.TargetFormat.ToUpperInvariant()}.");
            }

            var targetDescriptor = FormatMatrix.Formats[job.TargetFormat];
            var finalOutputPath = SafeFilePolicy.ResolveFinalOutputPath(
                job.InputPath,
                targetDescriptor.Extension,
                job.Options.CustomOutputDirectory,
                job.Options.ConflictPolicy);

            // ConflictPolicy.Skip when destination already exists
            if (finalOutputPath is null)
            {
                job.PostOriginalSha256 = await SafeFilePolicy.ComputeSha256Async(job.InputPath, ct);
                job.OriginalIntactVerified = string.Equals(job.OriginalSha256, job.PostOriginalSha256, StringComparison.OrdinalIgnoreCase);
                if (!job.OriginalIntactVerified)
                {
                    throw new InvalidOperationException("Fallo crítico de invariante INV-01: el hash SHA-256 del archivo original fue alterado.");
                }

                job.ProbeSummary = "Omitido (el destino ya existía y la política es Skip)";
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ProgressPercent = 100;
                Notify(job);
                return;
            }

            tempPath = SafeFilePolicy.CreateTemporaryOutputPath(finalOutputPath, job.Id);

            var adapter = ResolveAdapter(job.SourceFormat, job.TargetFormat);
            if (!await adapter.IsAvailableAsync(ct))
            {
                throw new InvalidOperationException($"El motor requerido '{adapter.EngineName}' no está disponible en el sistema.");
            }

            job.EngineUsed = adapter.EngineName;
            job.Status = JobStatus.Running;
            job.ProgressPercent = 15;
            Notify(job);

            var progress = new Progress<double>(p =>
            {
                job.ProgressPercent = Math.Clamp(p, 1, 95);
                Notify(job);
            });

            await adapter.ConvertAsync(job, tempPath, progress, ct);
            ct.ThrowIfCancellationRequested();

            // P0 FIX: Validate temporary file BEFORE publishing to finalOutputPath!
            // If Overwrite is enabled and tempPath is invalid/corrupted/wrong format,
            // the existing file at finalOutputPath remains 100% untouched!
            job.Status = JobStatus.Validating;
            job.ProgressPercent = 96;
            Notify(job);

            await ValidateTemporaryOutputBeforePublishAsync(tempPath, job.TargetFormat, job, ct);

            // P0 FIX: Verify original file SHA-256 invariance BEFORE and AFTER publishing.
            // Any alteration of the original file is an explicit FAILED state, never Completed.
            var prePublishOriginalSha = await SafeFilePolicy.ComputeSha256Async(job.InputPath, ct);
            if (!string.Equals(job.OriginalSha256, prePublishOriginalSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "Violación crítica de invariante INV-01: el archivo original fue modificado durante la conversión.");
            }

            // Now atomically publish the validated temporary file to finalOutputPath
            SafeFilePolicy.PublishValidatedAtomicOutput(tempPath, finalOutputPath, job.Options.ConflictPolicy);
            tempPath = null; // Published

            job.PostOriginalSha256 = await SafeFilePolicy.ComputeSha256Async(job.InputPath, ct);
            job.OriginalIntactVerified = string.Equals(job.OriginalSha256, job.PostOriginalSha256, StringComparison.OrdinalIgnoreCase);
            if (!job.OriginalIntactVerified)
            {
                throw new InvalidOperationException(
                    "Violación crítica de invariante INV-01 tras publicar: el SHA-256 del archivo original no coincide.");
            }

            job.OutputPath = finalOutputPath;
            job.OutputSizeBytes = new FileInfo(finalOutputPath).Length;
            job.ProgressPercent = 100;
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            Notify(job);
        }
        catch (OperationCanceledException)
        {
            SafeFilePolicy.SafeDeleteTemp(tempPath);
            CleanupAdditionalOutputs(job);
            await VerifyOriginalOnAbortAsync(job);
            job.OutputPath = null;
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = "Operación cancelada por el usuario sin publicar resultados parciales.";
            Notify(job);
        }
        catch (Exception ex)
        {
            SafeFilePolicy.SafeDeleteTemp(tempPath);
            CleanupAdditionalOutputs(job);
            await VerifyOriginalOnAbortAsync(job);
            job.OutputPath = null;
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = ex.Message;
            Notify(job);
        }
        finally
        {
            _jobTokens.TryRemove(job.Id, out _);
            if (gateAcquired)
            {
                _concurrencyGate.Release();
            }
        }
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

    private static void CleanupAdditionalOutputs(ConversionJob job)
    {
        foreach (var extra in job.AdditionalOutputPaths)
        {
            SafeFilePolicy.SafeDeleteTemp(extra);
        }
        job.AdditionalOutputPaths.Clear();
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

    private void Notify(ConversionJob job) => JobUpdated?.Invoke(job);

    public void Dispose()
    {
        foreach (var cts in _jobTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }
        _concurrencyGate.Dispose();
    }
}

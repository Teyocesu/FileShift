using System.Collections.Concurrent;

namespace FileShift.Core;

public sealed class ConversionQueue : IDisposable
{
    private readonly IReadOnlyList<IConversionEngineAdapter> _adapters;
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly ConcurrentDictionary<string, ConversionJob> _jobs = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobTokens = new();

    public event Action<ConversionJob>? JobUpdated;

    public ConversionQueue(IEnumerable<IConversionEngineAdapter> adapters, int maxConcurrency = 4)
    {
        _adapters = adapters.ToList();
        _concurrencyGate = new SemaphoreSlim(Math.Clamp(maxConcurrency, 1, 16));
    }

    public IReadOnlyCollection<ConversionJob> Jobs => _jobs.Values
        .OrderByDescending(j => j.CreatedAt)
        .ToList();

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
            await Task.Delay(50, ct);
        }
        throw new OperationCanceledException();
    }

    private async Task ExecuteJobAsync(ConversionJob job, CancellationToken ct)
    {
        string? tempPath = null;
        await _concurrencyGate.WaitAsync(ct);
        try
        {
            job.Status = JobStatus.Preparing;
            job.StartedAt = DateTimeOffset.UtcNow;
            Notify(job);

            var inspection = MagicByteInspector.InspectFile(job.InputPath);
            if (!inspection.IsValid)
            {
                throw new InvalidDataException(inspection.FailureReason ?? "Formato de entrada inválido.");
            }

            job.SourceFormat = inspection.DetectedFormat;
            job.InputSizeBytes = new FileInfo(job.InputPath).Length;
            job.OriginalSha256 = await SafeFilePolicy.ComputeSha256Async(job.InputPath, ct);

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

            if (finalOutputPath is null)
            {
                job.Status = JobStatus.Completed;
                job.CompletedAt = DateTimeOffset.UtcNow;
                job.ProgressPercent = 100;
                Notify(job);
                return;
            }

            job.OutputPath = finalOutputPath;
            tempPath = SafeFilePolicy.CreateTemporaryOutputPath(finalOutputPath, job.Id);

            var adapter = ResolveAdapter(job.SourceFormat, job.TargetFormat);
            job.EngineUsed = adapter.EngineName;
            job.Status = JobStatus.Running;
            Notify(job);

            var progress = new Progress<double>(p =>
            {
                job.ProgressPercent = Math.Clamp(p, 0, 99);
                Notify(job);
            });

            await adapter.ConvertAsync(job, tempPath, progress, ct);

            job.Status = JobStatus.Validating;
            Notify(job);

            SafeFilePolicy.PublishAtomicOutput(tempPath, finalOutputPath, job.Options.ConflictPolicy);

            var outputInspection = MagicByteInspector.InspectFile(finalOutputPath);
            if (!outputInspection.IsValid)
            {
                SafeFilePolicy.SafeDeleteTemp(finalOutputPath);
                throw new InvalidDataException("El archivo de salida generado no superó la validación de cabecera.");
            }

            var postOriginalSha256 = await SafeFilePolicy.ComputeSha256Async(job.InputPath, ct);
            job.OriginalIntactVerified = string.Equals(job.OriginalSha256, postOriginalSha256, StringComparison.OrdinalIgnoreCase);
            job.OutputSizeBytes = new FileInfo(finalOutputPath).Length;
            job.ProgressPercent = 100;
            job.Status = JobStatus.Completed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            Notify(job);
        }
        catch (OperationCanceledException)
        {
            SafeFilePolicy.SafeDeleteTemp(tempPath);
            job.Status = JobStatus.Cancelled;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = "Operación cancelada por el usuario.";
            Notify(job);
        }
        catch (Exception ex)
        {
            SafeFilePolicy.SafeDeleteTemp(tempPath);
            job.Status = JobStatus.Failed;
            job.CompletedAt = DateTimeOffset.UtcNow;
            job.ErrorMessage = ex.Message;
            Notify(job);
        }
        finally
        {
            _jobTokens.TryRemove(job.Id, out _);
            _concurrencyGate.Release();
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

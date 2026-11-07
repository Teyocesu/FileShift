using System.Collections.Concurrent;

namespace FileShift.Core;

public sealed class FolderAutomationService : IDisposable
{
    private readonly ConversionQueue _queue;
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _processedFiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _generatedOutputs = new(StringComparer.OrdinalIgnoreCase);

    public FolderAutomationService(ConversionQueue queue)
    {
        _queue = queue;
        _queue.JobUpdated += OnJobUpdated;
    }

    public void SyncRules(IEnumerable<AutomationRule> rules)
    {
        foreach (var existing in _watchers.Values)
        {
            existing.EnableRaisingEvents = false;
            existing.Dispose();
        }
        _watchers.Clear();

        foreach (var rule in rules.Where(r => r.Enabled && Directory.Exists(r.SourceDirectory)))
        {
            var watcher = new FileSystemWatcher(rule.SourceDirectory)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.CreationTime | NotifyFilters.LastWrite
            };

            watcher.Created += async (_, e) => await HandleFileCandidateAsync(rule, e.FullPath);
            watcher.Renamed += async (_, e) => await HandleFileCandidateAsync(rule, e.FullPath);
            watcher.EnableRaisingEvents = true;
            _watchers[rule.Id] = watcher;
        }
    }

    public async Task<ConversionJob?> HandleFileCandidateAsync(AutomationRule rule, string fullPath, CancellationToken ct = default)
    {
        if (!rule.Enabled || string.IsNullOrWhiteSpace(fullPath)) return null;

        var fileName = Path.GetFileName(fullPath);
        if (fileName.StartsWith('.') || fileName.Contains(".fileshift.tmp.", StringComparison.OrdinalIgnoreCase))
            return null;

        var normalizedPath = Path.GetFullPath(fullPath);

        // Prevent infinite conversion loops on our own outputs
        if (_generatedOutputs.ContainsKey(normalizedPath))
            return null;

        var ext = Path.GetExtension(normalizedPath).TrimStart('.').ToLowerInvariant();
        if (ext == "jpg") ext = "jpeg";

        // Do not convert a file that already matches the target format
        var targetNorm = FormatMatrix.NormalizeFormatId(rule.TargetFormat);
        if (string.Equals(ext, targetNorm, StringComparison.OrdinalIgnoreCase))
            return null;

        if (rule.AllowedInputFormats.Count > 0 &&
            !rule.AllowedInputFormats.Any(f => string.Equals(FormatMatrix.NormalizeFormatId(f), ext, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        // Deduplicate rapid duplicate FileSystemWatcher events
        if (_processedFiles.TryGetValue(normalizedPath, out var lastSeen) &&
            (DateTimeOffset.UtcNow - lastSeen) < TimeSpan.FromSeconds(10))
        {
            return null;
        }
        _processedFiles[normalizedPath] = DateTimeOffset.UtcNow;

        // Wait until file finishes copying/writing on disk
        if (!await WaitForFileWriteStabilityAsync(normalizedPath, ct))
            return null;

        var options = new ConversionOptions(
            ConflictPolicy: rule.ConflictPolicy,
            CustomOutputDirectory: rule.OutputDirectory
        );

        return _queue.Enqueue(normalizedPath, targetNorm, options);
    }

    public void RegisterGeneratedOutput(string outputPath)
    {
        if (!string.IsNullOrWhiteSpace(outputPath))
        {
            _generatedOutputs[Path.GetFullPath(outputPath)] = 1;
        }
    }

    private void OnJobUpdated(ConversionJob job)
    {
        if (!string.IsNullOrWhiteSpace(job.OutputPath))
        {
            RegisterGeneratedOutput(job.OutputPath);
        }
    }

    private static async Task<bool> WaitForFileWriteStabilityAsync(string filePath, CancellationToken ct)
    {
        long previousLength = -1;
        int stableCount = 0;

        for (int attempt = 0; attempt < 25; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(filePath)) return false;

            try
            {
                using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.None);
                var currentLength = stream.Length;
                if (currentLength > 0 && currentLength == previousLength)
                {
                    stableCount++;
                    if (stableCount >= 2) return true;
                }
                else
                {
                    stableCount = 0;
                    previousLength = currentLength;
                }
            }
            catch (IOException)
            {
                stableCount = 0;
            }

            await Task.Delay(150, ct);
        }

        return false;
    }

    public void Dispose()
    {
        _queue.JobUpdated -= OnJobUpdated;
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }
        _watchers.Clear();
    }
}

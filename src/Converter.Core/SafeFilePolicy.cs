using System.Security.Cryptography;

namespace FileShift.Core;

public static class SafeFilePolicy
{
    /// <summary>
    /// Computes the SHA-256 hash of a file to guarantee the original file invariant (INV-01).
    /// </summary>
    public static async Task<string> ComputeSha256Async(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    internal static string GetOutputDirectory(string inputPath, string? customOutputDirectory)
        => Path.GetFullPath(string.IsNullOrWhiteSpace(customOutputDirectory)
            ? Path.GetDirectoryName(inputPath)!
            : customOutputDirectory);

    internal static IReadOnlyList<string> PublishOutputs(
        IReadOnlyList<string> stagedPaths,
        string outputDirectory,
        string originalPath,
        ConflictPolicy policy,
        Action verifyOriginal,
        CancellationToken ct)
    {
        // Serialize cooperating publishers, including independent CLI invocations on Windows.
        using var mutex = new Mutex(false, "FileShift.OutputPublication");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (mutex.WaitOne(50)) break;
            }
            catch (AbandonedMutexException)
            {
                break; // The OS granted ownership. Existing recovery files stay on disk.
            }
        }

        try
        {
            var published = new List<(string Path, string? Backup)>();
            try
            {
                foreach (var stagedPath in stagedPaths)
                {
                    ct.ThrowIfCancellationRequested();
                    var destination = Path.Combine(outputDirectory, Path.GetFileName(stagedPath));
                    if (string.Equals(destination, originalPath, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("El destino coincide con el archivo original.");

                    var candidate = destination;
                    for (var suffix = 1; ; suffix++)
                    {
                        if (Path.Exists(candidate))
                        {
                            if (policy == ConflictPolicy.Skip)
                            {
                                RollBack(published);
                                verifyOriginal();
                                ct.ThrowIfCancellationRequested();
                                return [];
                            }

                            if (policy == ConflictPolicy.Overwrite)
                            {
                                if (!File.Exists(candidate))
                                    throw new IOException($"El destino no es un archivo: {candidate}");

                                var backup = Path.Combine(outputDirectory, $".fileshift.bak.{Guid.NewGuid():N}");
                                // If replacement itself fails, retain any backup the OS created.
                                try
                                {
                                    File.Replace(stagedPath, candidate, backup);
                                }
                                catch (Exception ex) when (File.Exists(backup) && ex is IOException or UnauthorizedAccessException)
                                {
                                    if (!Path.Exists(candidate))
                                    {
                                        try { File.Move(backup, candidate); }
                                        catch (IOException) { }
                                        catch (UnauthorizedAccessException) { }
                                    }
                                    if (File.Exists(backup))
                                        throw new IOException($"No se pudo reemplazar {candidate}. Respaldo: {backup}", ex);
                                    throw;
                                }
                                published.Add((candidate, backup));
                                break;
                            }

                            candidate = Path.Combine(outputDirectory,
                                $"{Path.GetFileNameWithoutExtension(destination)} ({suffix}){Path.GetExtension(destination)}");
                            continue;
                        }

                        try
                        {
                            File.Move(stagedPath, candidate);
                            published.Add((candidate, null));
                            break;
                        }
                        catch (IOException) when (Path.Exists(candidate))
                        {
                            suffix--;
                        }
                    }
                }

                verifyOriginal();
                ct.ThrowIfCancellationRequested();
            }
            catch (Exception ex)
            {
                try { RollBack(published); }
                catch (IOException rollbackFailure)
                {
                    throw new IOException(rollbackFailure.Message, new AggregateException(ex, rollbackFailure));
                }
                throw;
            }

            foreach (var (_, backup) in published)
                SafeDeleteTemp(backup);
            return published.Select(output => output.Path).ToArray();
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void RollBack(List<(string Path, string? Backup)> published)
    {
        var failures = new List<string>();
        for (var index = published.Count - 1; index >= 0; index--)
        {
            var (path, backup) = published[index];
            try
            {
                if (backup is null)
                    File.Delete(path);
                else
                    File.Move(backup, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failures.Add(backup is null
                    ? $"No se pudo retirar {path}: {ex.Message}"
                    : $"No se pudo restaurar {path}. Respaldo conservado: {backup}. {ex.Message}");
            }
        }
        published.Clear();
        if (failures.Count > 0)
            throw new IOException(string.Join(Environment.NewLine, failures));
    }

    public static void SafeDeleteTemp(string? tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempFilePath)) return;
        try { File.Delete(tempFilePath); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

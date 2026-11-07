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

    /// <summary>
    /// Resolves the final output path in the target directory according to the configured ConflictPolicy.
    /// Never overwrites an existing file unless ConflictPolicy.Overwrite is explicitly specified.
    /// Never returns a path identical to inputPath.
    /// </summary>
    public static string? ResolveFinalOutputPath(
        string inputPath,
        string targetExtension,
        string? customOutputDir,
        ConflictPolicy conflictPolicy)
    {
        var fullInput = Path.GetFullPath(inputPath);
        var baseDir = !string.IsNullOrWhiteSpace(customOutputDir)
            ? Path.GetFullPath(customOutputDir)
            : Path.GetDirectoryName(fullInput) ?? Directory.GetCurrentDirectory();

        Directory.CreateDirectory(baseDir);

        var stem = Path.GetFileNameWithoutExtension(fullInput);
        var ext = targetExtension.TrimStart('.').ToLowerInvariant();
        if (ext == "jpeg") ext = "jpg";

        var candidate = Path.Combine(baseDir, $"{stem}.{ext}");

        // INV-01: Never allow output path to equal input path even if extensions match
        if (PathsEqual(candidate, fullInput))
        {
            candidate = Path.Combine(baseDir, $"{stem} (converted).{ext}");
        }

        if (!File.Exists(candidate))
        {
            return candidate;
        }

        return conflictPolicy switch
        {
            ConflictPolicy.Skip => null,
            ConflictPolicy.Overwrite => candidate,
            ConflictPolicy.AutoRename or _ => FindNextAvailablePath(baseDir, stem, ext, fullInput)
        };
    }

    public static string CreateTemporaryOutputPath(string finalOutputPath, string jobId)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath)) ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(finalOutputPath);
        return Path.Combine(dir, $".fileshift.tmp.{jobId}{ext}");
    }

    /// <summary>
    /// Atomically publishes a pre-validated temporary output file to finalOutputPath.
    /// If overwriting an existing destination file, backs up the existing file first and restores it
    /// if the atomic move fails, ensuring a pre-existing valid file is never lost.
    /// </summary>
    public static void PublishValidatedAtomicOutput(string validatedTempFilePath, string finalOutputPath, ConflictPolicy policy)
    {
        if (!File.Exists(validatedTempFilePath))
        {
            throw new IOException("El archivo temporal validado no existe.");
        }

        var info = new FileInfo(validatedTempFilePath);
        if (info.Length == 0)
        {
            SafeDeleteTemp(validatedTempFilePath);
            throw new IOException("El archivo temporal tiene 0 bytes; publicación abortada.");
        }

        bool destinationExists = File.Exists(finalOutputPath);
        if (destinationExists && policy != ConflictPolicy.Overwrite)
        {
            SafeDeleteTemp(validatedTempFilePath);
            throw new IOException("El archivo de destino ya existe y la política actual prohíbe sobrescribir.");
        }

        string? backupPath = null;
        try
        {
            if (destinationExists && policy == ConflictPolicy.Overwrite)
            {
                backupPath = $"{finalOutputPath}.fileshift.bak.{Guid.NewGuid():N}";
                File.Copy(finalOutputPath, backupPath, overwrite: true);
            }

            File.Move(validatedTempFilePath, finalOutputPath, overwrite: policy == ConflictPolicy.Overwrite);

            if (backupPath is not null)
            {
                SafeDeleteTemp(backupPath);
            }
        }
        catch
        {
            if (backupPath is not null && File.Exists(backupPath) && !File.Exists(finalOutputPath))
            {
                try { File.Move(backupPath, finalOutputPath, overwrite: true); } catch { }
            }
            SafeDeleteTemp(backupPath);
            throw;
        }
    }

    public static void SafeDeleteTemp(string? tempFilePath)
    {
        if (string.IsNullOrWhiteSpace(tempFilePath)) return;
        try
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors on locked handles
        }
    }

    private static bool PathsEqual(string pathA, string pathB)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.GetFullPath(pathA), Path.GetFullPath(pathB), comparison);
    }

    private static string FindNextAvailablePath(string directory, string stem, string ext, string fullInput)
    {
        for (int index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}).{ext}");
            if (!File.Exists(candidate) && !PathsEqual(candidate, fullInput))
            {
                return candidate;
            }
        }
        var uniqueSuffix = Guid.NewGuid().ToString("N")[..6];
        return Path.Combine(directory, $"{stem} ({uniqueSuffix}).{ext}");
    }
}

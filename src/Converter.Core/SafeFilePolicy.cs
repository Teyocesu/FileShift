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
    /// </summary>
    public static string? ResolveFinalOutputPath(
        string inputPath,
        string targetExtension,
        string? customOutputDir,
        ConflictPolicy conflictPolicy)
    {
        var baseDir = !string.IsNullOrWhiteSpace(customOutputDir)
            ? customOutputDir
            : Path.GetDirectoryName(Path.GetFullPath(inputPath)) ?? Directory.GetCurrentDirectory();

        Directory.CreateDirectory(baseDir);

        var stem = Path.GetFileNameWithoutExtension(inputPath);
        var ext = targetExtension.TrimStart('.').ToLowerInvariant();
        if (ext == "jpeg") ext = "jpg";

        var candidate = Path.Combine(baseDir, $"{stem}.{ext}");

        // Never allow output path to equal input path
        if (string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(inputPath), StringComparison.OrdinalIgnoreCase))
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
            ConflictPolicy.AutoRename or _ => FindNextAvailablePath(baseDir, stem, ext)
        };
    }

    public static string CreateTemporaryOutputPath(string finalOutputPath, string jobId)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(finalOutputPath)) ?? Path.GetTempPath();
        var ext = Path.GetExtension(finalOutputPath);
        return Path.Combine(dir, $".fileshift.tmp.{jobId}{ext}");
    }

    public static void PublishAtomicOutput(string tempFilePath, string finalOutputPath, ConflictPolicy policy)
    {
        if (!File.Exists(tempFilePath))
        {
            throw new IOException("El archivo temporal de salida no fue generado por el motor.");
        }

        var info = new FileInfo(tempFilePath);
        if (info.Length == 0)
        {
            SafeDeleteTemp(tempFilePath);
            throw new IOException("El motor produjo un archivo vacío de 0 bytes; operación abortada por seguridad.");
        }

        bool overwrite = policy == ConflictPolicy.Overwrite;
        File.Move(tempFilePath, finalOutputPath, overwrite);
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

    private static string FindNextAvailablePath(string directory, string stem, string ext)
    {
        for (int index = 1; index < 10_000; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}).{ext}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }
        return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N[..6]}).{ext}");
    }
}

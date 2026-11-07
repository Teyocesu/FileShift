using System.Diagnostics;

namespace FileShift.Infrastructure;

public sealed record EngineDiagnostic(
    string Name,
    bool Available,
    string Version,
    string BinaryPath,
    string LicenseNotice
);

public static class DoctorService
{
    public static async Task<IReadOnlyList<EngineDiagnostic>> RunDiagnosticsAsync(CancellationToken ct = default)
    {
        var results = new List<EngineDiagnostic>
        {
            await ProbeBinaryAsync("FFmpeg", ["ffmpeg"], "-version", "LGPL v2.1+ / GPL v2+", ct),
            await ProbeBinaryAsync("ffprobe", ["ffprobe"], "-version", "LGPL v2.1+ / GPL v2+", ct),
            await ProbeBinaryAsync("ImageMagick", ["magick", "convert"], "-version", "ImageMagick License (Apache-2.0 compatible)", ct),
            await ProbeBinaryAsync("PDF Engine (Ghostscript / PDFium)", ["gs", "gswin64c"], "--version", "BSD-3-Clause (PDFium) / AGPLv3 (Ghostscript CLI)", ct),
            await ProbeBinaryAsync("LibreOffice (Opcional)", ["soffice", "libreoffice"], "--version", "MPL 2.0", ct)
        };
        return results;
    }

    private static async Task<EngineDiagnostic> ProbeBinaryAsync(
        string displayName,
        string[] binaryCandidates,
        string versionArg,
        string license,
        CancellationToken ct)
    {
        foreach (var bin in binaryCandidates)
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = bin,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.StartInfo.ArgumentList.Add(versionArg);
                proc.Start();
                var stdout = await proc.StandardOutput.ReadLineAsync(ct) ?? "";
                await proc.WaitForExitAsync(ct);

                if (proc.ExitCode == 0)
                {
                    return new EngineDiagnostic(displayName, true, stdout.Trim(), bin, license);
                }
            }
            catch
            {
                // Try next candidate
            }
        }

        return new EngineDiagnostic(displayName, false, "No detectado en PATH", "N/A", license);
    }
}

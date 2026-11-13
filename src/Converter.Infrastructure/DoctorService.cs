using System.Diagnostics;
using FileShift.Core;

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
            await ProbeBinaryAsync("ImageMagick", OperatingSystem.IsWindows() ? ["magick"] : ["convert"], "-version", "ImageMagick License (Apache-2.0 compatible)", ct),
            await ProbeBinaryAsync("PDF Engine (Ghostscript / PDFium)", OperatingSystem.IsWindows() ? ["gswin64c"] : ["gs"], "--version", "BSD-3-Clause (PDFium) / AGPLv3 (Ghostscript CLI)", ct),
            await ProbeBinaryAsync("LibreOffice (Opcional)", LibreOfficeAdapter.FindSofficeBinary() is { } soffice ? [soffice] : [], "--version", "MPL 2.0", ct)
        };
        return results;
    }

    public static void UpdateShellConversionMatrix(IReadOnlyList<EngineDiagnostic> diagnostics)
    {
        var engines = diagnostics.Where(diagnostic => diagnostic.Available).Select(diagnostic => diagnostic.Name).ToHashSet();
        var available = new Dictionary<MediaCategory, bool>
        {
            [MediaCategory.Image] = engines.Contains("ImageMagick"),
            [MediaCategory.Svg] = engines.Contains("ImageMagick"),
            [MediaCategory.Video] = engines.Contains("FFmpeg") && engines.Contains("ffprobe"),
            [MediaCategory.Audio] = engines.Contains("FFmpeg") && engines.Contains("ffprobe"),
            [MediaCategory.Pdf] = engines.Contains("PDF Engine (Ghostscript / PDFium)") && engines.Contains("ImageMagick"),
            [MediaCategory.Document] = engines.Contains("LibreOffice (Opcional)")
        };

        UserSettingsStore.WriteShellConversionMatrix(FormatMatrix.Formats.Select(pair =>
        {
            var targets = available[pair.Value.Category]
                ? FormatMatrix.GetSupportedTargets(pair.Key, available[MediaCategory.Document])
                    .Where(target => target != "pdf" || engines.Contains("PDF Engine (Ghostscript / PDFium)"))
                : [];
            return $"{pair.Key}={pair.Value.Category.ToString().ToLowerInvariant()}:{string.Join(",", targets)}";
        }));
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
                var (output, _) = await EngineProcess.RunAsync(proc, ct);
                var stdout = output.Split('\n', 2)[0];

                if (proc.ExitCode == 0)
                {
                    return new EngineDiagnostic(displayName, true, stdout.Trim(), bin, license);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                // Try next candidate
            }
        }

        return new EngineDiagnostic(displayName, false, "No detectado en PATH", "N/A", license);
    }
}

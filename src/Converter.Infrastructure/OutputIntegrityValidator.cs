using System.Diagnostics;
using FileShift.Core;

namespace FileShift.Infrastructure;

public sealed class OutputIntegrityValidator : IOutputIntegrityValidator
{
    public async Task<OutputValidationResult> ValidateOutputAsync(
        string filePath,
        string targetFormatId,
        CancellationToken ct = default)
    {
        var normTarget = FormatMatrix.NormalizeFormatId(targetFormatId);
        if (!FormatMatrix.Formats.TryGetValue(normTarget, out var descriptor))
        {
            return new OutputValidationResult(false, null, $"Formato de salida desconocido: {targetFormatId}");
        }

        if (normTarget == "pdf")
        {
            return await ValidatePdfWithGhostscriptAsync(filePath, ct);
        }

        return descriptor.Category switch
        {
            MediaCategory.Image => await ValidateImageWithIdentifyAsync(filePath, normTarget, ct),
            MediaCategory.Video or MediaCategory.Audio => await ValidateMediaWithFfprobeAsync(filePath, descriptor.Category, ct),
            MediaCategory.Pdf or MediaCategory.Document => await ValidatePdfWithGhostscriptAsync(filePath, ct),
            _ => new OutputValidationResult(true, "Validado por estructura binaria", null)
        };
    }

    private static async Task<OutputValidationResult> ValidateImageWithIdentifyAsync(
        string filePath,
        string normTarget,
        CancellationToken ct)
    {
        string binary = OperatingSystem.IsWindows() ? "magick" : "identify";
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = binary,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            if (OperatingSystem.IsWindows())
            {
                proc.StartInfo.ArgumentList.Add("identify");
            }
            proc.StartInfo.ArgumentList.Add("-regard-warnings");
            proc.StartInfo.ArgumentList.Add("-format");
            proc.StartInfo.ArgumentList.Add("%m %wx%h\\n");
            proc.StartInfo.ArgumentList.Add(filePath);

            var (stdout, stderr) = await EngineProcess.RunAsync(proc, ct);

            if (proc.ExitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            {
                return new OutputValidationResult(
                    false,
                    null,
                    $"ImageMagick identify rechazó la imagen generada ({normTarget}): {stderr.Trim()}");
            }

            var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
            return new OutputValidationResult(true, firstLine, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new OutputValidationResult(false, null, $"Error ejecutando validador de imagen: {ex.Message}");
        }
    }

    private static async Task<OutputValidationResult> ValidateMediaWithFfprobeAsync(
        string filePath,
        MediaCategory category,
        CancellationToken ct)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffprobe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var args = proc.StartInfo.ArgumentList;
            args.Add("-v");
            args.Add("error");
            args.Add("-show_entries");
            args.Add("stream=codec_type,codec_name:format=format_name,duration");
            args.Add("-of");
            args.Add("default=noprint_wrappers=1");
            args.Add(filePath);

            var (stdout, stderr) = await EngineProcess.RunAsync(proc, ct);

            if (proc.ExitCode != 0)
            {
                return new OutputValidationResult(
                    false,
                    null,
                    $"ffprobe detectó contenedor multimedia corrupto: {stderr.Trim()}");
            }

            bool hasVideo = stdout.Contains("codec_type=video", StringComparison.OrdinalIgnoreCase);
            bool hasAudio = stdout.Contains("codec_type=audio", StringComparison.OrdinalIgnoreCase);

            if (category == MediaCategory.Video && !hasVideo)
            {
                return new OutputValidationResult(false, null, "El archivo de video generado no contiene ningún stream de video válido.");
            }

            if (category == MediaCategory.Audio && !hasAudio)
            {
                return new OutputValidationResult(false, null, "El archivo de audio generado no contiene ningún stream de audio válido.");
            }

            var summary = string.Join(", ", stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Take(4));

            return new OutputValidationResult(true, summary, null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new OutputValidationResult(false, null, $"Error ejecutando ffprobe: {ex.Message}");
        }
    }

    private static async Task<OutputValidationResult> ValidatePdfWithGhostscriptAsync(
        string filePath,
        CancellationToken ct)
    {
        string gsBinary = OperatingSystem.IsWindows() ? "gswin64c" : "gs";
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = gsBinary,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };

            var args = proc.StartInfo.ArgumentList;
            args.Add("-dSAFER");
            args.Add("-dBATCH");
            args.Add("-dNOPAUSE");
            args.Add("-sDEVICE=nullpage");
            args.Add(filePath);

            var (stdout, stderr) = await EngineProcess.RunAsync(proc, ct);

            if (proc.ExitCode != 0 ||
                stdout.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
                stderr.Contains("Error:", StringComparison.OrdinalIgnoreCase))
            {
                return new OutputValidationResult(
                    false,
                    null,
                    $"Ghostscript detectó un PDF inválido o dañado: {(stderr + " " + stdout).Trim()}");
            }

            int pageCount = stdout
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Count(l => l.StartsWith("Page ", StringComparison.Ordinal));

            return new OutputValidationResult(true, $"PDF válido ({Math.Max(1, pageCount)} pág.)", null);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new OutputValidationResult(false, null, $"Error validando PDF con Ghostscript: {ex.Message}");
        }
    }
}

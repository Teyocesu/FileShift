using System.Diagnostics;
using System.Globalization;
using FileShift.Core;

namespace FileShift.Infrastructure;

public sealed class PdfAdapter : IConversionEngineAdapter
{
    public string EngineName => "PDFEngine";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        foreach (var bin in new[] { "gs", "gswin64c" })
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
                proc.StartInfo.ArgumentList.Add("--version");
                proc.Start();
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode == 0) return true;
            }
            catch
            {
                // Continue checking next binary
            }
        }
        return false;
    }

    public async Task ConvertAsync(
        ConversionJob job,
        string tempOutputPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        progress?.Report(15);
        var target = FormatMatrix.NormalizeFormatId(job.TargetFormat);
        string device = target == "jpeg" ? "jpeg" : "png16m";
        string gsBinary = OperatingSystem.IsWindows() ? "gswin64c" : "gs";

        int totalPages = await InspectPdfPageCountAsync(gsBinary, job.InputPath, ct);
        var (firstPage, lastPage) = ResolvePageRange(job.Options.PageRange, totalPages);

        // Render first requested page into tempOutputPath (primary atomic output)
        await RenderPdfPageAsync(gsBinary, device, job.InputPath, firstPage, tempOutputPath, ct);
        progress?.Report(65);

        // If multi-page PDF and range spans multiple pages, render pages 2..N atomically as well
        if (lastPage > firstPage)
        {
            var targetExt = FormatMatrix.Formats[target].Extension;
            var outputDir = string.IsNullOrWhiteSpace(job.Options.CustomOutputDirectory)
                ? (Path.GetDirectoryName(job.InputPath) ?? Environment.CurrentDirectory)
                : job.Options.CustomOutputDirectory;
            var baseStem = Path.GetFileNameWithoutExtension(job.InputPath);

            for (int page = firstPage + 1; page <= lastPage; page++)
            {
                ct.ThrowIfCancellationRequested();
                var pageInputDummy = Path.Combine(outputDir, $"{baseStem}_p{page}.pdf");
                var pageFinalPath = SafeFilePolicy.ResolveFinalOutputPath(
                    pageInputDummy,
                    targetExt,
                    outputDir,
                    job.Options.ConflictPolicy)
                    ?? throw new InvalidOperationException($"El archivo de página {page} fue omitido por política de conflicto.");

                var pageTemp = SafeFilePolicy.CreateTemporaryOutputPath(pageFinalPath, $"{job.Id}_p{page}");
                try
                {
                    await RenderPdfPageAsync(gsBinary, device, job.InputPath, page, pageTemp, ct);
                    var inspection = MagicByteInspector.InspectFile(pageTemp);
                    if (!inspection.IsValid || FormatMatrix.NormalizeFormatId(inspection.DetectedFormat) != target)
                    {
                        throw new InvalidDataException($"La página {page} del PDF no generó una imagen '{target}' válida.");
                    }

                    SafeFilePolicy.PublishValidatedAtomicOutput(
                        pageTemp,
                        pageFinalPath,
                        job.Options.ConflictPolicy);

                    job.AdditionalOutputPaths.Add(pageFinalPath);
                }
                catch
                {
                    SafeFilePolicy.SafeDeleteTemp(pageTemp);
                    throw;
                }
            }
        }

        progress?.Report(95);
    }

    private static (int FirstPage, int LastPage) ResolvePageRange(string? pageRange, int totalPages)
    {
        if (string.IsNullOrWhiteSpace(pageRange))
        {
            return (1, Math.Max(1, totalPages));
        }

        var parts = pageRange.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length >= 1 && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int first))
        {
            int last = parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lp)
                ? lp
                : first;

            first = Math.Clamp(first, 1, Math.Max(1, totalPages));
            last = Math.Clamp(last, first, Math.Max(first, totalPages));
            return (first, last);
        }

        return (1, Math.Max(1, totalPages));
    }

    private static async Task<int> InspectPdfPageCountAsync(
        string gsBinary,
        string inputPath,
        CancellationToken ct)
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
        args.Add(inputPath);

        proc.Start();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stderr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var combined = $"{stdout}\n{stderr}";
        if (proc.ExitCode != 0 ||
            combined.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("encrypted", StringComparison.OrdinalIgnoreCase) ||
            combined.Contains("Error:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"El archivo PDF no se pudo procesar (posiblemente cifrado o dañado): {combined.Trim()}");
        }

        int pages = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(line => line.StartsWith("Page ", StringComparison.Ordinal));

        return Math.Max(1, pages);
    }

    private static async Task RenderPdfPageAsync(
        string gsBinary,
        string device,
        string inputPath,
        int pageNumber,
        string outputPath,
        CancellationToken ct)
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
        args.Add($"-sDEVICE={device}");
        args.Add("-r150");
        args.Add($"-dFirstPage={pageNumber}");
        args.Add($"-dLastPage={pageNumber}");
        args.Add($"-sOutputFile={outputPath}");
        args.Add(inputPath);

        proc.Start();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        var stdErr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0 ||
            stdout.Contains("Error:", StringComparison.OrdinalIgnoreCase) ||
            stdErr.Contains("Error:", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Motor PDF falló al renderizar página {pageNumber} (código {proc.ExitCode}): {(stdErr + " " + stdout).Trim()}");
        }
    }
}

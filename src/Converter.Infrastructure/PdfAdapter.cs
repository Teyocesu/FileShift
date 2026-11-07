using System.Diagnostics;
using FileShift.Core;

namespace FileShift.Infrastructure;

public sealed class PdfAdapter : IConversionEngineAdapter
{
    public string EngineName => "PDFEngine";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        foreach (var bin in new[] { "gs", "gswin64c", "magick", "convert" })
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
        progress?.Report(20);
        var target = FormatMatrix.NormalizeFormatId(job.TargetFormat);
        string device = target == "jpeg" ? "jpeg" : "png16m";
        string gsBinary = OperatingSystem.IsWindows() ? "gswin64c" : "gs";

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

        if (!string.IsNullOrWhiteSpace(job.Options.PageRange))
        {
            var parts = job.Options.PageRange.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length >= 1 && int.TryParse(parts[0], out int firstPage))
            {
                args.Add($"-dFirstPage={firstPage}");
                int lastPage = parts.Length >= 2 && int.TryParse(parts[1], out int lp) ? lp : firstPage;
                args.Add($"-dLastPage={lastPage}");
            }
        }
        else
        {
            args.Add("-dFirstPage=1");
            args.Add("-dLastPage=1");
        }

        args.Add($"-sOutputFile={tempOutputPath}");
        args.Add(job.InputPath);

        proc.Start();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        var stdErr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"Motor PDF finalizó con código {proc.ExitCode}: {stdErr.Trim()}");
        }

        progress?.Report(95);
    }
}

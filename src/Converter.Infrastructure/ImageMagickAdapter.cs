using System.Diagnostics;
using System.Globalization;
using FileShift.Core;

namespace FileShift.Infrastructure;

public sealed class ImageMagickAdapter : IConversionEngineAdapter
{
    public string EngineName => "ImageMagick";

    private static string ResolveExecutable()
    {
        return OperatingSystem.IsWindows() ? "magick" : "convert";
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        foreach (var candidate in new[] { "magick", "convert" })
        {
            try
            {
                using var proc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = candidate,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    }
                };
                proc.StartInfo.ArgumentList.Add("-version");
                proc.Start();
                await proc.WaitForExitAsync(ct);
                if (proc.ExitCode == 0) return true;
            }
            catch
            {
                // Try next binary name
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

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ResolveExecutable(),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var args = proc.StartInfo.ArgumentList;
        args.Add(job.InputPath);

        // Apply EXIF orientation automatically
        args.Add("-auto-orient");

        var target = FormatMatrix.NormalizeFormatId(job.TargetFormat);
        var targetDescriptor = FormatMatrix.Formats[target];

        // Explicit background compositing policy when target format does not support alpha (e.g. JPEG, BMP, PDF)
        if (!targetDescriptor.SupportsAlpha)
        {
            args.Add("-background");
            args.Add(string.IsNullOrWhiteSpace(job.Options.BackgroundHex) ? "#FFFFFF" : job.Options.BackgroundHex);
            args.Add("-alpha");
            args.Add("remove");
            args.Add("-alpha");
            args.Add("off");
        }

        if (job.Options.LongestEdgePx.HasValue && job.Options.LongestEdgePx.Value > 0)
        {
            int edge = job.Options.LongestEdgePx.Value;
            args.Add("-resize");
            args.Add($"{edge}x{edge}>");
        }

        if (target == "ico")
        {
            args.Add("-define");
            args.Add("icon:auto-resize=256,128,64,48,32,16");
        }

        if (job.Options.StripMetadata)
        {
            args.Add("-strip");
        }

        args.Add("-quality");
        args.Add(Math.Clamp(job.Options.Quality, 1, 100).ToString(CultureInfo.InvariantCulture));

        args.Add(tempOutputPath);

        proc.Start();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        progress?.Report(65);
        var stdErr = await proc.StandardError.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"ImageMagick finalizó con código {proc.ExitCode}: {stdErr.Trim()}");
        }

        progress?.Report(95);
    }
}

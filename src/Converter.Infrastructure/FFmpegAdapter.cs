using System.Diagnostics;
using System.Globalization;
using FileShift.Core;

namespace FileShift.Infrastructure;

public sealed class FFmpegAdapter : IConversionEngineAdapter
{
    public string EngineName => "FFmpeg";

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var proc = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            proc.StartInfo.ArgumentList.Add("-version");
            proc.Start();
            await proc.WaitForExitAsync(ct);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task ConvertAsync(
        ConversionJob job,
        string tempOutputPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        double? durationSeconds = await ProbeDurationSecondsAsync(job.InputPath, ct);

        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        var args = proc.StartInfo.ArgumentList;
        args.Add("-y");
        args.Add("-hide_banner");
        args.Add("-nostdin");
        args.Add("-i");
        args.Add(job.InputPath);
        args.Add("-progress");
        args.Add("pipe:1");

        var target = FormatMatrix.NormalizeFormatId(job.TargetFormat);
        var targetCat = FormatMatrix.Formats[target].Category;
        int crf = Math.Clamp((int)Math.Round(51 - (job.Options.Quality / 100.0) * 33), 18, 36);

        if (target == "gif")
        {
            // Two-stream filtergraph palette generation for high-quality animated GIF
            var scaleExpr = job.Options.LongestEdgePx.HasValue
                ? $"scale={job.Options.LongestEdgePx.Value}:-1:flags=lanczos,"
                : "scale=640:-1:flags=lanczos,";
            args.Add("-vf");
            args.Add($"fps=12,{scaleExpr}split[s0][s1];[s0]palettegen[p];[s1][p]paletteuse");
            args.Add("-loop");
            args.Add("0");
        }
        else if (targetCat == MediaCategory.Video)
        {
            if (job.Options.LongestEdgePx.HasValue)
            {
                int edge = job.Options.LongestEdgePx.Value;
                args.Add("-vf");
                args.Add($"scale='min({edge},iw)':-2");
            }

            switch (target)
            {
                case "webm":
                    args.Add("-c:v"); args.Add("libvpx-vp9");
                    args.Add("-crf"); args.Add(crf.ToString(CultureInfo.InvariantCulture));
                    args.Add("-b:v"); args.Add("0");
                    args.Add("-c:a"); args.Add("libopus");
                    break;
                case "avi":
                    args.Add("-c:v"); args.Add("mpeg4");
                    args.Add("-q:v"); args.Add("4");
                    args.Add("-c:a"); args.Add("libmp3lame");
                    break;
                default: // mp4, mkv, mov
                    args.Add("-c:v"); args.Add("libx264");
                    args.Add("-preset"); args.Add("fast");
                    args.Add("-crf"); args.Add(crf.ToString(CultureInfo.InvariantCulture));
                    args.Add("-pix_fmt"); args.Add("yuv420p");
                    args.Add("-c:a"); args.Add("aac");
                    args.Add("-b:a"); args.Add($"{job.Options.AudioBitrateKbps ?? 192}k");
                    break;
            }
        }
        else if (targetCat == MediaCategory.Audio)
        {
            args.Add("-vn");
            if (job.Options.AudioSampleRate.HasValue)
            {
                args.Add("-ar");
                args.Add(job.Options.AudioSampleRate.Value.ToString(CultureInfo.InvariantCulture));
            }

            int bitrate = job.Options.AudioBitrateKbps ?? 192;
            switch (target)
            {
                case "mp3":
                    args.Add("-c:a"); args.Add("libmp3lame");
                    args.Add("-b:a"); args.Add($"{bitrate}k");
                    break;
                case "wav":
                    args.Add("-c:a"); args.Add("pcm_s16le");
                    break;
                case "flac":
                    args.Add("-c:a"); args.Add("flac");
                    break;
                case "aac":
                case "m4a":
                    args.Add("-c:a"); args.Add("aac");
                    args.Add("-b:a"); args.Add($"{bitrate}k");
                    break;
                case "ogg":
                    args.Add("-c:a"); args.Add("libvorbis");
                    args.Add("-b:a"); args.Add($"{bitrate}k");
                    break;
                case "opus":
                    args.Add("-c:a"); args.Add("libopus");
                    args.Add("-b:a"); args.Add($"{bitrate}k");
                    break;
            }
        }

        if (job.Options.StripMetadata)
        {
            args.Add("-map_metadata");
            args.Add("-1");
        }

        args.Add(tempOutputPath);

        proc.Start();
        using var reg = ct.Register(() =>
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
        });

        var stdErrTask = proc.StandardError.ReadToEndAsync(ct);

        while (await proc.StandardOutput.ReadLineAsync(ct) is { } line)
        {
            if (durationSeconds is > 0 && line.StartsWith("out_time_ms=", StringComparison.Ordinal))
            {
                if (long.TryParse(line.AsSpan("out_time_ms=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out long us))
                {
                    double pct = (us / 1_000_000.0) / durationSeconds.Value * 100.0;
                    progress?.Report(Math.Clamp(pct, 1, 99));
                }
            }
        }

        await proc.WaitForExitAsync(ct);
        var stdErr = await stdErrTask;

        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException($"FFmpeg finalizó con código {proc.ExitCode}: {stdErr.Trim()}");
        }
    }

    public static async Task<double?> ProbeDurationSecondsAsync(string inputPath, CancellationToken ct = default)
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
            args.Add("-v"); args.Add("error");
            args.Add("-show_entries"); args.Add("format=duration");
            args.Add("-of"); args.Add("default=noprint_wrappers=1:nokey=1");
            args.Add(inputPath);

            proc.Start();
            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode == 0 &&
                double.TryParse(output.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds) &&
                seconds > 0)
            {
                return seconds;
            }
        }
        catch
        {
            // Ignore probe errors if stream has no duration header
        }
        return null;
    }
}

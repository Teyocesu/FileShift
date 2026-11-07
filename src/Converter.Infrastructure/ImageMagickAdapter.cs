using System.Diagnostics;
using System.Globalization;
using System.Text;
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

        var target = FormatMatrix.NormalizeFormatId(job.TargetFormat);
        if (target == "pdf")
        {
            await ConvertImageToPdfSafeAsync(job, tempOutputPath, progress, ct);
            return;
        }

        await RunImageMagickConvertAsync(job, target, tempOutputPath, progress, ct);
    }

    private static async Task RunImageMagickConvertAsync(
        ConversionJob job,
        string target,
        string outputPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
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

        args.Add(outputPath);

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

    private static async Task ConvertImageToPdfSafeAsync(
        ConversionJob job,
        string tempOutputPath,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var tempJpeg = tempOutputPath + ".dct.jpg";
        try
        {
            await RunImageMagickConvertAsync(job, "jpeg", tempJpeg, progress, ct);
            var (width, height) = await ProbeImageDimensionsAsync(tempJpeg, ct);
            var jpegBytes = await File.ReadAllBytesAsync(tempJpeg, ct);

            await BuildSingleImagePdf14Async(tempOutputPath, jpegBytes, width, height, ct);
            progress?.Report(95);
        }
        finally
        {
            SafeFilePolicy.SafeDeleteTemp(tempJpeg);
        }
    }

    private static async Task<(int Width, int Height)> ProbeImageDimensionsAsync(string imagePath, CancellationToken ct)
    {
        string binary = OperatingSystem.IsWindows() ? "magick" : "identify";
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
        proc.StartInfo.ArgumentList.Add("-format");
        proc.StartInfo.ArgumentList.Add("%w %h");
        proc.StartInfo.ArgumentList.Add(imagePath);

        proc.Start();
        var stdout = await proc.StandardOutput.ReadToEndAsync(ct);
        await proc.WaitForExitAsync(ct);

        var parts = stdout.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int w) &&
            int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) &&
            w > 0 && h > 0)
        {
            return (w, h);
        }

        return (612, 792);
    }

    private static async Task BuildSingleImagePdf14Async(
        string outputPdfPath,
        byte[] jpegBytes,
        int width,
        int height,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        void WriteAscii(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            ms.Write(bytes, 0, bytes.Length);
        }

        WriteAscii("%PDF-1.4\n%\xE2\xE3\xCF\xD3\n");

        var offsets = new long[6];

        offsets[1] = ms.Position;
        WriteAscii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

        offsets[2] = ms.Position;
        WriteAscii("2 0 obj\n<< /Type /Pages /Kids [3 0 R] /Count 1 >>\nendobj\n");

        offsets[3] = ms.Position;
        WriteAscii(string.Create(CultureInfo.InvariantCulture,
            $"3 0 obj\n<< /Type /Page /Parent 2 0 R /Resources << /XObject << /Im0 4 0 R >> >> /MediaBox [0 0 {width} {height}] /Contents 5 0 R >>\nendobj\n"));

        offsets[4] = ms.Position;
        WriteAscii(string.Create(CultureInfo.InvariantCulture,
            $"4 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpegBytes.Length} >>\nstream\n"));
        ms.Write(jpegBytes, 0, jpegBytes.Length);
        WriteAscii("\nendstream\nendobj\n");

        string contentStream = string.Create(CultureInfo.InvariantCulture, $"q\n{width} 0 0 {height} 0 0 cm\n/Im0 Do\nQ\n");
        var contentBytes = Encoding.ASCII.GetBytes(contentStream);

        offsets[5] = ms.Position;
        WriteAscii(string.Create(CultureInfo.InvariantCulture,
            $"5 0 obj\n<< /Length {contentBytes.Length} >>\nstream\n"));
        ms.Write(contentBytes, 0, contentBytes.Length);
        WriteAscii("\nendstream\nendobj\n");

        long xrefOffset = ms.Position;
        WriteAscii("xref\n0 6\n0000000000 65535 f \n");
        for (int i = 1; i <= 5; i++)
        {
            WriteAscii(string.Create(CultureInfo.InvariantCulture, $"{offsets[i]:D10} 00000 n \n"));
        }

        WriteAscii(string.Create(CultureInfo.InvariantCulture,
            $"trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF\n"));

        await File.WriteAllBytesAsync(outputPdfPath, ms.ToArray(), ct);
    }
}

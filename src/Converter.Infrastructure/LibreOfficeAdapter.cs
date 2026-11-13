using System.Diagnostics;
using FileShift.Core;

namespace FileShift.Infrastructure;

public sealed class LibreOfficeAdapter : IConversionEngineAdapter
{
    public string EngineName => "LibreOffice";

    public static string? FindSofficeBinary()
    {
        var candidates = new[]
        {
            "soffice",
            "libreoffice",
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe"
        };

        foreach (var candidate in candidates)
        {
            if (Path.IsPathRooted(candidate) && File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        return Task.FromResult(FindSofficeBinary() is not null);
    }

    public async Task ConvertAsync(
        ConversionJob job,
        string tempOutputPath,
        IProgress<double>? progress,
        CancellationToken ct = default)
    {
        var binary = FindSofficeBinary()
            ?? throw new InvalidOperationException("LibreOffice no está instalado. El soporte de documentos Office es opcional.");

        var workDir = Path.Combine(Path.GetTempPath(), $"fileshift_lo_{job.Id}");
        Directory.CreateDirectory(workDir);

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

            var args = proc.StartInfo.ArgumentList;
            args.Add("--headless");
            args.Add("--convert-to");
            args.Add("pdf");
            args.Add("--outdir");
            args.Add(workDir);
            args.Add(job.InputPath);

            await EngineProcess.RunAsync(proc, ct);

            var expectedPdf = Path.Combine(workDir, $"{Path.GetFileNameWithoutExtension(job.InputPath)}.pdf");
            if (proc.ExitCode != 0 || !File.Exists(expectedPdf))
            {
                throw new InvalidOperationException("LibreOffice headless no pudo generar el archivo PDF.");
            }

            File.Move(expectedPdf, tempOutputPath, overwrite: true);
            progress?.Report(95);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); } catch { }
        }
    }
}

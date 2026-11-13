using System.ComponentModel;
using System.Diagnostics;

namespace FileShift.Infrastructure;

internal static class EngineProcess
{
    internal static async Task<(string Output, string Error)> RunAsync(
        Process process, CancellationToken ct, Func<StreamReader, Task<string>>? readOutput = null)
    {
        ct.ThrowIfCancellationRequested();
        if (OperatingSystem.IsWindows())
        {
            var relative = process.StartInfo.FileName switch
            {
                "ffmpeg" or "ffprobe" => Path.Combine("ffmpeg", process.StartInfo.FileName + ".exe"),
                "magick" => Path.Combine("imagemagick", "magick.exe"),
                "gswin64c" => Path.Combine("ghostscript", "bin", "gswin64c.exe"),
                _ => null
            };
            if (relative is not null)
            {
                var engine = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "FileShift", "engines", relative);
                if (File.Exists(engine)) process.StartInfo.FileName = engine;
            }
        }
        process.Start();
        var output = readOutput is null ? process.StandardOutput.ReadToEndAsync() : readOutput(process.StandardOutput);
        var error = process.StandardError.ReadToEndAsync();
        using var registration = ct.Register(() =>
        {
            try { process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (Win32Exception) { }
        });

        await process.WaitForExitAsync(CancellationToken.None);
        await Task.WhenAll(output, error);
        ct.ThrowIfCancellationRequested();
        return (await output, await error);
    }
}

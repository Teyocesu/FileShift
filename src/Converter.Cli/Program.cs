using FileShift.Core;
using FileShift.Infrastructure;

namespace FileShift.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        return command switch
        {
            "formats" => RunFormatsCommand(),
            "doctor" => await RunDoctorCommandAsync(),
            "convert" => await RunConvertCommandAsync(args.Skip(1).ToArray()),
            _ => FailWithMessage($"Comando desconocido '{command}'. Ejecutá 'fileshift --help'.")
        };
    }

    private static int RunFormatsCommand()
    {
        Console.WriteLine("FileShift — Matriz de Formatos y Conversiones Soportadas");
        Console.WriteLine(new string('─', 76));
        foreach (var (id, desc) in FormatMatrix.Formats.OrderBy(kv => kv.Value.Category).ThenBy(kv => kv.Key))
        {
            if (id == "jpg") continue;
            var targets = string.Join(", ", FormatMatrix.GetSupportedTargets(id, libreOfficeInstalled: true));
            Console.WriteLine($"  {desc.DisplayName,-16} (.{desc.Extension,-4}) [{desc.Category,-8}] -> {targets}");
        }
        return 0;
    }

    private static async Task<int> RunDoctorCommandAsync()
    {
        Console.WriteLine("FileShift Doctor — Diagnóstico de Motores, Codecs e Integración Shell");
        Console.WriteLine(new string('─', 76));
        var diagnostics = await DoctorService.RunDiagnosticsAsync();
        foreach (var diag in diagnostics)
        {
            var status = diag.Available ? "[OK]" : "[NO INSTALADO]";
            Console.WriteLine($"{status,-16} {diag.Name,-34} | {diag.Version}");
        }
        Console.WriteLine(new string('─', 76));
        Console.WriteLine($"Plataforma: {Environment.OSVersion} | Concurrencia recomendada: {Math.Clamp(Environment.ProcessorCount / 2, 2, 8)} hilos");
        return diagnostics.Any(d => d.Name == "FFmpeg" && d.Available) ? 0 : 2;
    }

    private static async Task<int> RunConvertCommandAsync(string[] args)
    {
        var inputFiles = new List<string>();
        string? targetFormat = null;
        string? presetName = null;
        int quality = 85;
        int? longestEdge = null;
        string? outputDir = null;
        ConflictPolicy conflictPolicy = ConflictPolicy.AutoRename;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--to" when i + 1 < args.Length:
                    targetFormat = args[++i];
                    break;
                case "--preset" when i + 1 < args.Length:
                    presetName = args[++i];
                    break;
                case "--quality" when i + 1 < args.Length && int.TryParse(args[++i], out int q):
                    quality = Math.Clamp(q, 1, 100);
                    break;
                case "--resize" when i + 1 < args.Length && int.TryParse(args[++i], out int px):
                    longestEdge = px;
                    break;
                case "--out-dir" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--overwrite":
                    conflictPolicy = ConflictPolicy.Overwrite;
                    break;
                case "--":
                    inputFiles.AddRange(args.Skip(i + 1));
                    i = args.Length;
                    break;
                default:
                    if (!args[i].StartsWith('-'))
                        inputFiles.Add(args[i]);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(presetName))
        {
            var defaults = new UserSettings().Presets;
            var preset = defaults.FirstOrDefault(p =>
                p.Id.Equals(presetName, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Contains(presetName, StringComparison.OrdinalIgnoreCase));

            if (preset is not null)
            {
                targetFormat ??= preset.TargetFormat;
                quality = preset.Quality;
                longestEdge ??= preset.LongestEdgePx;
            }
        }

        if (inputFiles.Count == 0 || string.IsNullOrWhiteSpace(targetFormat))
        {
            return FailWithMessage("Uso: fileshift convert <archivo1> [archivo2...] --to <formato> [--quality 85]");
        }

        using var queue = new ConversionQueue(
            new IConversionEngineAdapter[]
            {
                new ImageMagickAdapter(),
                new FFmpegAdapter(),
                new PdfAdapter(),
                new LibreOfficeAdapter()
            });

        var options = new ConversionOptions(
            Quality: quality,
            LongestEdgePx: longestEdge,
            ConflictPolicy: conflictPolicy,
            CustomOutputDirectory: outputDir,
            PresetName: presetName
        );

        var jobs = inputFiles.Select(file => queue.Enqueue(file, targetFormat, options)).ToList();
        int failures = 0;

        foreach (var job in jobs)
        {
            var finished = await queue.WaitForJobAsync(job.Id);
            if (finished.Status == JobStatus.Completed)
            {
                Console.WriteLine($"[OK] {finished.InputPath} -> {finished.OutputPath} ({finished.OutputSizeBytes} bytes, original intacto: {finished.OriginalIntactVerified})");
            }
            else
            {
                Console.Error.WriteLine($"[ERROR] {finished.InputPath}: {finished.ErrorMessage}");
                failures++;
            }
        }

        return failures == 0 ? 0 : 1;
    }

    private static int FailWithMessage(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            FileShift CLI — Convertidor Universal de Archivos (100% Local & Open Source)

            Comandos:
              fileshift convert <archivo...> --to <formato> [--quality 1-100] [--resize px] [--out-dir ruta]
              fileshift convert <archivo...> --preset <nombre>
              fileshift formats
              fileshift doctor
            """);
    }
}

using System.Text.Json;
using FileShift.Core;
using FileShift.Infrastructure;

namespace FileShift.Cli;

public static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
            "formats" => RunFormatsCommand(args.Skip(1).ToArray()),
            "doctor" => await RunDoctorCommandAsync(args.Skip(1).ToArray()),
            "convert" => await RunConvertCommandAsync(args.Skip(1).ToArray()),
            _ => FailWithMessage($"Comando desconocido '{command}'. Ejecutá 'fileshift --help'.")
        };
    }

    private static int RunFormatsCommand(string[] args)
    {
        bool json = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        bool libreOfficeInstalled = LibreOfficeAdapter.FindSofficeBinary() is not null;

        if (json)
        {
            var payload = FormatMatrix.Formats
                .Where(kv => kv.Key != "jpg")
                .OrderBy(kv => kv.Value.Category)
                .ThenBy(kv => kv.Key)
                .Select(kv => new
                {
                    id = kv.Key,
                    displayName = kv.Value.DisplayName,
                    extension = kv.Value.Extension,
                    category = kv.Value.Category.ToString(),
                    supportsAlpha = kv.Value.SupportsAlpha,
                    targets = FormatMatrix.GetSupportedTargets(kv.Key, libreOfficeInstalled: true),
                    activeTargets = FormatMatrix.GetSupportedTargets(kv.Key, libreOfficeInstalled)
                });
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
            return 0;
        }

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

    private static async Task<int> RunDoctorCommandAsync(string[] args)
    {
        bool json = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        var diagnostics = await DoctorService.RunDiagnosticsAsync();
        DoctorService.UpdateShellConversionMatrix(diagnostics);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(diagnostics, JsonOpts));
            return diagnostics.Where(d => !d.Name.Contains("Opcional", StringComparison.Ordinal)).All(d => d.Available) ? 0 : 2;
        }

        Console.WriteLine("FileShift Doctor — Diagnóstico de Motores, Codecs e Integración Shell");
        Console.WriteLine(new string('─', 76));
        foreach (var diag in diagnostics)
        {
            var status = diag.Available ? "[OK]" : "[NO INSTALADO]";
            Console.WriteLine($"{status,-16} {diag.Name,-34} | {diag.Version}");
        }
        Console.WriteLine(new string('─', 76));
        Console.WriteLine($"Plataforma: {Environment.OSVersion} | Concurrencia recomendada: {Math.Clamp(Environment.ProcessorCount / 2, 2, 8)} hilos");
        return diagnostics.Where(d => !d.Name.Contains("Opcional", StringComparison.Ordinal)).All(d => d.Available) ? 0 : 2;
    }

    private static async Task<int> RunConvertCommandAsync(string[] args)
    {
        var settings = UserSettingsStore.Load();
        var inputFiles = new List<string>();
        string? targetFormat = null;
        string? presetName = null;
        int? quality = null;
        int? bitrate = null;
        int? longestEdge = null;
        string? outputDir = settings.SaveNextToOriginal ? null : settings.CustomOutputDirectory;
        string? pageRange = null;
        bool stripMetadata = false;
        bool jsonOutput = false;
        bool fromExplorer = false;
        ConflictPolicy conflictPolicy = settings.DefaultConflictPolicy;

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
                case "--resize" when i + 1 < args.Length && int.TryParse(args[++i], out int px) && px > 0:
                    longestEdge = px;
                    break;
                case "--out-dir" when i + 1 < args.Length:
                    outputDir = args[++i];
                    break;
                case "--pages" or "--page-range" when i + 1 < args.Length:
                    pageRange = args[++i];
                    break;
                case "--strip-metadata":
                    stripMetadata = true;
                    break;
                case "--overwrite":
                    conflictPolicy = ConflictPolicy.Overwrite;
                    break;
                case "--skip":
                    conflictPolicy = ConflictPolicy.Skip;
                    break;
                case "--auto-rename":
                    conflictPolicy = ConflictPolicy.AutoRename;
                    break;
                case "--explorer":
                    fromExplorer = true;
                    break;
                case "--json":
                    jsonOutput = true;
                    break;
                case "--":
                    inputFiles.AddRange(args.Skip(i + 1));
                    i = args.Length;
                    break;
                default:
                    if (args[i].StartsWith('-')) return FailWithMessage($"Opción inválida: {args[i]}");
                    inputFiles.Add(args[i]);
                    break;
            }
        }

        if (!string.IsNullOrWhiteSpace(presetName))
        {
            var preset = settings.Presets.FirstOrDefault(p =>
                p.Id.Equals(presetName, StringComparison.OrdinalIgnoreCase) ||
                p.Name.Equals(presetName, StringComparison.OrdinalIgnoreCase) ||
                p.Id.Equals($"preset-{presetName}", StringComparison.OrdinalIgnoreCase));

            if (preset is null) return FailWithMessage($"Preset desconocido: {presetName}");
            targetFormat ??= preset.TargetFormat;
            quality ??= preset.Quality;
            longestEdge ??= preset.LongestEdgePx;
            bitrate = preset.AudioBitrateKbps;
            stripMetadata |= preset.StripMetadata;
        }

        if (inputFiles.Count == 0 || string.IsNullOrWhiteSpace(targetFormat))
        {
            return FailWithMessage("Uso: fileshift convert <archivo1> [archivo2...] --to <formato> [--quality 85] [--resize px] [--pages 1-3] [--json]");
        }

        using var queue = new ConversionQueue(
            new IConversionEngineAdapter[]
            {
                new ImageMagickAdapter(),
                new FFmpegAdapter(),
                new PdfAdapter(),
                new LibreOfficeAdapter()
            },
            settings.MaxConcurrency,
            new OutputIntegrityValidator());

        var options = new ConversionOptions(
            Quality: quality ?? settings.DefaultQuality,
            AudioBitrateKbps: bitrate,
            LongestEdgePx: longestEdge,
            StripMetadata: stripMetadata,
            ConflictPolicy: conflictPolicy,
            CustomOutputDirectory: outputDir,
            PageRange: pageRange,
            PresetName: presetName
        );

        var jobs = inputFiles.Select(file => queue.Enqueue(file, targetFormat, options)).ToList();
        var finishedJobs = new List<ConversionJob>();
        int failures = 0;

        foreach (var job in jobs)
        {
            var finished = await queue.WaitForJobAsync(job.Id);
            finishedJobs.Add(finished);
            if (finished.Status == JobStatus.Completed)
            {
                if (!jsonOutput)
                {
                    Console.WriteLine($"[OK] {finished.InputPath} -> {finished.OutputPath} ({finished.OutputSizeBytes} bytes, original intacto: {finished.OriginalIntactVerified})");
                }
            }
            else
            {
                if (!jsonOutput)
                {
                    Console.Error.WriteLine($"[ERROR] {finished.InputPath}: {finished.ErrorMessage}");
                }
                failures++;
            }
        }

        if (jsonOutput)
        {
            var payload = finishedJobs.Select(j => new
            {
                id = j.Id,
                inputPath = j.InputPath,
                sourceFormat = j.SourceFormat,
                targetFormat = j.TargetFormat,
                status = j.Status.ToString(),
                outputPath = j.OutputPath,
                additionalOutputPaths = j.AdditionalOutputPaths,
                inputSizeBytes = j.InputSizeBytes,
                outputSizeBytes = j.OutputSizeBytes,
                originalSha256 = j.OriginalSha256,
                postOriginalSha256 = j.PostOriginalSha256,
                originalIntactVerified = j.OriginalIntactVerified,
                probeSummary = j.ProbeSummary,
                errorMessage = j.ErrorMessage,
                durationMs = j.StartedAt.HasValue && j.CompletedAt.HasValue
                    ? (int)Math.Max(1, (j.CompletedAt.Value - j.StartedAt.Value).TotalMilliseconds)
                    : 0
            });
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
        }

        if (fromExplorer && failures > 0 && OperatingSystem.IsWindows())
        {
            var errors = finishedJobs.Where(job => job.Status != JobStatus.Completed).Take(5)
                .Select(job => $"{job.FileName}: {job.ErrorMessage}");
            MessageBoxW(IntPtr.Zero, string.Join(Environment.NewLine, errors), "FileShift", 0x00000010);
        }
        return failures == 0 ? 0 : 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true)]
    private static extern int MessageBoxW(IntPtr owner, string text, string caption, uint type);

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
              fileshift convert <archivo...> --to <formato> [--quality 1-100] [--resize px] [--pages 1-N] [--out-dir ruta] [--overwrite] [--json]
              fileshift convert <archivo...> --preset <nombre> [--json]
              fileshift formats [--json]
              fileshift doctor [--json]
            """);
    }
}

namespace FileShift.Core;

public enum MediaCategory
{
    Unknown = 0,
    Image = 1,
    Svg = 2,
    Video = 3,
    Audio = 4,
    Pdf = 5,
    Document = 6
}

public enum JobStatus
{
    Queued,
    Preparing,
    Running,
    Validating,
    Completed,
    Failed,
    Cancelled
}

public enum ConflictPolicy
{
    AutoRename,
    Skip,
    Overwrite
}

public sealed record FormatDescriptor(
    string Id,
    string DisplayName,
    string Extension,
    MediaCategory Category,
    string RequiredEngine,
    bool IsLossless,
    bool SupportsAlpha,
    bool SupportsAnimation,
    string DefaultMimeType
);

public sealed record ConversionOptions(
    int Quality = 85,
    int? LongestEdgePx = null,
    int? AudioBitrateKbps = null,
    int? AudioSampleRate = null,
    bool StripMetadata = false,
    string BackgroundHex = "#FFFFFF",
    string? PageRange = null,
    ConflictPolicy ConflictPolicy = ConflictPolicy.AutoRename,
    string? CustomOutputDirectory = null,
    string? PresetName = null
);

public sealed record ConversionPreset(
    string Id,
    string Name,
    string TargetFormat,
    int Quality,
    int? LongestEdgePx,
    int? AudioBitrateKbps,
    bool StripMetadata = false
);

public sealed record AutomationRule(
    string Id,
    string Name,
    bool Enabled,
    string SourceDirectory,
    IReadOnlyList<string> AllowedInputFormats,
    string TargetFormat,
    string? PresetId,
    string? OutputDirectory,
    ConflictPolicy ConflictPolicy,
    string? LastResult = null,
    DateTimeOffset? LastTriggeredAt = null
);

public sealed class ConversionJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    public required string InputPath { get; init; }
    public required string SourceFormat { get; init; }
    public required string TargetFormat { get; init; }
    public string? OutputPath { get; set; }
    public string EngineUsed { get; set; } = "Pending";
    public JobStatus Status { get; set; } = JobStatus.Queued;
    public double ProgressPercent { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public long InputSizeBytes { get; set; }
    public long? OutputSizeBytes { get; set; }
    public string? OriginalSha256 { get; set; }
    public bool OriginalIntactVerified { get; set; }
    public ConversionOptions Options { get; init; } = new();
}

public sealed class UserSettings
{
    public bool SaveNextToOriginal { get; set; } = true;
    public string? CustomOutputDirectory { get; set; }
    public int DefaultQuality { get; set; } = 85;
    public int MaxConcurrency { get; set; } = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    public ConflictPolicy DefaultConflictPolicy { get; set; } = ConflictPolicy.AutoRename;
    public bool ShowNotifications { get; set; } = true;
    public bool RevealInExplorerWhenDone { get; set; } = false;
    public bool KeepRunningInBackground { get; set; } = true;
    public bool LaunchAtWindowsStartup { get; set; } = false;
    public bool ContextMenuEnabled { get; set; } = true;
    public string Language { get; set; } = "es";

    public Dictionary<string, List<string>> MenuFavorites { get; set; } = new()
    {
        ["image"] = ["jpeg", "png", "webp", "avif"],
        ["svg"] = ["png", "jpeg", "webp"],
        ["video"] = ["mp4", "mov", "gif", "mp3"],
        ["audio"] = ["mp3", "m4a", "wav", "flac"],
        ["pdf"] = ["png", "jpeg"]
    };

    public List<ConversionPreset> Presets { get; set; } =
    [
        new("preset-web-webp", "Imagen para Web (WebP)", "webp", 82, 1920, null),
        new("preset-hq-jpeg", "JPEG Alta Calidad", "jpeg", 92, null, null),
        new("preset-video-mp4", "Video MP4 Compatible", "mp4", 80, 1920, 192),
        new("preset-audio-mp3", "Audio MP3 192 kbps", "mp3", 85, null, 192)
    ];

    public List<AutomationRule> Automations { get; set; } = [];
}

public interface IConversionEngineAdapter
{
    string EngineName { get; }
    Task<bool> IsAvailableAsync(CancellationToken ct = default);
    Task ConvertAsync(
        ConversionJob job,
        string tempOutputPath,
        IProgress<double>? progress,
        CancellationToken ct = default
    );
}

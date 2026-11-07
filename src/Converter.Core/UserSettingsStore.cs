using System.Text.Json;
using System.Text.Json.Serialization;

namespace FileShift.Core;

public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string GetConfigDirectory()
    {
        var envOverride = Environment.GetEnvironmentVariable("FILESHIFT_CONFIG_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            Directory.CreateDirectory(envOverride);
            return Path.GetFullPath(envOverride);
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
        {
            localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        }

        var dir = Path.Combine(localAppData, "FileShift");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetSettingsFilePath() =>
        Path.Combine(GetConfigDirectory(), "settings.json");

    public static string GetMenuCacheFilePath() =>
        Path.Combine(GetConfigDirectory(), "menu-cache.json");

    public static UserSettings Load()
    {
        var path = GetSettingsFilePath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonSerializer.Deserialize<UserSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    EnsureDefaultFavorites(loaded);
                    WriteMenuCacheForShellExtension(loaded);
                    return loaded;
                }
            }
            catch
            {
                // Fall back to default settings if corrupted
            }
        }

        var defaults = CreateDefaultSettings();
        Save(defaults);
        return defaults;
    }

    public static void Save(UserSettings settings)
    {
        EnsureDefaultFavorites(settings);
        var path = GetSettingsFilePath();
        var tempPath = $"{path}.tmp.{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
        WriteMenuCacheForShellExtension(settings);
    }

    /// <summary>
    /// Writes a compact, deterministic cache file consumed by the C++17 Win32 COM extension (FileShift.Shell.dll)
    /// so Explorer context menus reflect user favorite formats and enabled state immediately without JSON parser bloat.
    /// Format:
    /// ENABLED=1
    /// LANG=es
    /// FAV_image=jpeg,png,webp
    /// FAV_svg=png,jpeg,webp
    /// FAV_video=mp4,mov,gif,mp3
    /// FAV_audio=mp3,m4a,wav
    /// FAV_pdf=png,jpeg
    /// FAV_document=pdf
    /// </summary>
    public static void WriteMenuCacheForShellExtension(UserSettings settings)
    {
        var lines = new List<string>
        {
            $"ENABLED={(settings.ContextMenuEnabled ? "1" : "0")}",
            $"LANG={settings.Language}"
        };

        foreach (var category in new[] { "image", "svg", "video", "audio", "pdf", "document" })
        {
            if (settings.MenuFavorites.TryGetValue(category, out var favs) && favs.Count > 0)
            {
                lines.Add($"FAV_{category}={string.Join(",", favs.Select(FormatMatrix.NormalizeFormatId))}");
            }
            else
            {
                lines.Add($"FAV_{category}=");
            }
        }

        var cachePath = GetMenuCacheFilePath();
        var tempCache = $"{cachePath}.tmp.{Guid.NewGuid():N}";
        File.WriteAllLines(tempCache, lines);
        File.Move(tempCache, cachePath, overwrite: true);
    }

    public static UserSettings CreateDefaultSettings()
    {
        var settings = new UserSettings();
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        var videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

        if (string.IsNullOrWhiteSpace(pictures))
            pictures = Path.Combine(GetConfigDirectory(), "Watched");
        if (string.IsNullOrWhiteSpace(videos))
            videos = Path.Combine(GetConfigDirectory(), "Watched");

        var screenshotsDir = Path.Combine(pictures, "Screenshots");
        var capturesDir = Path.Combine(videos, "Captures");

        settings.Automations =
        [
            new AutomationRule(
                Id: "rule-screenshots-webp",
                Name: "Capturas PNG → WebP",
                Enabled: true,
                SourceDirectory: screenshotsDir,
                AllowedInputFormats: ["png"],
                TargetFormat: "webp",
                PresetId: "preset-web-webp",
                OutputDirectory: null,
                ConflictPolicy: ConflictPolicy.AutoRename,
                CopyConvertedFile: true,
                LastResult: "Vigilando carpeta activamente (sin bucles)"
            ),
            new AutomationRule(
                Id: "rule-recordings-mp4",
                Name: "Grabaciones MKV/WebM → MP4",
                Enabled: true,
                SourceDirectory: capturesDir,
                AllowedInputFormats: ["mkv", "webm", "mov"],
                TargetFormat: "mp4",
                PresetId: "preset-video-mp4",
                OutputDirectory: null,
                ConflictPolicy: ConflictPolicy.AutoRename,
                CopyConvertedFile: false,
                LastResult: "Vigilando carpeta activamente"
            )
        ];

        return settings;
    }

    private static void EnsureDefaultFavorites(UserSettings settings)
    {
        var defaults = new UserSettings().MenuFavorites;
        foreach (var (k, v) in defaults)
        {
            if (!settings.MenuFavorites.ContainsKey(k))
            {
                settings.MenuFavorites[k] = [.. v];
            }
        }
    }
}

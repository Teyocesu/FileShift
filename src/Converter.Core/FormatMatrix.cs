namespace FileShift.Core;

public static class FormatMatrix
{
    public static readonly IReadOnlyDictionary<string, FormatDescriptor> Formats =
        new Dictionary<string, FormatDescriptor>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            ["png"] = new("png", "PNG", "png", MediaCategory.Image, "ImageMagick", true, true, false, "image/png"),
            ["jpeg"] = new("jpeg", "JPEG", "jpg", MediaCategory.Image, "ImageMagick", false, false, false, "image/jpeg"),
            ["jpg"] = new("jpeg", "JPEG", "jpg", MediaCategory.Image, "ImageMagick", false, false, false, "image/jpeg"),
            ["webp"] = new("webp", "WebP", "webp", MediaCategory.Image, "ImageMagick", false, true, true, "image/webp"),
            ["avif"] = new("avif", "AVIF", "avif", MediaCategory.Image, "ImageMagick", false, true, false, "image/avif"),
            ["bmp"] = new("bmp", "BMP", "bmp", MediaCategory.Image, "ImageMagick", true, false, false, "image/bmp"),
            ["gif"] = new("gif", "GIF", "gif", MediaCategory.Image, "ImageMagick", false, true, true, "image/gif"),
            ["tiff"] = new("tiff", "TIFF", "tiff", MediaCategory.Image, "ImageMagick", true, true, false, "image/tiff"),
            ["ico"] = new("ico", "ICO", "ico", MediaCategory.Image, "ImageMagick", true, true, false, "image/x-icon"),
            ["heic"] = new("heic", "HEIC", "heic", MediaCategory.Image, "ImageMagick", false, true, false, "image/heic"),
            ["svg"] = new("svg", "SVG", "svg", MediaCategory.Svg, "ImageMagick", true, true, false, "image/svg+xml"),

            // Video
            ["mp4"] = new("mp4", "MP4 (H.264)", "mp4", MediaCategory.Video, "FFmpeg", false, false, true, "video/mp4"),
            ["mkv"] = new("mkv", "MKV", "mkv", MediaCategory.Video, "FFmpeg", false, false, true, "video/x-matroska"),
            ["mov"] = new("mov", "MOV", "mov", MediaCategory.Video, "FFmpeg", false, false, true, "video/quicktime"),
            ["webm"] = new("webm", "WebM (VP9)", "webm", MediaCategory.Video, "FFmpeg", false, true, true, "video/webm"),
            ["avi"] = new("avi", "AVI", "avi", MediaCategory.Video, "FFmpeg", false, false, true, "video/x-msvideo"),

            // Audio
            ["mp3"] = new("mp3", "MP3", "mp3", MediaCategory.Audio, "FFmpeg", false, false, false, "audio/mpeg"),
            ["wav"] = new("wav", "WAV (PCM)", "wav", MediaCategory.Audio, "FFmpeg", true, false, false, "audio/wav"),
            ["flac"] = new("flac", "FLAC", "flac", MediaCategory.Audio, "FFmpeg", true, false, false, "audio/flac"),
            ["aac"] = new("aac", "AAC", "aac", MediaCategory.Audio, "FFmpeg", false, false, false, "audio/aac"),
            ["m4a"] = new("m4a", "M4A", "m4a", MediaCategory.Audio, "FFmpeg", false, false, false, "audio/mp4"),
            ["ogg"] = new("ogg", "OGG Vorbis", "ogg", MediaCategory.Audio, "FFmpeg", false, false, false, "audio/ogg"),
            ["opus"] = new("opus", "Opus", "opus", MediaCategory.Audio, "FFmpeg", false, false, false, "audio/opus"),

            // PDF & Office
            ["pdf"] = new("pdf", "PDF", "pdf", MediaCategory.Pdf, "PDFEngine", true, false, false, "application/pdf"),
            ["docx"] = new("docx", "Word DOCX", "docx", MediaCategory.Document, "LibreOffice", true, false, false, "application/vnd.openxmlformats-officedocument.wordprocessingml.document"),
            ["odt"] = new("odt", "OpenDocument Text", "odt", MediaCategory.Document, "LibreOffice", true, false, false, "application/vnd.oasis.opendocument.text"),
            ["xlsx"] = new("xlsx", "Excel XLSX", "xlsx", MediaCategory.Document, "LibreOffice", true, false, false, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"),
            ["pptx"] = new("pptx", "PowerPoint PPTX", "pptx", MediaCategory.Document, "LibreOffice", true, false, false, "application/vnd.openxmlformats-officedocument.presentationml.presentation")
        };

    public static IReadOnlyList<string> GetSupportedTargets(string sourceFormat, bool libreOfficeInstalled = false)
    {
        var normalized = NormalizeFormatId(sourceFormat);
        if (!Formats.TryGetValue(normalized, out var descriptor))
            return [];

        return descriptor.Category switch
        {
            MediaCategory.Image => new[] { "jpeg", "png", "webp", "avif", "bmp", "gif", "tiff", "ico", "pdf" }
                .Where(t => !string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            MediaCategory.Svg => ["png", "jpeg", "webp", "avif", "bmp", "tiff", "ico", "pdf"],
            MediaCategory.Video => new[] { "mp4", "mkv", "mov", "webm", "avi", "gif", "mp3", "wav", "flac", "aac", "m4a", "ogg", "opus" }
                .Where(t => !string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            MediaCategory.Audio => new[] { "mp3", "wav", "flac", "aac", "m4a", "ogg", "opus" }
                .Where(t => !string.Equals(t, normalized, StringComparison.OrdinalIgnoreCase))
                .ToList(),
            MediaCategory.Pdf => ["png", "jpeg"],
            MediaCategory.Document => libreOfficeInstalled ? ["pdf"] : [],
            _ => []
        };
    }

    public static bool CanConvert(string sourceFormat, string targetFormat, bool libreOfficeInstalled = true)
    {
        var targets = GetSupportedTargets(sourceFormat, libreOfficeInstalled);
        var normalizedTarget = NormalizeFormatId(targetFormat);
        return targets.Contains(normalizedTarget, StringComparer.OrdinalIgnoreCase);
    }

    public static string NormalizeFormatId(string format)
    {
        var clean = format.Trim().TrimStart('.').ToLowerInvariant();
        return clean == "jpg" ? "jpeg" : clean;
    }
}

using System.Text;

namespace FileShift.Core;

public static class MagicByteInspector
{
    public sealed record InspectionResult(
        bool IsValid,
        string DetectedFormat,
        MediaCategory Category,
        string? FailureReason = null
    );

    public static InspectionResult InspectFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return new(false, "unknown", MediaCategory.Unknown, "El archivo de entrada no existe.");
        }

        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Length == 0)
        {
            return new(false, "unknown", MediaCategory.Unknown, "El archivo de entrada está vacío (0 bytes).");
        }

        Span<byte> header = stackalloc byte[64];
        int bytesRead;
        try
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            bytesRead = fs.Read(header);
        }
        catch (Exception ex)
        {
            return new(false, "unknown", MediaCategory.Unknown, $"No se pudo leer la cabecera del archivo: {ex.Message}");
        }

        var slice = header[..bytesRead];
        string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytesRead >= 8 &&
            slice[0] == 0x89 && slice[1] == 0x50 && slice[2] == 0x4E && slice[3] == 0x47 &&
            slice[4] == 0x0D && slice[5] == 0x0A && slice[6] == 0x1A && slice[7] == 0x0A)
        {
            return new(true, "png", MediaCategory.Image);
        }

        // JPEG: FF D8 FF
        if (bytesRead >= 3 && slice[0] == 0xFF && slice[1] == 0xD8 && slice[2] == 0xFF)
        {
            return new(true, "jpeg", MediaCategory.Image);
        }

        // GIF: GIF87a or GIF89a
        if (bytesRead >= 6 &&
            slice[0] == (byte)'G' && slice[1] == (byte)'I' && slice[2] == (byte)'F' &&
            slice[3] == (byte)'8' && (slice[4] == (byte)'7' || slice[4] == (byte)'9') && slice[5] == (byte)'a')
        {
            return new(true, "gif", MediaCategory.Image);
        }

        // BMP: BM
        if (bytesRead >= 2 && slice[0] == (byte)'B' && slice[1] == (byte)'M')
        {
            return new(true, "bmp", MediaCategory.Image);
        }

        // TIFF: II*\0 or MM\0*
        if (bytesRead >= 4 &&
            ((slice[0] == 0x49 && slice[1] == 0x49 && slice[2] == 0x2A && slice[3] == 0x00) ||
             (slice[0] == 0x4D && slice[1] == 0x4D && slice[2] == 0x00 && slice[3] == 0x2A)))
        {
            return new(true, "tiff", MediaCategory.Image);
        }

        // ICO: 00 00 01 00
        if (bytesRead >= 4 && slice[0] == 0x00 && slice[1] == 0x00 && slice[2] == 0x01 && slice[3] == 0x00)
        {
            return new(true, "ico", MediaCategory.Image);
        }

        // RIFF containers (WEBP, WAV, AVI)
        if (bytesRead >= 12 &&
            slice[0] == (byte)'R' && slice[1] == (byte)'I' && slice[2] == (byte)'F' && slice[3] == (byte)'F')
        {
            var fourCc = Encoding.ASCII.GetString(slice.Slice(8, 4));
            if (fourCc == "WEBP") return new(true, "webp", MediaCategory.Image);
            if (fourCc == "WAVE") return new(true, "wav", MediaCategory.Audio);
            if (fourCc == "AVI ") return new(true, "avi", MediaCategory.Video);
        }

        // ISO Base Media File Format (ftyp at offset 4): MP4, MOV, M4A, AVIF, HEIC
        if (bytesRead >= 12 &&
            slice[4] == (byte)'f' && slice[5] == (byte)'t' && slice[6] == (byte)'y' && slice[7] == (byte)'p')
        {
            var brand = Encoding.ASCII.GetString(slice.Slice(8, 4));
            if (brand is "avif" or "avis") return new(true, "avif", MediaCategory.Image);
            if (brand is "heic" or "heix" or "mif1" or "msf1") return new(true, "heic", MediaCategory.Image);
            if (brand is "M4A " or "M4B ") return new(true, "m4a", MediaCategory.Audio);
            if (brand == "qt  " || ext == "mov") return new(true, "mov", MediaCategory.Video);
            return new(true, "mp4", MediaCategory.Video);
        }

        // Matroska / WebM: 1A 45 DF A3
        if (bytesRead >= 4 && slice[0] == 0x1A && slice[1] == 0x45 && slice[2] == 0xDF && slice[3] == 0xA3)
        {
            var asciiWindow = Encoding.ASCII.GetString(slice).ToLowerInvariant();
            if (asciiWindow.Contains("webm") || ext == "webm")
                return new(true, "webm", MediaCategory.Video);
            return new(true, "mkv", MediaCategory.Video);
        }

        // FLAC: fLaC
        if (bytesRead >= 4 && slice[0] == (byte)'f' && slice[1] == (byte)'L' && slice[2] == (byte)'a' && slice[3] == (byte)'C')
        {
            return new(true, "flac", MediaCategory.Audio);
        }

        // OGG / Opus: OggS
        if (bytesRead >= 4 && slice[0] == (byte)'O' && slice[1] == (byte)'g' && slice[2] == (byte)'g' && slice[3] == (byte)'S')
        {
            return new(true, ext == "opus" ? "opus" : "ogg", MediaCategory.Audio);
        }

        // MP3 / AAC (ID3 or ADTS/MPEG frame sync)
        if (bytesRead >= 3 && slice[0] == (byte)'I' && slice[1] == (byte)'D' && slice[2] == (byte)'3')
        {
            return new(true, ext == "aac" ? "aac" : "mp3", MediaCategory.Audio);
        }
        if (bytesRead >= 2 && slice[0] == 0xFF && (slice[1] & 0xE0) == 0xE0)
        {
            return new(true, ext == "aac" ? "aac" : "mp3", MediaCategory.Audio);
        }

        // PDF: %PDF-
        if (bytesRead >= 5 &&
            slice[0] == (byte)'%' && slice[1] == (byte)'P' && slice[2] == (byte)'D' && slice[3] == (byte)'F' && slice[4] == (byte)'-')
        {
            return new(true, "pdf", MediaCategory.Pdf);
        }

        // ZIP-based Office OpenDocument/OpenXML (PK\x03\x04)
        if (bytesRead >= 4 && slice[0] == 0x50 && slice[1] == 0x4B && slice[2] == 0x03 && slice[3] == 0x04)
        {
            if (ext is "docx" or "xlsx" or "pptx" or "odt")
            {
                return new(true, ext, MediaCategory.Document);
            }
        }

        // SVG (XML / <svg)
        var textPrefix = Encoding.UTF8.GetString(slice).TrimStart();
        if (textPrefix.StartsWith("<svg", StringComparison.OrdinalIgnoreCase) ||
            (textPrefix.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase) && ext == "svg"))
        {
            return new(true, "svg", MediaCategory.Svg);
        }

        return new(
            false,
            "unknown",
            MediaCategory.Unknown,
            $"Firma binaria no reconocida o archivo corrupto para la extensión '.{ext}'."
        );
    }
}

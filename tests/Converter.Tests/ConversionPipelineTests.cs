using FileShift.Core;
using Xunit;

namespace FileShift.Tests;

public sealed class ConversionPipelineTests
{
    [Fact]
    public void MagicByteInspector_DetectsValidPngHeader_AndRejectsCorruptFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var validPng = Path.Combine(tempDir, "valid.png");
            File.WriteAllBytes(validPng, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
            var res = MagicByteInspector.InspectFile(validPng);
            Assert.True(res.IsValid);
            Assert.Equal("png", res.DetectedFormat);

            var corruptPng = Path.Combine(tempDir, "corrupt.png");
            File.WriteAllBytes(corruptPng, [0x00, 0x01, 0x02, 0x03, 0x04]);
            var badRes = MagicByteInspector.InspectFile(corruptPng);
            Assert.False(badRes.IsValid);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SafeFilePolicy_AutoRename_NeverOverwritesExistingDestination()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_collision_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var input = Path.Combine(tempDir, "documento Unicode ¡España!.png");
            var existingOut = Path.Combine(tempDir, "documento Unicode ¡España!.webp");
            File.WriteAllBytes(input, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            File.WriteAllText(existingOut, "existing-content");

            var resolved = SafeFilePolicy.ResolveFinalOutputPath(input, "webp", null, ConflictPolicy.AutoRename);
            Assert.NotNull(resolved);
            Assert.EndsWith("documento Unicode ¡España! (1).webp", resolved);
            Assert.Equal("existing-content", File.ReadAllText(existingOut));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}

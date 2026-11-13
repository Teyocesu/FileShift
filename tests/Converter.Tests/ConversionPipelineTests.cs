using System.Diagnostics;
using System.Text;
using FileShift.Core;
using FileShift.Infrastructure;
using Xunit;

namespace FileShift.Tests;

public sealed class ConversionPipelineTests
{
    private static async Task CreateSamplePngAsync(string path)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = OperatingSystem.IsWindows() ? "magick" : "convert",
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        var args = proc.StartInfo.ArgumentList;
        args.Add("-size");
        args.Add("180x120");
        args.Add("xc:#14B8A6");
        args.Add(path);
        proc.Start();
        await proc.WaitForExitAsync();
        Assert.Equal(0, proc.ExitCode);
    }

    private static async Task CreateSampleMp4Async(string path)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            }
        };
        var args = proc.StartInfo.ArgumentList;
        args.Add("-y");
        args.Add("-f"); args.Add("lavfi"); args.Add("-i"); args.Add("color=c=teal:s=160x120:d=1.0");
        args.Add("-f"); args.Add("lavfi"); args.Add("-i"); args.Add("sine=frequency=440:duration=1.0");
        args.Add("-c:v"); args.Add("libx264"); args.Add("-pix_fmt"); args.Add("yuv420p");
        args.Add("-c:a"); args.Add("aac");
        args.Add(path);
        proc.Start();
        var stderr = proc.StandardError.ReadToEndAsync();
        await proc.WaitForExitAsync();
        Assert.True(proc.ExitCode == 0, await stderr);
    }

    private static async Task CreateMultiPagePdfAsync(string path)
    {
        string gsBin = OperatingSystem.IsWindows() ? "gswin64c" : "gs";
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = gsBin,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        var args = proc.StartInfo.ArgumentList;
        args.Add("-q");
        args.Add("-dBATCH");
        args.Add("-dNOPAUSE");
        args.Add("-sDEVICE=pdfwrite");
        args.Add($"-sOutputFile={path}");
        args.Add("-c");
        args.Add("<< /PageSize [200 200] >> setpagedevice 0.1 0.6 0.5 setrgbcolor 20 20 160 160 rectfill showpage 0.2 0.3 0.8 setrgbcolor 30 30 140 140 rectfill showpage");
        proc.Start();
        var stderr = proc.StandardError.ReadToEndAsync();
        var stdout = proc.StandardOutput.ReadToEndAsync();
        await proc.WaitForExitAsync();
        Assert.True(proc.ExitCode == 0, (await stderr) + (await stdout));
    }

    private sealed class CorruptOutputAdapter(byte[] payloadToWrite, bool mutateInputFile = false) : IConversionEngineAdapter
    {
        public string EngineName => "ImageMagick";
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public async Task ConvertAsync(ConversionJob job, string tempOutputPath, IProgress<double>? progress, CancellationToken ct = default)
        {
            await File.WriteAllBytesAsync(tempOutputPath, payloadToWrite, ct);
            if (mutateInputFile)
            {
                await File.AppendAllTextAsync(job.InputPath, "CORRUPTED_BY_ROGUE_ENGINE", ct);
            }
        }
    }

    private sealed class SlowCancelableAdapter : IConversionEngineAdapter
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationSeen { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupAllowed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string EngineName => "ImageMagick";
        public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

        public async Task ConvertAsync(ConversionJob job, string tempOutputPath, IProgress<double>? progress, CancellationToken ct = default)
        {
            await File.WriteAllBytesAsync(tempOutputPath, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], ct);
            Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, ct); }
            finally
            {
                CancellationSeen.TrySetResult();
                await CleanupAllowed.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
        }
    }

    [Fact]
    public async Task P0_Overwrite_WhenConversionProducesInvalidOutput_PreservesExistingTargetFileIntact()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_p0_overwrite_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePng = Path.Combine(tempDir, "sample.png");
            await CreateSamplePngAsync(sourcePng);
            var originalPngSha = await SafeFilePolicy.ComputeSha256Async(sourcePng);

            var existingWebp = Path.Combine(tempDir, "sample.webp");
            var existingContent = Encoding.UTF8.GetBytes("EXISTING_PRECIOUS_WEBP_FILE_THAT_MUST_NOT_BE_LOST");
            await File.WriteAllBytesAsync(existingWebp, existingContent);
            var existingWebpSha = await SafeFilePolicy.ComputeSha256Async(existingWebp);

            using var queue = new ConversionQueue(
                [new CorruptOutputAdapter([0x00, 0x11, 0x22, 0x33, 0x44, 0x55])],
                maxConcurrency: 1,
                validator: new OutputIntegrityValidator());

            var job = queue.Enqueue(sourcePng, "webp", new ConversionOptions(ConflictPolicy: ConflictPolicy.Overwrite));
            var finished = await queue.WaitForJobAsync(job.Id);

            Assert.Equal(JobStatus.Failed, finished.Status);
            Assert.True(File.Exists(existingWebp), "El archivo de destino previo no debe ser eliminado si la nueva salida es inválida.");
            Assert.Equal(existingWebpSha, await SafeFilePolicy.ComputeSha256Async(existingWebp));
            Assert.Equal(originalPngSha, await SafeFilePolicy.ComputeSha256Async(sourcePng));
            Assert.Empty(Directory.GetFileSystemEntries(tempDir, ".fileshift.tmp.*"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task P0_HeaderOnlyTruncatedPng_IsRejectedBeforePublish_AndPreservesExistingTarget()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_p0_truncated_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourceJpg = Path.Combine(tempDir, "photo.jpg");
            await CreateSamplePngAsync(sourceJpg);

            var existingPng = Path.Combine(tempDir, "photo.png");
            await CreateSamplePngAsync(existingPng);
            var existingPngSha = await SafeFilePolicy.ComputeSha256Async(existingPng);

            // Header-only PNG (starts with 89 50 4E 47 0D 0A 1A 0A, but lacks IEND trailer and valid IDAT)
            byte[] truncatedPng = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

            using var queue = new ConversionQueue(
                [new CorruptOutputAdapter(truncatedPng)],
                maxConcurrency: 1,
                validator: new OutputIntegrityValidator());

            var job = queue.Enqueue(sourceJpg, "png", new ConversionOptions(ConflictPolicy: ConflictPolicy.Overwrite));
            var finished = await queue.WaitForJobAsync(job.Id);

            Assert.Equal(JobStatus.Failed, finished.Status);
            Assert.Contains("truncado", finished.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Equal(existingPngSha, await SafeFilePolicy.ComputeSha256Async(existingPng));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task P0_OriginalFileModifiedDuringConversion_MarksJobFailedExplicitly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_p0_inv01_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePng = Path.Combine(tempDir, "input.png");
            await CreateSamplePngAsync(sourcePng);

            var validSampleJpg = Path.Combine(tempDir, "donor.jpg");
            await CreateSamplePngAsync(validSampleJpg);
            var validJpgBytes = await File.ReadAllBytesAsync(validSampleJpg);

            using var queue = new ConversionQueue(
                [new CorruptOutputAdapter(validJpgBytes, mutateInputFile: true)],
                maxConcurrency: 1,
                validator: new OutputIntegrityValidator());

            var job = queue.Enqueue(sourcePng, "jpeg");
            var finished = await queue.WaitForJobAsync(job.Id);

            Assert.Equal(JobStatus.Failed, finished.Status);
            Assert.False(finished.OriginalIntactVerified);
            Assert.Contains("INV-01", finished.ErrorMessage ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task FolderAutomationService_ConvertsNewFileOnce_AndDoesNotLoopOnGeneratedOutput()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_auto_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            using var queue = new ConversionQueue(
                [new ImageMagickAdapter()],
                maxConcurrency: 1,
                validator: new OutputIntegrityValidator());
            using var automation = new FolderAutomationService(queue);

            var rule = new AutomationRule(
                Id: "rule-test",
                Name: "PNG to WebP",
                Enabled: true,
                SourceDirectory: tempDir,
                AllowedInputFormats: ["png", "webp"],
                TargetFormat: "webp",
                PresetId: null,
                OutputDirectory: null,
                ConflictPolicy: ConflictPolicy.AutoRename);

            var inputPng = Path.Combine(tempDir, "screenshot_01.png");
            await CreateSamplePngAsync(inputPng);

            var job = await automation.HandleFileCandidateAsync(rule, inputPng);
            Assert.NotNull(job);
            var finished = await queue.WaitForJobAsync(job.Id);
            Assert.Equal(JobStatus.Completed, finished.Status);
            Assert.NotNull(finished.OutputPath);

            // Feed the generated output back into HandleFileCandidateAsync: must return null (loop prevention)
            var loopAttempt = await automation.HandleFileCandidateAsync(rule, finished.OutputPath);
            Assert.Null(loopAttempt);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task P0_Cancellation_CleansUpTempFile_AndLeavesOriginalIntact()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_cancel_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var sourcePng = Path.Combine(tempDir, "cancel_me.png");
            await CreateSamplePngAsync(sourcePng);
            var preSha = await SafeFilePolicy.ComputeSha256Async(sourcePng);

            var adapter = new SlowCancelableAdapter();
            using var queue = new ConversionQueue([adapter], maxConcurrency: 1);
            var job = queue.Enqueue(sourcePng, "webp");

            await adapter.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var completion = queue.WaitForJobAsync(job.Id);
            try
            {
                Assert.True(queue.CancelJob(job.Id));
                await adapter.CancellationSeen.Task.WaitAsync(TimeSpan.FromSeconds(10));
                Assert.False(completion.IsCompleted);
                Assert.NotEqual(JobStatus.Cancelled, job.Status);
            }
            finally
            {
                adapter.CleanupAllowed.TrySetResult();
            }

            var finished = await completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(JobStatus.Cancelled, finished.Status);
            Assert.Null(finished.OutputPath);
            Assert.Equal(preSha, await SafeFilePolicy.ComputeSha256Async(sourcePng));
            Assert.Empty(Directory.GetFileSystemEntries(tempDir, ".fileshift.tmp.*"));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RealConversion_ImageMagick_PngToWebp_And_PngToPdf_PreservesOriginalSha256()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_img_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var inputPng = Path.Combine(tempDir, "imagen ¡España! 2026.png");
            await CreateSamplePngAsync(inputPng);
            var preSha = await SafeFilePolicy.ComputeSha256Async(inputPng);

            using var queue = new ConversionQueue(
                [new ImageMagickAdapter(), new PdfAdapter()],
                maxConcurrency: 2,
                validator: new OutputIntegrityValidator());

            var webpJob = queue.Enqueue(inputPng, "webp", new ConversionOptions(Quality: 85, LongestEdgePx: 120));
            var pdfJob = queue.Enqueue(inputPng, "pdf", new ConversionOptions(Quality: 90));

            var doneWebp = await queue.WaitForJobAsync(webpJob.Id);
            var donePdf = await queue.WaitForJobAsync(pdfJob.Id);

            Assert.Equal(JobStatus.Completed, doneWebp.Status);
            Assert.True(doneWebp.OriginalIntactVerified);
            Assert.Equal(preSha, doneWebp.PostOriginalSha256);
            Assert.NotNull(doneWebp.OutputPath);
            Assert.True(File.Exists(doneWebp.OutputPath));

            Assert.Equal(JobStatus.Completed, donePdf.Status);
            Assert.True(donePdf.OriginalIntactVerified);
            Assert.NotNull(donePdf.OutputPath);
            Assert.True(File.Exists(donePdf.OutputPath));
            Assert.Equal("pdf", MagicByteInspector.InspectFile(donePdf.OutputPath).DetectedFormat);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RealConversion_FFmpeg_Mp4ToWebm_And_Mp4ToMp3_VerifiedByFfprobe()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_av_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var inputMp4 = Path.Combine(tempDir, "clip_audio_video.mp4");
            await CreateSampleMp4Async(inputMp4);
            var preSha = await SafeFilePolicy.ComputeSha256Async(inputMp4);

            using var queue = new ConversionQueue(
                [new FFmpegAdapter()],
                maxConcurrency: 2,
                validator: new OutputIntegrityValidator());

            var webmJob = queue.Enqueue(inputMp4, "webm");
            var mp3Job = queue.Enqueue(inputMp4, "mp3");

            var doneWebm = await queue.WaitForJobAsync(webmJob.Id);
            var doneMp3 = await queue.WaitForJobAsync(mp3Job.Id);

            Assert.Equal(JobStatus.Completed, doneWebm.Status);
            Assert.True(doneWebm.OriginalIntactVerified);
            Assert.Equal(preSha, doneWebm.PostOriginalSha256);
            Assert.Contains("video", doneWebm.ProbeSummary ?? "", StringComparison.OrdinalIgnoreCase);

            Assert.Equal(JobStatus.Completed, doneMp3.Status);
            Assert.True(doneMp3.OriginalIntactVerified);
            Assert.Contains("audio", doneMp3.ProbeSummary ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RealConversion_PdfAdapter_MultiPagePdf_ExportsAllPagesAndPageRange()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"fileshift_pdf_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var inputPdf = Path.Combine(tempDir, "multipage.pdf");
            await CreateMultiPagePdfAsync(inputPdf);

            using var queue = new ConversionQueue(
                [new PdfAdapter()],
                maxConcurrency: 1,
                validator: new OutputIntegrityValidator());

            var allPagesJob = queue.Enqueue(inputPdf, "png");
            var finishedAll = await queue.WaitForJobAsync(allPagesJob.Id);

            Assert.Equal(JobStatus.Completed, finishedAll.Status);
            Assert.NotNull(finishedAll.OutputPath);
            Assert.True(File.Exists(finishedAll.OutputPath));
            Assert.Single(finishedAll.AdditionalOutputPaths);
            Assert.True(File.Exists(finishedAll.AdditionalOutputPaths[0]));
            Assert.EndsWith("multipage_p2.png", finishedAll.AdditionalOutputPaths[0]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private sealed class RejectOutputValidator : IOutputIntegrityValidator
    {
        public Task<OutputValidationResult> ValidateOutputAsync(string filePath, string targetFormatId, CancellationToken ct = default)
            => Task.FromResult(new OutputValidationResult(false, null, "Salida rechazada para la prueba"));
    }

    [Fact]
    public async Task Pdf_Overwrite_WhenValidationFails_PreservesEveryExistingPage()
    {
        var directory = Directory.CreateTempSubdirectory("fileshift-pdf-");
        try
        {
            var input = Path.Combine(directory.FullName, "multipage.pdf");
            await CreateMultiPagePdfAsync(input);
            var originalHash = await SafeFilePolicy.ComputeSha256Async(input);
            var first = Path.Combine(directory.FullName, "multipage.png");
            var second = Path.Combine(directory.FullName, "multipage_p2.png");
            await File.WriteAllTextAsync(first, "first original");
            await File.WriteAllTextAsync(second, "second original");

            using var queue = new ConversionQueue([new PdfAdapter()], validator: new RejectOutputValidator());
            var job = queue.Enqueue(input, "png", new ConversionOptions(ConflictPolicy: ConflictPolicy.Overwrite));
            var finished = await queue.WaitForJobAsync(job.Id).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(JobStatus.Failed, finished.Status);
            Assert.Equal("Salida rechazada para la prueba", finished.ErrorMessage);
            Assert.Equal("first original", await File.ReadAllTextAsync(first));
            Assert.Equal("second original", await File.ReadAllTextAsync(second));
            Assert.Equal(originalHash, await SafeFilePolicy.ComputeSha256Async(input));
            Assert.Empty(finished.AdditionalOutputPaths);
            Assert.Empty(directory.GetFileSystemInfos(".fileshift.*"));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Theory]
    [InlineData(ConflictPolicy.Overwrite, JobStatus.Failed)]
    [InlineData(ConflictPolicy.Skip, JobStatus.Completed)]
    [InlineData(ConflictPolicy.AutoRename, JobStatus.Completed)]
    public async Task Pdf_SecondaryCollision_AppliesPolicyToWholeJob(ConflictPolicy policy, JobStatus expected)
    {
        var directory = Directory.CreateTempSubdirectory("fileshift-collision-");
        try
        {
            var input = Path.Combine(directory.FullName, "multipage.pdf");
            await CreateMultiPagePdfAsync(input);
            var first = Path.Combine(directory.FullName, "multipage.png");
            var second = Path.Combine(directory.FullName, "multipage_p2.png");
            await File.WriteAllTextAsync(first, "preexisting");
            Directory.CreateDirectory(second);
            await File.WriteAllTextAsync(Path.Combine(second, "keep.txt"), "directory contents");

            using var queue = new ConversionQueue([new PdfAdapter()], validator: new OutputIntegrityValidator());
            var job = queue.Enqueue(input, "png", new ConversionOptions(ConflictPolicy: policy));
            var finished = await queue.WaitForJobAsync(job.Id).WaitAsync(TimeSpan.FromSeconds(30));

            Assert.Equal(expected, finished.Status);
            Assert.True(finished.OriginalIntactVerified);
            Assert.Equal("preexisting", await File.ReadAllTextAsync(first));
            Assert.Equal("directory contents", await File.ReadAllTextAsync(Path.Combine(second, "keep.txt")));
            Assert.Empty(directory.GetFileSystemInfos(".fileshift.*"));
            if (policy == ConflictPolicy.AutoRename)
            {
                Assert.EndsWith("multipage (1).png", finished.OutputPath);
                Assert.EndsWith("multipage_p2 (1).png", Assert.Single(finished.AdditionalOutputPaths));
                Assert.True(File.Exists(finished.OutputPath));
                Assert.True(File.Exists(finished.AdditionalOutputPaths[0]));
            }
            else
            {
                Assert.Null(finished.OutputPath);
                Assert.Empty(finished.AdditionalOutputPaths);
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Overwrite_WhenRollbackCannotRestore_KeepsOriginalDestinationBackup()
    {
        var directory = Directory.CreateTempSubdirectory("fileshift-recovery-");
        try
        {
            var stage = directory.CreateSubdirectory("stage");
            var destination = Path.Combine(directory.FullName, "output.png");
            var staged = Path.Combine(stage.FullName, "output.png");
            File.WriteAllText(destination, "preexisting content");
            File.WriteAllText(staged, "new content");
            var input = Path.Combine(directory.FullName, "input.pdf");

            var error = Assert.Throws<IOException>(() => SafeFilePolicy.PublishOutputs(
                [staged], directory.FullName, input, ConflictPolicy.Overwrite, () =>
                {
                    File.Delete(destination);
                    Directory.CreateDirectory(destination);
                    throw new InvalidDataException("Fallo al verificar el original");
                }, CancellationToken.None));

            var backup = Assert.Single(directory.GetFiles(".fileshift.bak.*"));
            Assert.Contains(backup.FullName, error.Message);
            Assert.Equal("preexisting content", File.ReadAllText(backup.FullName));
            Assert.True(Directory.Exists(destination));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void UserSettingsStore_PersistsSettings_AndGeneratesMenuCacheForExplorerShell()
    {
        var tempConfigDir = Path.Combine(Path.GetTempPath(), $"fileshift_cfg_{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable("FILESHIFT_CONFIG_DIR", tempConfigDir);
        try
        {
            var settings = new UserSettings
            {
                ContextMenuEnabled = true,
                MaxConcurrency = 6
            };
            settings.MenuFavorites["image"] = ["avif", "webp", "png"];

            UserSettingsStore.Save(settings);

            var loaded = UserSettingsStore.Load();
            Assert.Equal(6, loaded.MaxConcurrency);
            Assert.Equal(["avif", "webp", "png"], loaded.MenuFavorites["image"]);

            var menuCachePath = UserSettingsStore.GetMenuCacheFilePath();
            Assert.True(File.Exists(menuCachePath));
            var cacheText = File.ReadAllText(menuCachePath);
            Assert.Contains("ENABLED=1", cacheText);
            Assert.Contains("FAV_image=avif,webp,png", cacheText);
        }
        finally
        {
            Environment.SetEnvironmentVariable("FILESHIFT_CONFIG_DIR", null);
            if (Directory.Exists(tempConfigDir))
            {
                Directory.Delete(tempConfigDir, recursive: true);
            }
        }
    }
}

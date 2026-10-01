using System.Diagnostics;
using ChzzkDownloader.Services;
using Xunit.Abstractions;

namespace ChzzkDownloader.IntegrationTests;

public sealed class FastVerificationIntegrationTests(ITestOutputHelper output)
{
    [Fact]
    public async Task InvalidContainerStillFailsAndCancellationIsPreserved()
    {
        var path = Path.Combine(Path.GetTempPath(), "StreamNest-invalid-" + Guid.NewGuid() + ".mp4");
        try
        {
            await File.WriteAllTextAsync(path, "Not a video container");
            await Assert.ThrowsAsync<YtDlpException>(() => MediaVerificationService.VerifyAsync(path, null, null, false, CancellationToken.None));
            using var stop = new CancellationTokenSource();
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => MediaVerificationService.VerifyAsync(path, null, null, false, stop.Token));
        }
        finally { File.Delete(path); }
    }

    [EnvironmentFact("STREAMNEST_RUN_FAST_VERIFY")]
    public async Task ExistingLargeFiles_CompleteMetadataCheckWithoutFullDecode()
    {
        var folder = Environment.GetEnvironmentVariable("STREAMNEST_FAST_VERIFY_FOLDER")!;
        var files = new DirectoryInfo(folder).GetFiles("*.mp4").OrderByDescending(f => f.Length).Take(2).ToArray();
        Assert.NotEmpty(files);
        foreach (var file in files)
        {
            using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var timer = Stopwatch.StartNew();
            var warnings = await MediaVerificationService.VerifyAsync(file.FullName, null, null, true, stop.Token);
            output.WriteLine($"{file.Length} bytes: {timer.Elapsed.TotalSeconds:F3}s, {warnings.Count} metadata warnings.");
            Assert.Empty(warnings);
        }
    }
}

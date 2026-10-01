namespace ChzzkDownloader.Models;

public enum MediaKind
{
    Video,
    AudioOnly
}

public sealed record MediaVerificationExpectation(
    MediaKind Kind,
    double? ExpectedDurationSeconds = null,
    int? ExpectedHeight = null,
    bool ExpectsAudio = true,
    string? ExpectedContainer = null)
{
    public static MediaVerificationExpectation ForVideo(double? expectedDuration, int? expectedHeight, bool expectsAudio, string? expectedContainer = null) =>
        new(MediaKind.Video, expectedDuration, expectedHeight, expectsAudio, expectedContainer);

    public static MediaVerificationExpectation ForAudio(double? expectedDuration, string? expectedContainer = "m4a") =>
        new(MediaKind.AudioOnly, expectedDuration, null, true, expectedContainer);
}

using ChzzkDownloader.Models;

namespace ChzzkDownloader.Services;

public sealed class DownloadProgressAggregator
{
    private readonly object _sync = new();
    private readonly int _expectedPartCount;
    private string _currentPartId = string.Empty;
    private int _partIndex;
    private double _lastRawPercent;
    private double _maximumOverallPercent;

    public DownloadProgressAggregator(string? formatSelector)
    {
        var primarySelector = formatSelector?.Split('/', 2)[0] ?? string.Empty;
        _expectedPartCount = primarySelector.Contains('+', StringComparison.Ordinal) ? 2 : 1;
    }

    public DownloadProgressInfo Aggregate(DownloadProgressInfo raw)
    {
        lock (_sync)
        {
            if (!raw.Percent.HasValue)
                return raw;

            var rawPercent = Math.Clamp(raw.Percent.Value, 0, 100);
            var effectivePartCount = raw.PartKind == DownloadPartKind.Combined
                ? 1
                : _expectedPartCount;

            if (effectivePartCount > 1)
                UpdatePart(raw, rawPercent);
            else
                _partIndex = 0;

            var calculatedPercent = effectivePartCount == 1
                ? rawPercent
                : (_partIndex + rawPercent / 100d) / effectivePartCount * 100d;
            var overallPercent = Math.Max(_maximumOverallPercent, Math.Clamp(calculatedPercent, 0, 100));
            _maximumOverallPercent = overallPercent;
            _lastRawPercent = rawPercent;

            var message = BuildMessage(raw, rawPercent, overallPercent, effectivePartCount);
            return raw with { Percent = overallPercent, Message = message };
        }
    }

    private void UpdatePart(DownloadProgressInfo raw, double rawPercent)
    {
        var inferredIndex = raw.PartKind switch
        {
            DownloadPartKind.Video => 0,
            DownloadPartKind.Audio => 1,
            _ => -1
        };

        if (inferredIndex >= 0)
        {
            _partIndex = inferredIndex;
        }
        else if (!string.IsNullOrWhiteSpace(raw.PartId))
        {
            if (string.IsNullOrWhiteSpace(_currentPartId))
                _currentPartId = raw.PartId;
            else if (!raw.PartId.Equals(_currentPartId, StringComparison.Ordinal))
            {
                _currentPartId = raw.PartId;
                _partIndex = Math.Min(_partIndex + 1, _expectedPartCount - 1);
            }
        }
        else if (_partIndex < _expectedPartCount - 1 &&
                 _lastRawPercent >= 90 &&
                 rawPercent < _lastRawPercent - 10)
        {
            _partIndex++;
        }
    }

    private static string BuildMessage(
        DownloadProgressInfo raw,
        double rawPercent,
        double overallPercent,
        int effectivePartCount)
    {
        if (effectivePartCount == 1)
            return raw.Message;

        var stageName = raw.PartKind switch
        {
            DownloadPartKind.Video => "영상",
            DownloadPartKind.Audio => "오디오",
            _ => "파트"
        };
        var detailsSeparator = raw.Message.IndexOf(" · ", StringComparison.Ordinal);
        var details = detailsSeparator >= 0 ? raw.Message[detailsSeparator..] : string.Empty;
        return $"{stageName} {rawPercent:0.0}% · 전체 {overallPercent:0.0}%{details}";
    }
}

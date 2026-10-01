namespace ChzzkDownloader.Services;

public static class RPlayLoginPopupFlow
{
    public static bool TryGetCallbackUri(string? value, bool googleAuthenticationStarted, out Uri callbackUri)
    {
        callbackUri = null!;
        if (!googleAuthenticationStarted || !RPlaySessionService.IsSessionPage(value) ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri)) return false;

        var codes = uri.Query.TrimStart('?').Split('&')
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && Uri.UnescapeDataString(pair[0]) == "code")
            .Select(pair => Uri.UnescapeDataString(pair[1])).ToArray();
        if (codes.Length != 1 || string.IsNullOrWhiteSpace(codes[0])) return false;

        callbackUri = uri;
        return true;
    }
}

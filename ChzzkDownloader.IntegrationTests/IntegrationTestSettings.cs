namespace ChzzkDownloader.IntegrationTests;

internal static class IntegrationTestSettings
{
    public const string RunAuthTestsVariable = "STREAMNEST_RUN_AUTH_TESTS";
    public const string RunNetworkTestsVariable = "STREAMNEST_RUN_NETWORK_TESTS";
    public const string RunDownloadTestsVariable = "STREAMNEST_RUN_DOWNLOAD_TESTS";
    public const string PublicUrlVariable = "STREAMNEST_YOUTUBE_PUBLIC_URL";
    public const string MembershipUrlVariable = "STREAMNEST_YOUTUBE_MEMBERS_URL";
    public const string AgeRestrictedUrlVariable = "STREAMNEST_YOUTUBE_AGE_URL";

    public static string RequireUrl(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{variableName}에 접근 권한이 있는 YouTube 영상 URL을 지정해주세요.");
        return value;
    }
}

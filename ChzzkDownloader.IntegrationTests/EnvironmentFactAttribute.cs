namespace ChzzkDownloader.IntegrationTests;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class EnvironmentFactAttribute : FactAttribute
{
    public EnvironmentFactAttribute(string enableVariable, string? requiredValueVariable = null)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(enableVariable), "1", StringComparison.Ordinal))
        {
            Skip = $"{enableVariable}=1일 때만 실행되는 실사용 통합 테스트입니다.";
            return;
        }

        if (requiredValueVariable is not null &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(requiredValueVariable)))
            Skip = $"{requiredValueVariable}에 접근 권한이 있는 YouTube 영상 URL을 지정해주세요.";
    }
}

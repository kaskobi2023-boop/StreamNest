using ChzzkDownloader.Services;

namespace ChzzkDownloader.Tests;

public sealed class RPlayLoginPopupFlowTests
{
    [Theory]
    [InlineData("https://rplay.live/?code=synthetic-code")]
    [InlineData("https://www.rplay.live/login?state=synthetic-state&code=synthetic%2Bcode")]
    [InlineData("https://rplay.live/?%63ode=synthetic-code")]
    public void GoogleReturnPreservesTheWholeWebsiteCallback(string value)
    {
        Assert.True(RPlayLoginPopupFlow.TryGetCallbackUri(value, true, out var callback));
        Assert.Equal(value, callback.OriginalString);
    }

    [Theory]
    [InlineData("https://rplay.live/?code=synthetic-code", false)]
    [InlineData("https://rplay.live/", true)]
    [InlineData("https://rplay.live/?code=", true)]
    [InlineData("https://rplay.live/?code=%20", true)]
    [InlineData("https://rplay.live/?code=one&code=two", true)]
    [InlineData("https://rplay.live/?error=access_denied", true)]
    [InlineData("https://accounts.google.com/?code=synthetic-code", true)]
    [InlineData("https://api.rplay.live/?code=synthetic-code", true)]
    [InlineData("https://rplay.live.attacker.invalid/?code=synthetic-code", true)]
    [InlineData("https://user:password@rplay.live/?code=synthetic-code", true)]
    [InlineData("https://rplay.live:444/?code=synthetic-code", true)]
    [InlineData("http://rplay.live/?code=synthetic-code", true)]
    [InlineData("about:blank", true)]
    [InlineData(null, true)]
    public void UnrelatedOrIncompleteNavigationIsNotHandedOff(string? value, bool googleStarted)
    {
        Assert.False(RPlayLoginPopupFlow.TryGetCallbackUri(value, googleStarted, out _));
    }
}

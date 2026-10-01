using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Xml.Linq;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class LoginPopupIntegrationTests
{
    private const string VideoUrl = "https://rplay.live/play/0123456789abcdef01234567";
    private const string CallbackUrl = "https://rplay.live/?code=synthetic-callback-code";

    [EnvironmentFact("STREAMNEST_RUN_LOGIN_POPUP_TESTS")]
    public Task ClosingPopupPreservesDelayedCredentialCallback() => WithLoginAsync(async (login, parent) =>
    {
        await OpenPopupAsync(login, parent, """
            window.opener.postMessage('synthetic-credential', 'https://rplay.live');
            setTimeout(() => window.close(), 100);
            """);
        await Task.Delay(1800);
        Assert.Equal("true", await parent.ExecuteScriptAsync("window.callbackFinished === true"));
        Assert.Equal("1", await parent.ExecuteScriptAsync("Number(localStorage.getItem('fixture-loads'))"));
    });

    [EnvironmentFact("STREAMNEST_RUN_LOGIN_POPUP_TESTS")]
    public Task OAuthCodeReturnsToOriginalLoginPage() => WithLoginAsync(async (login, parent) =>
    {
        await OpenPopupAsync(login, parent, $"window.location.replace('{CallbackUrl}');");
        await WaitUntilAsync(async () => await parent.ExecuteScriptAsync("window.codeAccepted === true") == "true");
        Assert.Equal(CallbackUrl, parent.Source);
        Assert.Empty(login.OwnedWindows.Cast<Window>());
    });

    [EnvironmentFact("STREAMNEST_RUN_LOGIN_POPUP_TESTS")]
    public Task CancellingPopupDoesNotReloadOrAuthenticateOriginalPage() => WithLoginAsync(async (login, parent) =>
    {
        await OpenPopupAsync(login, parent, "setTimeout(() => window.close(), 100);");
        await Task.Delay(1200);
        Assert.Equal("1", await parent.ExecuteScriptAsync("Number(localStorage.getItem('fixture-loads'))"));
        Assert.Equal("false", await parent.ExecuteScriptAsync("window.callbackFinished === true || window.codeAccepted === true"));
    });

    private static Task WithLoginAsync(Func<LoginWindow, CoreWebView2, Task> action) => StaThreadRunner.RunAsync(async () =>
    {
        var profile = Path.Combine(Path.GetTempPath(), "StreamNest-popup-fixture-" + Guid.NewGuid());
        var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: profile);
        var resources = LoadResources();
        var login = new LoginWindow(VideoUrl, environment, resources)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000, Top = -10000, Width = 300, Height = 250,
            ShowActivated = false, ShowInTaskbar = false, Opacity = 0
        };
        var webView = (WebView2)login.FindName("LoginWebView");
        webView.CoreWebView2InitializationCompleted += (_, args) =>
        {
            if (args.IsSuccess) InterceptDocuments(webView.CoreWebView2, ParentDocument);
        };
        try
        {
            login.Show();
            await WaitUntilAsync(async () => webView.CoreWebView2 is not null &&
                await webView.CoreWebView2.ExecuteScriptAsync("window.fixtureReady === true") == "true");
            await action(login, webView.CoreWebView2);
            return true;
        }
        finally
        {
            login.Close();
            webView.Dispose();
            // Only synthetic fixture data is stored here. Never use or clean the app profile.
            for (var attempt = 0; attempt < 20 && Directory.Exists(profile); attempt++)
            {
                try { Directory.Delete(profile, true); }
                catch (IOException) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) { await Task.Delay(100); }
            }
        }
    });

    private static async Task OpenPopupAsync(LoginWindow login, CoreWebView2 parent, string popupScript)
    {
        await parent.ExecuteScriptAsync("window.open('about:blank', 'fixture-popup');");
        WebView2? child = null;
        await WaitUntilAsync(() =>
        {
            child = login.OwnedWindows.Cast<Window>().Select(w => w.Content).OfType<WebView2>().FirstOrDefault();
            return Task.FromResult(child?.CoreWebView2 is not null);
        });
        var childCore = child!.CoreWebView2;
        InterceptDocuments(childCore, uri => uri.Host == "accounts.google.com"
            ? "<!doctype html><html><body><script>setTimeout(() => {" + popupScript + "}, 150);</script></body></html>"
            : ParentDocument(uri));
        childCore.Navigate("https://accounts.google.com/fixture");
    }

    private static void InterceptDocuments(CoreWebView2 core, Func<Uri, string> document)
    {
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, args) =>
        {
            // Every request is intercepted: these tests never contact RPlay or Google.
            var html = document(new Uri(args.Request.Uri));
            args.Response = core.Environment.CreateWebResourceResponse(
                new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK",
                "Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store");
        };
    }

    private static string ParentDocument(Uri uri) => """
        <!doctype html><html><body><script>
        localStorage.setItem('fixture-loads', String(Number(localStorage.getItem('fixture-loads') || 0) + 1));
        window.callbackFinished = false;
        window.codeAccepted = new URLSearchParams(location.search).get('code') === 'synthetic-callback-code';
        window.addEventListener('message', event => {
            if (event.origin !== 'https://accounts.google.com' || event.data !== 'synthetic-credential') return;
            setTimeout(() => { window.callbackFinished = true; }, 650);
        });
        window.fixtureReady = true;
        </script></body></html>
        """;

    private static ResourceDictionary LoadResources()
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppStyles.xaml"));
        var dictionary = new XElement(p + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
            source.Root!.Element(p + "Application.Resources")!.Elements());
        return (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate()) return;
            await Task.Delay(50);
        }
        throw new TimeoutException("Synthetic login popup did not reach the expected state.");
    }
}

using System.Net;
using System.Windows;
using System.Windows.Controls;
using ChzzkDownloader.Services;

namespace ChzzkDownloader.IntegrationTests;

public sealed class ThumbnailLoadingIntegrationTests
{
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    [Fact]
    [Trait("Category", "WpfUI")]
    public async Task RealHttpThumbnail_DecodesAndUpdatesWpfControls()
    {
        await using var server = new LoopbackThumbnailServer(OnePixelPng);

        var result = await StaThreadRunner.RunAsync(async () =>
        {
            var image = await new ThumbnailService().LoadAsync(server.Uri.AbsoluteUri);
            var imageControl = new Image();
            var placeholder = new TextBlock { Visibility = Visibility.Visible };
            ThumbnailPresenter.Apply(imageControl, placeholder, image);
            return new
            {
                Loaded = imageControl.Source is not null,
                Placeholder = placeholder.Visibility,
                Width = image?.PixelWidth,
                Frozen = image?.IsFrozen
            };
        });

        Assert.True(result.Loaded);
        Assert.Equal(Visibility.Collapsed, result.Placeholder);
        Assert.Equal(1, result.Width);
        Assert.True(result.Frozen);
    }

    [Fact]
    [Trait("Category", "WpfUI")]
    public async Task FailedHttpThumbnail_LeavesPlaceholderVisible()
    {
        await using var server = new LoopbackThumbnailServer([], HttpStatusCode.NotFound);

        var result = await StaThreadRunner.RunAsync(async () =>
        {
            var image = await new ThumbnailService().LoadAsync(server.Uri.AbsoluteUri);
            var imageControl = new Image();
            var placeholder = new TextBlock { Visibility = Visibility.Collapsed };
            ThumbnailPresenter.Apply(imageControl, placeholder, image);
            return (imageControl.Source, placeholder.Visibility);
        });

        Assert.Null(result.Source);
        Assert.Equal(Visibility.Visible, result.Visibility);
    }
}

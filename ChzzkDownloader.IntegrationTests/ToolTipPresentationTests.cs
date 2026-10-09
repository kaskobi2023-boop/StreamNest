using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ChzzkDownloader.IntegrationTests;

[Collection(WebView2IntegrationCollection.Name)]
public sealed class ToolTipPresentationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ToolTipHasReadableDarkSurfaceAndWrapsLongDescriptions(bool longDescription) => StaThreadRunner.RunAsync(async () =>
    {
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var source = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "AppStyles.xaml"));
        var dictionaryXml = new XElement(p + "ResourceDictionary", new XAttribute(XNamespace.Xmlns + "x", x),
            source.Root!.Element(p + "Application.Resources")!.Elements());
        var resources = (ResourceDictionary)XamlReader.Parse(dictionaryXml.ToString());
        var text = longDescription
            ? "[정상] yt-dlp: 2026.08.19\n[정상] FFmpeg: ffmpeg version 8.1.2-essentials_build Copyright (c) FFmpeg developers\n[정상] 저장 폴더: 쓰기 가능 · 여유 공간 1.5 TB\n" + string.Concat(Enumerable.Repeat("긴 설명도 화면 밖으로 넘치지 않고 읽을 수 있습니다. ", 8))
            : "현재 지정된 저장 폴더를 엽니다.";
        var tooltip = new ToolTip { Resources = resources, Style = (Style)resources[typeof(ToolTip)], Content = text };
        tooltip.ApplyTemplate();
        tooltip.Measure(new Size(1200, 1200));
        tooltip.Arrange(new Rect(tooltip.DesiredSize));
        tooltip.UpdateLayout();
        var background = Assert.IsType<SolidColorBrush>(tooltip.Background).Color;
        Assert.Equal(Color.FromRgb(15, 22, 32), background);
        Assert.Equal(Color.FromRgb(243, 247, 250), Assert.IsType<SolidColorBrush>(tooltip.Foreground).Color);
        Assert.InRange(tooltip.ActualWidth, 40, 480);
        var textBlocks = Descendants(tooltip).OfType<TextBlock>().ToArray();
        Assert.Contains(textBlocks, block => block.Text == text);
        Assert.All(textBlocks, block => Assert.Equal(TextWrapping.Wrap, block.TextWrapping));
        if (longDescription) Assert.True(tooltip.ActualHeight > 100);

        var previewDirectory = Environment.GetEnvironmentVariable("STREAMNEST_TOOLTIP_PREVIEW_DIR");
        if (!string.IsNullOrWhiteSpace(previewDirectory))
        {
            Directory.CreateDirectory(previewDirectory);
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(tooltip.ActualWidth), (int)Math.Ceiling(tooltip.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(tooltip);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(previewDirectory, longDescription ? "tooltip-long.png" : "tooltip-short.png"));
            encoder.Save(stream);
        }
        await Task.CompletedTask;
        return true;
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

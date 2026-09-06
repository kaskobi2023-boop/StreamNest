using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ChzzkDownloader.Services;

public static class ThumbnailPresenter
{
    public static void Apply(Image target, UIElement placeholder, ImageSource? source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(placeholder);
        target.Source = source;
        placeholder.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
    }
}

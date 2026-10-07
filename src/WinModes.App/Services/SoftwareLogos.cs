using System.Collections.Concurrent;
using System.Windows.Media.Imaging;

namespace WinModes.App.Services;

internal static class SoftwareLogos
{
    private static readonly ConcurrentDictionary<string, BitmapImage> Cache = new(StringComparer.Ordinal);
    public static BitmapImage Get(string id) => Cache.GetOrAdd(id, key =>
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri($"pack://application:,,,/Assets/Software/{key}.png");
        image.DecodePixelWidth = 64;
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.EndInit();
        image.Freeze();
        return image;
    });
}

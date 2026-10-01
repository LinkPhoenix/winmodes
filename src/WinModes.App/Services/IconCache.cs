using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace WinModes.App.Services;

/// <summary>Icons extracted from executables, cached by path. Returns null when a file has no readable icon.</summary>
internal static class IconCache
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? Get(string? executablePath) =>
        string.IsNullOrEmpty(executablePath) ? null : Cache.GetOrAdd(executablePath, Load);

    private static ImageSource? Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // A Store app keeps its logo as a picture next to its program, which has no icon of its own.
            if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                var picture = BitmapFrame.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                picture.Freeze();
                return picture;
            }

            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is null)
            {
                return null;
            }

            var image = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // Frozen so it can be created on a worker thread and shown by the UI thread.
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            return null;
        }
    }
}

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

    /// <summary>The icon when it was already read, without reading it now.</summary>
    public static ImageSource? Peek(string? executablePath) =>
        !string.IsNullOrEmpty(executablePath) && Cache.TryGetValue(executablePath, out var icon) ? icon : null;

    /// <summary>Reads the icons that are not cached yet, several at a time. True when something new was read, so a list can be redrawn.</summary>
    public static Task<bool> PreloadAsync(IEnumerable<string?> executablePaths) => Task.Run(() =>
    {
        var missing = executablePaths
            .Where(path => !string.IsNullOrEmpty(path) && !Cache.ContainsKey(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Parallel.ForEach(missing, path => Cache.GetOrAdd(path!, Load));
        return missing.Count > 0;
    });

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

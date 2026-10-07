using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using WinModes.Core;

namespace WinModes.App.Services;

/// <summary>
/// The icon of each AI tool, taken from its own program. The program is seen while the tool runs and remembered, also across
/// runs, so the plan rows keep their icon when the tool is closed; a tool never seen is looked for where it installs.
/// </summary>
internal static class ToolIcons
{
    private const string CodexPackagePrefix = "OpenAI.Codex_";
    private const string PackagesKey = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages";

    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "tool-icons.json");

    private static readonly ConcurrentDictionary<string, string> Paths = new(Load(), StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, bool> Searched = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Remembers the program of every running tool. Claude Code comes last so its icon wins over the desktop app's.</summary>
    public static void Remember(IEnumerable<AiToolUsage> running)
    {
        var changed = false;
        foreach (var tool in running.Where(tool => tool.ExecutablePath is not null).OrderBy(tool => tool.Name == "Claude Code"))
        {
            var key = Key(tool.Name);
            var source = StoreLogo(tool.ExecutablePath!) ?? tool.ExecutablePath!;
            if (!Paths.TryGetValue(key, out var known) || !string.Equals(known, source, StringComparison.OrdinalIgnoreCase))
            {
                Paths[key] = source;
                changed = true;
            }
        }

        if (changed)
        {
            Save();
        }
    }

    // The Grok mark as xAI draws it (24 x 24, one ink, even-odd fill), unmodified except that the arc flags are separated, which
    // the WPF path parser needs. Grok has no program on this PC to take an icon from, so the mark is drawn, in the colour of the
    // surface it sits on, as xAI itself does on its dark surfaces. Used only to name xAI's own service, as its brand guidelines ask.
    private const string GrokMarkPath =
        "F0 M9.27 15.29l7.978-5.897c.391-.29.95-.177 1.137.272.98 2.369.542 5.215-1.41 7.169-1.951 1.954-4.667 2.382-7.149 1.406l-2.711 1.257c3.889 2.661 8.611 2.003 11.562-.953 2.341-2.344 3.066-5.539 2.388-8.42l.006.007c-.983-4.232.242-5.924 2.75-9.383.06-.082.12-.164.179-.248l-3.301 3.305v-.01L9.267 15.292"
        + "M7.623 16.723c-2.792-2.67-2.31-6.801.071-9.184 1.761-1.763 4.647-2.483 7.166-1.425l2.705-1.25a7.808 7.808 0 0 0 -1.829 -1A8.975 8.975 0 0 0 5.984 5.83c-2.533 2.536-3.33 6.436-1.962 9.764 1.022 2.487-.653 4.246-2.34 6.022-.599.63-1.199 1.259-1.682 1.925l7.62-6.815";

    private static readonly ConcurrentDictionary<Color, ImageSource> GrokMarks = new();

    /// <summary>The Grok mark painted with <paramref name="ink"/>, frozen so any thread can make and show it.</summary>
    public static ImageSource GrokMark(Color ink) => GrokMarks.GetOrAdd(ink, color =>
    {
        const double ViewBox = 24;
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var group = new DrawingGroup { ClipGeometry = new RectangleGeometry(new Rect(0, 0, ViewBox, ViewBox)) };
        group.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, ViewBox, ViewBox))));
        group.Children.Add(new GeometryDrawing(brush, null, Geometry.Parse(GrokMarkPath)));
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    });

    /// <summary>The mark in the ink of the app theme: white on the dark theme, near black on the light one.</summary>
    private static ImageSource GrokMarkForTheme() =>
        GrokMark(Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Light ? Color.FromRgb(0x11, 0x11, 0x11) : Colors.White);

    /// <param name="tool">"Claude", "Codex", "Grok" or a full tool name such as "Claude Code".</param>
    public static ImageSource? For(string tool)
    {
        var key = Key(tool);
        if (key.Equals("Grok", StringComparison.OrdinalIgnoreCase))
        {
            return GrokMarkForTheme();
        }

        // A path that no longer exists (the app was updated: its folder carries the version) is looked for again.
        if (!Paths.TryGetValue(key, out var path) || !File.Exists(path))
        {
            path = Searched.TryAdd(key, true) ? Locate(key) : null;
            if (path is not null)
            {
                Paths[key] = path;
                Save();
            }
        }

        return IconCache.Get(path);
    }

    /// <summary>Where a tool installs, for the ones not seen running yet; null when it is not found.</summary>
    private static string? Locate(string key) => key.ToUpperInvariant() switch
    {
        "CODEX" => CodexStoreProgram(),
        "CLAUDE" => FirstExisting(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "bin", "claude.exe"),
            LatestSquirrelProgram("AnthropicClaude", "claude.exe")),
        _ => null,
    };

    /// <summary>The Codex desktop app is a Store package; its folder is in the package repository of the registry.</summary>
    private static string? CodexStoreProgram()
    {
        try
        {
            using var packages = Registry.CurrentUser.OpenSubKey(PackagesKey);
            var name = packages?.GetSubKeyNames().Where(name => name.StartsWith(CodexPackagePrefix, StringComparison.OrdinalIgnoreCase)).Order().LastOrDefault();
            if (name is null || packages!.OpenSubKey(name)?.GetValue("PackageRootFolder") is not string root)
            {
                return null;
            }

            return FirstExisting(StoreLogo(Path.Combine(root, "app", "Codex.exe")), Path.Combine(root, "app", "Codex.exe"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Squirrel installs put each version in an "app-x.y.z" folder next to the updater.</summary>
    private static string? LatestSquirrelProgram(string folder, string program)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), folder);
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root, "app-*").Order().Select(version => Path.Combine(version, program)).LastOrDefault(File.Exists)
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// The logo of a Store app whose program is in its package folder (WindowsApps\Package_version\...): the program has no icon
    /// of its own, the manifest points to pictures in the "assets" folder. Null for any other program.
    /// </summary>
    private static string? StoreLogo(string program)
    {
        const string Marker = @"\WindowsApps\";
        var start = program.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        var end = program.IndexOf('\\', start + Marker.Length);
        if (end < 0)
        {
            return null;
        }

        var assets = Path.Combine(program[..end], "assets");
        return FirstExisting(Path.Combine(assets, "Square44x44Logo.scale-200.png"), Path.Combine(assets, "Square44x44Logo.png"));
    }

    private static string? FirstExisting(params string?[] paths) => paths.FirstOrDefault(path => path is not null && File.Exists(path));

    private static Dictionary<string, string> Load()
    {
        try
        {
            return File.Exists(StorePath) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(StorePath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
            AtomicFile.WriteAllText(StorePath, JsonSerializer.Serialize(Paths));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The icons are found again next time; nothing depends on this file.
        }
    }

    private static string Key(string tool) => tool.Split(' ')[0];
}

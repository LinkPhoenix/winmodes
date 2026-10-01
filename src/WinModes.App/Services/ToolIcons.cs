using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
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

    /// <param name="tool">"Claude", "Codex" or a full tool name such as "Claude Code".</param>
    public static ImageSource? For(string tool)
    {
        var key = Key(tool);
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

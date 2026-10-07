using System.Security;
using Microsoft.Win32;

namespace WinModes.Core.Software;

public sealed record RegisteredSoftware(string Name, string Publisher, string Version);

/// <summary>Read-only fallback for applications whose local metadata WinGet cannot correlate with a source.</summary>
public static class WindowsSoftwareInventory
{
    private sealed record DetectionRule(string EntryId, string[] Names, string[] Publishers);
    private static readonly DetectionRule[] Rules =
    [
        new("brave", ["Brave"], ["Brave Software Inc"]),
        new("chrome", ["Google Chrome"], ["Google LLC", "Google Inc."]),
        new("vivaldi", ["Vivaldi"], ["Vivaldi Technologies AS"]),
        new("obsidian", ["Obsidian"], ["Obsidian"]),
        new("joplin", ["Joplin"], ["Laurent Cozic"]),
        new("vlc", ["VLC media player"], ["VideoLAN"]),
        new("spotify", ["Spotify"], ["Spotify AB", "Spotify Ltd"]),
        new("discord", ["Discord"], ["Discord Inc."]),
        new("signal", ["Signal"], ["Signal Messenger, LLC"]),
        new("telegram", ["Telegram Desktop"], ["Telegram FZ-LLC"]),
        new("cursor", ["Cursor", "Cursor (User)"], ["Anysphere"]),
        new("vscode", ["Microsoft Visual Studio Code", "Microsoft Visual Studio Code (User)"], ["Microsoft Corporation"]),
        new("steam", ["Steam"], ["Valve Corporation"]),
        new("epic", ["Epic Games Launcher"], ["Epic Games, Inc."]),
        new("bitwarden", ["Bitwarden"], ["Bitwarden Inc."]),
        new("claude", ["Claude"], ["Anthropic", "Anthropic PBC"]),
        new("t3", ["T3 Code"], ["T3 Tools"]),
        new("notepadplusplus", ["Notepad++"], ["Notepad++ Team"]),
    ];

    public static SoftwareInventory Read()
    {
        var inventory = WindowsWinget.ReadInventory();
        // An unreadable WinGet source must still block installation; registry absence cannot prove absence.
        if (!inventory.Available) return inventory;
        var registered = new List<RegisteredSoftware>();
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall", writable: false);
                if (uninstall is null) continue;
                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var app = uninstall.OpenSubKey(name, writable: false);
                        if (app?.GetValue("DisplayName") is not string displayName || app.GetValue("Publisher") is not string publisher) continue;
                        registered.Add(new(displayName, publisher, app.GetValue("DisplayVersion") as string ?? ""));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { /* Keep other readable registrations. */ }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException) { /* No absence claim for an unreadable registry view. */ }
        }
        return MergeRegistered(inventory, registered);
    }

    public static SoftwareInventory MergeRegistered(SoftwareInventory inventory, IEnumerable<RegisteredSoftware> registered)
    {
        var packages = inventory.Packages.ToList();
        foreach (var app in registered)
        {
            var rule = Rules.FirstOrDefault(rule => rule.Names.Contains(app.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                && rule.Publishers.Contains(app.Publisher.Trim(), StringComparer.OrdinalIgnoreCase));
            if (rule is null) continue;
            var entry = SoftwareCatalog.Entries.Single(entry => entry.Id == rule.EntryId);
            if (packages.Any(package => package.PackageId.Equals(entry.WingetId, StringComparison.OrdinalIgnoreCase)
                && package.Source.Equals(entry.Source, StringComparison.OrdinalIgnoreCase))) continue;
            packages.Add(new(entry.WingetId!, entry.Source, app.Version));
        }
        return inventory with { Packages = packages };
    }
}

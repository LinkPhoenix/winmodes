using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace WinModes.App.Services;

/// <summary>User preferences, stored per user. Automation is opt-in: nothing runs by itself until turned on.</summary>
internal sealed record AppSettings
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "WinModes";
    public const string MinimizedArgument = "--minimized";

    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinModes", "settings.json");

    /// <summary>Start hidden in the notification area when launched at sign-in.</summary>
    public bool StartMinimized { get; init; } = true;

    /// <summary>The close button hides the window and keeps the app in the notification area.</summary>
    public bool CloseToTray { get; init; } = true;

    /// <summary>Ask before activating a mode.</summary>
    public bool ConfirmBeforeActivate { get; init; } = true;

    /// <summary>Mode activated when the app starts; null means none.</summary>
    public string? AutoActivateMode { get; init; }

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(SettingsPath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new AppSettings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));

        // The Run entry carries the minimized flag, so keep it in step with the setting.
        if (StartsWithWindows)
        {
            SetStartWithWindows(true, StartMinimized);
        }
    }

    /// <summary>Whether the per-user Run entry starts the app at sign-in.</summary>
    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(RunValueName) is string;
        }
    }

    public static void SetStartWithWindows(bool enabled, bool minimized)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath);
        if (enabled && Environment.ProcessPath is { } path)
        {
            key.SetValue(RunValueName, minimized ? $"\"{path}\" {MinimizedArgument}" : $"\"{path}\"");
        }
        else
        {
            key.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }
}

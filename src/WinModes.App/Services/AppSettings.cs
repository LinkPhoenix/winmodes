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

    /// <summary>Draw the memory used by AI tools on the notification-area icon.</summary>
    public bool ShowAiMemoryInTray { get; init; }

    /// <summary>Show the small always-on-top desktop widget.</summary>
    public bool ShowDesktopWidget { get; init; }

    /// <summary>Ctrl+Alt+1, 2, 3 activate the modes in order; Ctrl+Alt+0 deactivates.</summary>
    public bool EnableHotkeys { get; init; }

    /// <summary>Notify when AI tools use at least this much memory, in GB; 0 turns the alert off.</summary>
    public int AiMemoryAlertGb { get; init; }

    /// <summary>Last position of the desktop widget; null until the user moves it.</summary>
    public double? WidgetLeft { get; init; }

    public double? WidgetTop { get; init; }

    /// <summary>Look and content of the desktop widget.</summary>
    public WidgetSettings Widget { get; init; } = new();

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

/// <summary>Options of the desktop widget, edited on the Widget page.</summary>
internal sealed record WidgetSettings
{
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>40 to 100.</summary>
    public int OpacityPercent { get; init; } = 95;

    /// <summary>80, 100, 125 or 150.</summary>
    public int ScalePercent { get; init; } = 100;

    public bool ShowCpu { get; init; } = true;
    public bool ShowMemory { get; init; } = true;
    public bool ShowNetwork { get; init; }
    public bool ShowMode { get; init; } = true;
    public bool ShowAiTools { get; init; } = true;

    /// <summary>List each tool under the AI total.</summary>
    public bool ShowToolDetail { get; init; } = true;

    public int MaxTools { get; init; } = 4;

    /// <summary>Seconds between two refreshes: 1, 3 or 5.</summary>
    public int RefreshSeconds { get; init; } = 3;

    /// <summary>The widget cannot be dragged.</summary>
    public bool LockPosition { get; init; }

    /// <summary>Mouse clicks go to the window underneath; the widget can then only be changed from this page.</summary>
    public bool ClickThrough { get; init; }
}
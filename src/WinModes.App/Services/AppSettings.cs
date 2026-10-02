using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using WinModes.Core;

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

    public const string LightTheme = "light";
    public const string DarkTheme = "dark";

    /// <summary>"dark" (default) or "light". Read once at startup.</summary>
    public string Theme { get; init; } = DarkTheme;

    /// <summary>Language of the interface: "en" (default), "fr", "es" or "it". Read once at startup.</summary>
    public string Language { get; init; } = WinModes.Core.Localization.Loc.DefaultLanguage;

    /// <summary>Ask GitHub once at startup whether a newer release exists.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>The first-run guide was finished or skipped.</summary>
    public bool OnboardingDone { get; init; }

    /// <summary>Hide project names, folders, command lines and the account name.</summary>
    public bool PrivacyMode { get; init; }

    /// <summary>Notify when a single AI tool uses at least this much memory, in GB; 0 turns the alert off.</summary>
    public int AiToolAlertGb { get; init; }

    /// <summary>End project sessions idle for this many minutes; 0 (the default) never ends anything.</summary>
    public int AutoEndIdleMinutes { get; init; }

    /// <summary>Keep daily totals of what each AI tool used per project. Off by default.</summary>
    public bool RecordUsageHistory { get; init; }

    /// <summary>Count the tokens Claude Code and Codex used, from the logs they keep on this PC. Off by default: it opens those logs.</summary>
    public bool ReadTokenLogs { get; init; }

    /// <summary>Last position of the desktop widget; null until the user moves it.</summary>
    public double? WidgetLeft { get; init; }

    public double? WidgetTop { get; init; }

    /// <summary>Look and content of the desktop widget.</summary>
    public WidgetSettings Widget { get; init; } = new();

    /// <summary>Which notifications are shown, edited on the Notifications page.</summary>
    public NotificationSettings Notifications { get; init; } = new();

    /// <summary>Automatic switching when a program starts. Off by default.</summary>
    public AutoSwitchSettings AutoSwitch { get; init; } = new();

    /// <summary>Mode activated when the app starts; null means none.</summary>
    public string? AutoActivateMode { get; init; }

    // Read on every sample tick of the tray, the widget and the pages, so the parsed copy is kept. Every field is
    // init-only, and this process is the only writer: Save replaces the copy. A hand edit of settings.json while
    // the app runs is picked up at the next start.
    private static readonly Lock CacheGate = new();
    private static AppSettings? _cached;

    public static AppSettings Load()
    {
        lock (CacheGate)
        {
            if (_cached is not null)
            {
                return _cached;
            }

            try
            {
                // A failed read gives defaults for this call only: caching them would hide the real file until a restart.
                if (!File.Exists(SettingsPath))
                {
                    return _cached = new AppSettings();
                }

                return _cached = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                return new AppSettings();
            }
        }
    }

    public void Save()
    {
        lock (CacheGate)
        {
            AtomicFile.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
            _cached = this;
        }

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

/// <summary>Opt-in automatic switching, edited on the Automation page.</summary>
internal sealed record AutoSwitchSettings
{
    public bool Enabled { get; init; }

    /// <summary>Undo an automatically activated mode once its program has exited.</summary>
    public bool RevertWhenClosed { get; init; } = true;

    public IReadOnlyList<WinModes.Core.Automation.AutoSwitchRule> Rules { get; init; } = [];
}

/// <summary>Where the widget is shown.</summary>
internal enum WidgetPlacement { Desktop, Taskbar, Both }

/// <summary>The notices wanted for one tool. Low is null until chosen, so the older alert choice of the widget still applies.</summary>
internal sealed record ToolNotices
{
    public bool? Low { get; init; }

    /// <summary>The limit is used up.</summary>
    public bool Reached { get; init; } = true;

    /// <summary>A limit that was used up started over.</summary>
    public bool Reset { get; init; } = true;

    /// <summary>A limit reset was added to the reserve of the account.</summary>
    public bool CreditGained { get; init; } = true;
}

/// <summary>What WinModes may notify about, edited on the Notifications page. Everything is on until the user turns it off.</summary>
internal sealed record NotificationSettings
{
    /// <summary>Master switch: off, no optional notification is shown (errors and the test notice still are).</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>A plan window counts as running low under this share left.</summary>
    public int LowPercent { get; init; } = 10;

    public ToolNotices Claude { get; init; } = new();

    public ToolNotices Codex { get; init; } = new();

    public bool UpdateAvailable { get; init; } = true;

    public bool SignInExpired { get; init; } = true;

    /// <summary>Result of a mode switch started from the tray, a shortcut, an automation rule or at start.</summary>
    public bool ModeChanges { get; init; } = true;

    public bool IdleSessionEnded { get; init; } = true;

    /// <summary>Hold back the optional notifications between <see cref="QuietFrom"/> and <see cref="QuietTo"/>. Off by default.</summary>
    public bool QuietHoursOn { get; init; }

    /// <summary>Start of the quiet hours, in minutes since midnight.</summary>
    public int QuietFrom { get; init; } = 22 * 60;

    /// <summary>End of the quiet hours, in minutes since midnight.</summary>
    public int QuietTo { get; init; } = 7 * 60;

    /// <summary>Whether it is quiet now. The plan watcher then waits instead of announcing: the notice comes when the quiet ends.</summary>
    public bool IsQuietNow() => QuietHoursOn && WinModes.Core.Notifications.QuietHours.IsQuiet(QuietFrom, QuietTo, TimeOnly.FromDateTime(DateTime.Now));

    public ToolNotices For(string tool) => tool == "Claude" ? Claude : Codex;

    /// <summary>What the plan notices have to send for a tool, the widget's older alert choice standing in for "running low" until chosen.</summary>
    public WinModes.Core.Notifications.PlanNoticeChoice ChoiceFor(string tool, WidgetSettings widget)
    {
        var notices = For(tool);
        return new(notices.Low ?? LegacyLow(tool, widget), notices.Reached, notices.Reset, notices.CreditGained);
    }

    public bool LowFor(string tool, WidgetSettings widget) => For(tool).Low ?? LegacyLow(tool, widget);

    private static bool LegacyLow(string tool, WidgetSettings widget) => (tool == "Claude" ? widget.ClaudeAlert : widget.CodexAlert) ?? true;
}

/// <summary>Options of the desktop widget, edited on the Widget page.</summary>
internal sealed record WidgetSettings
{
    public bool AlwaysOnTop { get; init; } = true;

    /// <summary>Where the widget is shown: floating on the desktop, in the room left on the taskbar by the app icons, or both.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public WidgetPlacement Placement { get; init; }

    /// <summary>Which free part of the taskbar the widget prefers; automatic picks the one with the most room.</summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
    public WinModes.Core.TaskbarSide TaskbarSide { get; init; }

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

    /// <summary>Plan, remaining usage and limit reset of Claude and Codex, read from their local files. Off by default.</summary>
    public bool ShowSubscriptions { get; init; }

    /// <summary>Which plans the block lists; a plan switched off is neither read nor asked online.</summary>
    public bool ShowClaudePlan { get; init; } = true;

    public bool ShowCodexPlan { get; init; } = true;

    /// <summary>Show how many limit resets the account has in reserve, when the online reading gives it.</summary>
    public bool ShowResetCredits { get; init; } = true;

    // The three alert settings below are read only as the starting value of the "running low" notice: the Notifications page
    // now owns it (see NotificationSettings), so an existing choice is kept without a migration.
    public bool PlanAlert { get; init; }

    /// <summary>
    /// Also ask Anthropic and OpenAI for the usage, with the sign-in Claude Code and Codex keep on this PC.
    /// Off by default: it reads their sign-in files and uses endpoints that are not part of a public API.
    /// Before each tool had its own choice this was shared.
    /// </summary>
    public bool ReadUsageOnline { get; init; }

    // Per-tool choices. Null means "never chosen": the shared value above applies, so nothing is lost when the app
    // is updated. The Widget page saves them explicitly.
    public bool? ReadClaudeOnline { get; init; }

    public bool? ReadCodexOnline { get; init; }

    public bool? ClaudeAlert { get; init; }

    public bool? CodexAlert { get; init; }

    [System.Text.Json.Serialization.JsonIgnore]
    public bool ClaudeOnline => ReadClaudeOnline ?? ReadUsageOnline;

    [System.Text.Json.Serialization.JsonIgnore]
    public bool CodexOnline => ReadCodexOnline ?? ReadUsageOnline;

    /// <summary>Seconds between two refreshes: 1, 3 or 5.</summary>
    public int RefreshSeconds { get; init; } = 3;

    /// <summary>One line with CPU, memory and the AI total instead of the full panel.</summary>
    public bool Compact { get; init; }

    /// <summary>Small CPU and memory history graphs under the bars.</summary>
    public bool ShowGraph { get; init; }

    /// <summary>Hide while a full-screen app (game, video, presentation) is in front.</summary>
    public bool HideOnFullScreen { get; init; } = true;

    /// <summary>The widget cannot be dragged.</summary>
    public bool LockPosition { get; init; }

    /// <summary>Mouse clicks go to the window underneath; the widget can then only be changed from this page.</summary>
    public bool ClickThrough { get; init; }
}
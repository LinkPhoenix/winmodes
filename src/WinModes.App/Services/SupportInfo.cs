using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using WinModes.Core;
using WinModes.Core.Accounts;
using WinModes.Core.Engine;
using WinModes.Core.Reports;
using WinModes.Core.Usage;
using Forms = System.Windows.Forms;

namespace WinModes.App.Services;

/// <summary>
/// What goes in the support file: facts about this install and PC that help to reproduce a problem, the settings and the error logs.
/// No sign-in, no token, no conversation: the account names are reduced to "signed in or not". The user name and the PC name are
/// taken out afterwards by <see cref="SupportBundle"/>, whatever file they appear in.
/// </summary>
internal static class SupportInfo
{
    public static IReadOnlyList<SupportFile> Collect()
    {
        var files = new List<SupportFile> { new("info.txt", Describe()) };
        Add(files, "settings.json", AppSettings.FilePath);
        Add(files, "errors.log", ErrorLog.DefaultPath);
        Add(files, "errors.log.old", ErrorLog.DefaultPath + ".old");
        Add(files, "helper-errors.log", AppPaths.HelperErrorLog);
        return files;
    }

    private static void Add(List<SupportFile> files, string name, string path)
    {
        try
        {
            if (File.Exists(path))
            {
                // Shared read: the app may be writing the file right now.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream);
                files.Add(new SupportFile(name, reader.ReadToEnd()));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            files.Add(new SupportFile(name, $"(could not be read: {ex.GetType().Name})"));
        }
    }

    private static string Describe()
    {
        var settings = AppSettings.Load();
        var text = new StringBuilder();
        void Line(string label, object? value) => text.Append(label).Append(": ").Append(value).AppendLine();

        Line("WinModes", $"{AppInfo.FullVersion} ({(UpdateInstaller.IsInstalledBuild ? "installed" : "portable or development build")})");
        Line("Created", DateTimeOffset.Now.ToString("u", System.Globalization.CultureInfo.InvariantCulture));
        Line("Windows", $"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
        Line(".NET", RuntimeInformation.FrameworkDescription);
        Line("Language / theme", $"{Loc.Current} / {settings.Theme}; privacy mode {OnOff(settings.PrivacyMode)}");
        Line("Processors", Environment.ProcessorCount);
        Line("Screens", string.Join(", ", Forms.Screen.AllScreens.Select(screen => $"{screen.Bounds.Width}x{screen.Bounds.Height}{(screen.Primary ? " primary" : "")}")));
        Line("Taskbar", $"alignment {TaskbarSetting("TaskbarAl", "1 = centred, 0 = left")}, on all displays {TaskbarSetting("MMTaskbarEnabled", "1 = yes")}");
        Line("Widget", $"shown {OnOff(settings.ShowDesktopWidget)}, {settings.Widget.Placement}, side {settings.Widget.TaskbarSide}, plans {OnOff(settings.Widget.ShowSubscriptions)}");
        Line("Claude Code / Codex files", $"{File.Exists(Subscriptions.DefaultClaudeSettings)} / {Directory.Exists(Subscriptions.DefaultCodexHome)}");
        Line("WinModes sign-in Claude / Codex", $"{AccountSession.IsSignedIn(AccountProvider.Claude)} / {AccountSession.IsSignedIn(AccountProvider.ChatGpt)}");
        Line("Notifications", $"{OnOff(settings.Notifications.Enabled)}, quiet hours {OnOff(settings.Notifications.QuietHoursOn)}");
        Line("Token statistics", OnOff(settings.ReadTokenLogs));
        Line("Start with Windows", $"{OnOff(AppSettings.StartsWithWindows)} ({AppSettings.StartupState})");
        Line("Active mode", ModeSwitcher.ActiveMode ?? "none");
        return text.ToString();
    }

    private static string OnOff(bool value) => value ? "on" : "off";

    private static string TaskbarSetting(string name, string meaning)
    {
        var value = Registry.GetValue(@"HKEY_CURRENT_USER\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Advanced", name, null);
        return value is null ? $"not set ({meaning})" : $"{value} ({meaning})";
    }
}

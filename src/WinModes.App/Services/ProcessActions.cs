using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using WinModes.Core.Planning;

namespace WinModes.App.Services;

/// <summary>Actions offered by the right-click menus of the process lists. Each one is started by the user.</summary>
internal static class ProcessActions
{
    private const int SystemIdlePid = 0;
    private const int SystemPid = 4;

    private static readonly ProcessSampler Sampler = new();
    private static readonly Lock SamplerLock = new();

    /// <summary>One shared sampler, so CPU figures stay consistent whichever page asks.</summary>
    public static IReadOnlyList<ProcessNode> Sample()
    {
        lock (SamplerLock)
        {
            return Sampler.Sample();
        }
    }

    /// <summary>Ends a process after confirmation. Returns a message when it could not be done.</summary>
    public static async Task<string?> EndAsync(int pid, string name, bool wholeTree, bool isProtected)
    {
        if (pid is SystemIdlePid or SystemPid || pid == Environment.ProcessId)
        {
            return Loc.T("This process cannot be ended from WinModes.");
        }

        var confirm = new Wpf.Ui.Controls.MessageBox
        {
            Title = Loc.F(wholeTree ? "End {0} and everything it started?" : "End {0}?", name),
            Content = Loc.F("Process {0} will be closed immediately. Unsaved work in it is lost.", pid)
                + (isProtected ? "\n\n" + Loc.T("This app is on the protected list: no mode would close it. End it only if you are sure.") : ""),
            PrimaryButtonText = Loc.T(wholeTree ? "End process tree" : "End task"),
            CloseButtonText = Loc.T("Cancel"),
        };
        if (await confirm.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: wholeTree);
            return null;
        }
        catch (ArgumentException)
        {
            return Loc.F("{0} had already exited.", name);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or AggregateException)
        {
            return Loc.F("{0} could not be ended: Windows denied access.", name);
        }
    }

    public static void OpenLocation(string? path)
    {
        if (path is null)
        {
            return;
        }

        if (File.Exists(path))
        {
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { $"/select,{path}" } });
        }
        else if (Directory.Exists(path))
        {
            using var explorer = Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path } });
        }
    }

    public static void Copy(string? text)
    {
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    public static void SearchOnline(string name)
    {
        var url = "https://www.bing.com/search?q=" + Uri.EscapeDataString(name + ".exe process");
        using var browser = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
}

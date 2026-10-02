using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace WinModes.App.Services;

/// <summary>What a notification is about; the Notifications page switches each one on or off.</summary>
internal enum NoticeKind
{
    /// <summary>Limits and reset credits of a plan (the per-tool choices are applied where they are decided).</summary>
    Plan,

    /// <summary>AI tools use more memory than the limit.</summary>
    Memory,

    /// <summary>An idle AI session was ended.</summary>
    Idle,

    /// <summary>A newer WinModes release exists.</summary>
    Update,

    /// <summary>The outcome of a mode switch.</summary>
    Mode,

    /// <summary>A WinModes sign-in ended on the provider's side.</summary>
    SignIn,

    /// <summary>Something went wrong or a switch failed: always shown, it is the only way to learn about it.</summary>
    Problem,

    /// <summary>The "test notification" button: always shown, to check that Windows lets notifications through.</summary>
    Test,
}

/// <summary>
/// Shows the notifications of the app through the tray icon, which Windows displays as toasts. It applies the user's choices
/// in one place and remembers what a click on the notification should open.
/// </summary>
internal sealed class Notifier
{
    private const int DefaultDurationMs = 6000;
    private const int MaxTitle = 63;
    private const int MaxText = 255;

    private readonly Forms.NotifyIcon _icon;
    private readonly Dispatcher _dispatcher;
    private Action? _onClick;

    public Notifier(Forms.NotifyIcon icon)
    {
        _icon = icon;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _icon.BalloonTipClicked += (_, _) =>
        {
            var action = _onClick;
            _onClick = null;
            action?.Invoke();
        };
    }

    /// <summary>Whether the user's choices let this kind of notification through.</summary>
    public static bool IsAllowed(NoticeKind kind, NotificationSettings settings) => kind switch
    {
        NoticeKind.Problem or NoticeKind.Test => true,
        _ when !settings.Enabled => false,
        // The answer to something the user just did is not held back; everything else waits for the end of the quiet hours.
        not NoticeKind.Mode when settings.IsQuietNow() => false,
        NoticeKind.Update => settings.UpdateAvailable,
        NoticeKind.SignIn => settings.SignInExpired,
        NoticeKind.Mode => settings.ModeChanges,
        NoticeKind.Idle => settings.IdleSessionEnded,
        _ => true,
    };

    /// <summary>Shows the notification when allowed; true when it was shown. Safe to call from any thread.</summary>
    public bool Show(NoticeKind kind, string title, string text, Forms.ToolTipIcon icon = Forms.ToolTipIcon.Info, Action? onClick = null, int durationMs = DefaultDurationMs)
    {
        if (!IsAllowed(kind, AppSettings.Load().Notifications))
        {
            return false;
        }

        if (_dispatcher.CheckAccess())
        {
            Display(title, text, icon, onClick, durationMs);
        }
        else
        {
            _dispatcher.BeginInvoke(() => Display(title, text, icon, onClick, durationMs));
        }

        return true;
    }

    private void Display(string title, string text, Forms.ToolTipIcon icon, Action? onClick, int durationMs)
    {
        // The balloon of the tray icon is one at a time: the latest one decides what a click does.
        _onClick = onClick;
        _icon.ShowBalloonTip(durationMs, Shorten(title, MaxTitle), Shorten(text, MaxText), icon);
    }

    /// <summary>Windows cuts the title of a balloon at 63 characters and its text at 255: cut it here, with an ellipsis, instead.</summary>
    internal static string Shorten(string text, int limit) => text.Length <= limit ? text : text[..(limit - 1)] + "…";
}

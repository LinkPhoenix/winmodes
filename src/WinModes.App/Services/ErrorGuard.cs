using System.Windows;
using System.Windows.Threading;
using WinModes.Core;

namespace WinModes.App.Services;

/// <summary>
/// Last line of defence for exceptions nobody caught: most come from <c>async void</c> handlers and timers on
/// the interface thread. They are written to <see cref="ErrorLog"/> and the app keeps running, because a tray app
/// that closes silently loses the mode the user is in. Only an out-of-memory error is left to end the process.
/// </summary>
internal sealed class ErrorGuard
{
    private static readonly TimeSpan NoticeInterval = TimeSpan.FromMinutes(1);

    private readonly ErrorLog _log;
    private readonly Action _notify;
    private DateTime _lastNotice = DateTime.MinValue;

    private ErrorGuard(ErrorLog log, Action notify)
    {
        _log = log;
        _notify = notify;
    }

    /// <param name="notify">Tells the user something went wrong; called at most once a minute, on the interface thread.</param>
    public static ErrorGuard Register(Application app, ErrorLog log, Action notify)
    {
        var guard = new ErrorGuard(log, notify);
        app.DispatcherUnhandledException += guard.OnDispatcherException;
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Append("task", e.Exception);
            e.SetObserved();
        };
        // Cannot be recovered from, but the cause is worth keeping.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception)
            {
                log.Append("fatal", exception);
            }
        };
        return guard;
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Append("ui", e.Exception);
        if (e.Exception is OutOfMemoryException)
        {
            return;
        }

        e.Handled = true;
        if (DateTime.UtcNow - _lastNotice >= NoticeInterval)
        {
            _lastNotice = DateTime.UtcNow;
            _notify();
        }
    }
}

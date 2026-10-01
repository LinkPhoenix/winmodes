using WinModes.Core.Planning;

namespace WinModes.App.Services;

/// <summary>
/// Remembers when each AI session last used the CPU. A session counts as idle only for the time
/// WinModes has actually watched it, so nothing is called idle right after the app starts.
/// </summary>
internal static class AiActivityTracker
{
    // Below this a session is only keeping its process alive.
    private const double ActiveCpuPercent = 0.2;
    private static readonly Dictionary<int, (DateTime? Start, DateTime LastActive)> LastActive = [];
    private static readonly Lock Gate = new();

    public static void Observe(IReadOnlyList<AiSession> sessions)
    {
        var now = DateTime.Now;
        lock (Gate)
        {
            foreach (var session in sessions)
            {
                var pid = session.Root.Pid;
                var known = LastActive.TryGetValue(pid, out var entry) && entry.Start == session.Root.StartTime;
                if (!known || session.TotalCpuPercent >= ActiveCpuPercent)
                {
                    LastActive[pid] = (session.Root.StartTime, now);
                }
            }

            var alive = sessions.Select(session => session.Root.Pid).ToHashSet();
            foreach (var pid in LastActive.Keys.Where(pid => !alive.Contains(pid)).ToList())
            {
                LastActive.Remove(pid);
            }
        }
    }

    /// <summary>How long the session has been observed without CPU activity.</summary>
    public static TimeSpan IdleFor(AiSession session)
    {
        lock (Gate)
        {
            return LastActive.TryGetValue(session.Root.Pid, out var entry) && entry.Start == session.Root.StartTime
                ? DateTime.Now - entry.LastActive
                : TimeSpan.Zero;
        }
    }
}

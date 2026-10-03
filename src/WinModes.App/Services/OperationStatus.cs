namespace WinModes.App.Services;

internal sealed record OperationSnapshot(Guid Id, string Title, int Total, int Completed, string Detail,
    bool IsRunning, bool Failed, DateTimeOffset StartedUtc, DateTimeOffset? FinishedUtc);

/// <summary>Latest operation in this app session. Never presented as a durable journal.</summary>
internal static class OperationStatus
{
    private static readonly object Gate = new();
    private static OperationSnapshot? _current;
    public static event Action? Changed;
    public static OperationSnapshot? Current { get { lock (Gate) { return _current; } } }

    public static bool TryBegin(string title, int total, out Guid id)
    {
        id = Guid.NewGuid();
        lock (Gate)
        {
            if (_current is { IsRunning: true }) { id = Guid.Empty; return false; }
            _current = new(id, title, Math.Max(0, total), 0, "", true, false, DateTimeOffset.UtcNow, null);
        }
        Changed?.Invoke();
        return true;
    }

    public static void Progress(Guid id, int completed, string detail)
    {
        lock (Gate)
        {
            if (_current is not { IsRunning: true } current || current.Id != id) { return; }
            _current = current with { Completed = Math.Clamp(completed, 0, current.Total), Detail = detail };
        }
        Changed?.Invoke();
    }

    public static void Complete(Guid id, string summary, bool failed = false)
    {
        lock (Gate)
        {
            if (_current is not { IsRunning: true } current || current.Id != id) { return; }
            _current = current with { Detail = summary, IsRunning = false, Failed = failed, FinishedUtc = DateTimeOffset.UtcNow };
        }
        Changed?.Invoke();
    }
}

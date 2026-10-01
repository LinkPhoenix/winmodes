namespace WinModes.Core;

/// <summary>
/// Lets one operation run at a time. A second request made while one is running is refused rather than queued:
/// queued mode switches would stack administrator prompts and apply a pressed hotkey twice.
/// </summary>
public sealed class OperationGate
{
    private int _running;

    /// <returns>Ran is false, and the operation was not started, when another one is still running.</returns>
    public async Task<(bool Ran, T? Result)> TryRunAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            return (false, default);
        }

        try
        {
            return (true, await operation());
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}

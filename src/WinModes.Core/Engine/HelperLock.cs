namespace WinModes.Core.Engine;

/// <summary>
/// Makes the elevated helper run one command at a time, whoever started it (a prompt, the silent task). Two helpers
/// reading the journal at once would each record the other's change as the "before" state. The mutex is machine
/// wide and created by an elevated process, so its default access rights keep ordinary programs from holding it.
/// A mutex belongs to the thread that took it: acquire and dispose on the same thread.
/// </summary>
public sealed class HelperLock : IDisposable
{
    public const string DefaultName = @"Global\WinModes.Elevated";

    private readonly Mutex _mutex;
    private bool _held;

    private HelperLock(Mutex mutex, bool held)
    {
        _mutex = mutex;
        _held = held;
    }

    /// <returns>The lock, or null when another helper still holds it after <paramref name="timeout"/>.</returns>
    public static HelperLock? TryAcquire(TimeSpan timeout, string name = DefaultName)
    {
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            return new HelperLock(mutex, mutex.WaitOne(timeout)) is { _held: true } acquired ? acquired : Discard(mutex);
        }
        catch (AbandonedMutexException)
        {
            // The previous helper died holding it. Its journal entries are written before each change, so going on is safe.
            return new HelperLock(mutex, held: true);
        }
    }

    public void Dispose()
    {
        if (_held)
        {
            _held = false;
            _mutex.ReleaseMutex();
        }

        _mutex.Dispose();
    }

    private static HelperLock? Discard(Mutex mutex)
    {
        mutex.Dispose();
        return null;
    }
}

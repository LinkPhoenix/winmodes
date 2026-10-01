using WinModes.Core.Engine;

namespace WinModes.Core.Tests;

public sealed class HelperLockTests
{
    private static string UniqueName() => $@"Local\WinModes.Tests.{Guid.NewGuid():N}";

    // A mutex belongs to the thread that took it, so a helper is simulated on its own thread.
    private static Task Hold(string name, ManualResetEventSlim held, ManualResetEventSlim release) => Task.Factory.StartNew(() =>
    {
        using var heldLock = HelperLock.TryAcquire(TimeSpan.Zero, name);
        Assert.NotNull(heldLock);
        held.Set();
        release.Wait();
    }, TaskCreationOptions.LongRunning);

    [Fact]
    public async Task TryAcquire_GivesNullWhileAnotherHelperHoldsTheLock_AndSucceedsOnceItEnded()
    {
        var name = UniqueName();
        using var held = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var holder = Hold(name, held, release);
        held.Wait();

        Assert.Null(Probe(name, TimeSpan.FromMilliseconds(50)));

        release.Set();
        await holder;
        Assert.True(Probe(name, TimeSpan.FromSeconds(5)) is not null);
    }

    [Fact]
    public void Dispose_ReleasesTheLockForTheNextHelper()
    {
        var name = UniqueName();
        HelperLock.TryAcquire(TimeSpan.Zero, name)!.Dispose();

        Assert.NotNull(Probe(name, TimeSpan.Zero));
    }

    // Takes the lock and gives it back on the calling thread; the result only says whether it was free.
    private static object? Probe(string name, TimeSpan timeout)
    {
        using var probe = HelperLock.TryAcquire(timeout, name);
        return probe is null ? null : new object();
    }
}

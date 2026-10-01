namespace WinModes.Core.Tests;

public sealed class OperationGateTests
{
    [Fact]
    public async Task TryRunAsync_RefusesASecondOperationWhileTheFirstRuns()
    {
        var gate = new OperationGate();
        var release = new TaskCompletionSource();
        var first = gate.TryRunAsync(async () =>
        {
            await release.Task;
            return 1;
        });

        var second = await gate.TryRunAsync(() => Task.FromResult(2));

        Assert.False(second.Ran);
        release.SetResult();
        Assert.Equal((true, 1), await first);
    }

    [Fact]
    public async Task TryRunAsync_AllowsTheNextOperationOnceTheFirstEnded()
    {
        var gate = new OperationGate();

        Assert.Equal((true, 1), await gate.TryRunAsync(() => Task.FromResult(1)));
        Assert.Equal((true, 2), await gate.TryRunAsync(() => Task.FromResult(2)));
    }

    [Fact]
    public async Task TryRunAsync_ReleasesTheGateWhenTheOperationThrows()
    {
        var gate = new OperationGate();

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.TryRunAsync<int>(() => throw new InvalidOperationException()));

        Assert.Equal((true, 3), await gate.TryRunAsync(() => Task.FromResult(3)));
    }
}

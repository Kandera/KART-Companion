namespace KARTCompanion.Tests;

public class SyncGateTests
{
    [Fact]
    public async Task RunAsync_WhenNotRunning_InvokesActionAndReturnsItsResult()
    {
        var gate = new SyncGate();

        var result = await gate.RunAsync(() => Task.FromResult(42));

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_WhileAlreadyRunning_SkipsSecondCallInsteadOfRunningConcurrently()
    {
        var gate = new SyncGate();
        var firstCallStarted = new TaskCompletionSource();
        var releaseFirstCall = new TaskCompletionSource();
        var secondCallInvocations = 0;

        var firstTask = gate.RunAsync(async () =>
        {
            firstCallStarted.SetResult();
            await releaseFirstCall.Task;
            return 1;
        });

        await firstCallStarted.Task;
        var secondResult = await gate.RunAsync(() =>
        {
            secondCallInvocations++;
            return Task.FromResult(2);
        });

        Assert.Equal(0, secondResult);
        Assert.Equal(0, secondCallInvocations);

        releaseFirstCall.SetResult();
        Assert.Equal(1, await firstTask);
    }

    [Fact]
    public async Task RunAsync_WhenActionThrows_StillResetsSoTheNextCallRuns()
    {
        // Regression test for the bug where `_syncing = true; ... ; _syncing = false;` without
        // try/finally could permanently wedge the guard off if the action ever threw.
        var gate = new SyncGate();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => gate.RunAsync<int>(() => throw new InvalidOperationException("boom")));

        Assert.False(gate.IsRunning);

        var result = await gate.RunAsync(() => Task.FromResult(99));
        Assert.Equal(99, result);
    }
}

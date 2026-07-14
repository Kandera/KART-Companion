namespace KARTCompanion;

/// <summary>
/// Ensures only one async operation runs at a time and the "in progress" flag always resets —
/// even if the operation throws. Shared by TrayApplicationContext's background sync timer and
/// SettingsForm's Force Sync button, which previously each tracked their own `_syncing` bool
/// without a try/finally, risking a permanently wedged guard if an exception ever landed between
/// the flag being set and cleared.
/// </summary>
public sealed class SyncGate
{
    private int _running;

    public bool IsRunning => Volatile.Read(ref _running) != 0;

    /// <returns>The action's result, or default if a run was already in progress (skipped).</returns>
    public async Task<T?> RunAsync<T>(Func<Task<T>> action)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
            return default;

        try
        {
            return await action();
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}

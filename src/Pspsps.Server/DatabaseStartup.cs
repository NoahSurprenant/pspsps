namespace Pspsps.Server;

/// <summary>
/// Waits for the database at startup. A new pod's first connections can be refused for a while (its network policy is
/// still being applied, or PostgreSQL is restarting), longer than EF Core's retries last.
/// </summary>
internal static class DatabaseStartup
{
    /// <summary>
    /// Tries <paramref name="canConnect"/> now and then every <paramref name="interval"/> until it answers true, or until
    /// <paramref name="timeout"/> has passed; an attempt that throws counts as not yet. True once the database answered.
    /// </summary>
    public static async Task<bool> WaitAsync(
        Func<CancellationToken, Task<bool>> canConnect, TimeSpan timeout, TimeSpan interval, TimeProvider time,
        Func<TimeSpan, CancellationToken, Task> delay, CancellationToken cancellationToken)
    {
        var deadline = time.GetUtcNow() + timeout;
        while (true)
        {
            try
            {
                if (await canConnect(cancellationToken))
                {
                    return true;
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // Not reachable yet: try again until the deadline.
            }

            var left = deadline - time.GetUtcNow();
            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            await delay(left < interval ? left : interval, cancellationToken);
        }
    }
}

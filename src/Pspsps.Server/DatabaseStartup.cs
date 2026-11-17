namespace Pspsps.Server;

/// <summary>How long <see cref="DatabaseStartup.WaitAsync"/> waits in all, between attempts, and for one attempt.</summary>
internal sealed record DatabaseWaitOptions(TimeSpan Budget, TimeSpan Interval, TimeSpan AttemptTimeout);

/// <summary>
/// Waits for the database at startup. A new pod's first connections can be refused or dropped while its network policy
/// is applied (or PostgreSQL is restarting), for longer than EF Core's retries last.
/// </summary>
internal static class DatabaseStartup
{
    /// <summary>
    /// Tries <paramref name="connect"/> now and then every <see cref="DatabaseWaitOptions.Interval"/> until it
    /// succeeds or the server answers with an error (<paramref name="isAnswer"/>), or until
    /// <see cref="DatabaseWaitOptions.Budget"/> has passed on the monotonic clock. Each attempt is cut off after
    /// <see cref="DatabaseWaitOptions.AttemptTimeout"/>, so the last one can end up to that long past the budget. A
    /// failed attempt, timed out or thrown, counts as not yet and goes to <paramref name="onFailure"/> with its number.
    /// Only <paramref name="cancellationToken"/> stops the wait early. True once the server answered.
    /// </summary>
    /// <remarks>
    /// <paramref name="delay"/> is separate from <paramref name="time"/> so tests can move a fake clock forward in it,
    /// without a background loop advancing the clock while the wait sleeps.
    /// </remarks>
    public static async Task<bool> WaitAsync(
        Func<CancellationToken, Task> connect, Func<Exception, bool> isAnswer, Action<int, Exception> onFailure,
        DatabaseWaitOptions options, TimeProvider time, Func<TimeSpan, CancellationToken, Task> delay,
        CancellationToken cancellationToken)
    {
        var start = time.GetTimestamp();
        for (var attempt = 1; ; attempt++)
        {
            using (var timeout = new CancellationTokenSource(options.AttemptTimeout, time))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token))
            {
                try
                {
                    // WaitAsync cuts the attempt off even if connect doesn't watch its token.
                    await connect(linked.Token).WaitAsync(linked.Token);
                    return true;
                }
                // Only the caller's own cancellation stops the wait; any other one (a timeout) is "not yet".
                catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                {
                    if (isAnswer(e))
                    {
                        return true;
                    }

                    onFailure(attempt, timeout.IsCancellationRequested
                        ? new TimeoutException("The attempt timed out without an answer.", e)
                        : e);
                }
            }

            var left = options.Budget - time.GetElapsedTime(start);
            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            await delay(left < options.Interval ? left : options.Interval, cancellationToken);
        }
    }
}

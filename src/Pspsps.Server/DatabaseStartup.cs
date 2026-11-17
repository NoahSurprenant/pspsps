using System.Globalization;
using Npgsql;

namespace Pspsps.Server;

/// <summary>How long <see cref="DatabaseStartup.WaitAsync"/> waits in all, between attempts, and per attempt.</summary>
internal sealed record DatabaseWaitOptions(TimeSpan Budget, TimeSpan Interval, TimeSpan AttemptTimeout);

/// <summary>
/// Waits for the database at startup. A new pod's first connections can be refused or dropped while its network policy
/// is applied (or PostgreSQL is restarting), for longer than EF Core's retries last.
/// </summary>
internal static class DatabaseStartup
{
    public const string BudgetKey = "Database:StartupWaitSeconds";
    public const int DefaultBudgetSeconds = 90;
    public const int MaxBudgetSeconds = 120;

    /// <summary>
    /// The wait's settings: a budget of <see cref="BudgetKey"/> seconds, <see cref="DefaultBudgetSeconds"/> when
    /// unset (a value that isn't a whole number from 1 to <see cref="MaxBudgetSeconds"/> goes to
    /// <paramref name="invalid"/> and the default is used); an attempt every 2 s, each cut off after 5 s.
    /// </summary>
    /// <remarks>
    /// The chart's startup probe allows 240 s: the longest wait (120 s, plus one 5 s attempt) and the migration's
    /// retries (7 connects of at most 5 s, about 40 s of backoff) fit in it.
    /// </remarks>
    public static DatabaseWaitOptions OptionsFrom(IConfiguration configuration, Action<string> invalid)
    {
        var seconds = DefaultBudgetSeconds;
        var configured = configuration[BudgetKey];
        if (configured is not null)
        {
            if (int.TryParse(configured, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
                && parsed is >= 1 and <= MaxBudgetSeconds)
            {
                seconds = parsed;
            }
            else
            {
                invalid(configured);
            }
        }

        return new DatabaseWaitOptions(
            Budget: TimeSpan.FromSeconds(seconds), Interval: TimeSpan.FromSeconds(2),
            AttemptTimeout: TimeSpan.FromSeconds(5));
    }

    /// <summary>
    /// <see cref="WaitAsync"/> for PostgreSQL: each attempt opens a connection of its own (unpooled, so none is left
    /// idle after), and the server's non-transient errors (<see cref="IsPostgresAnswer"/>) count as an answer.
    /// </summary>
    public static Task<bool> WaitForPostgresAsync(
        string connectionString, DatabaseWaitOptions options, TimeProvider time, Action<int, Exception> onFailure,
        CancellationToken cancellationToken)
    {
        var check = CheckConnectionString(connectionString);
        return WaitAsync(
            async ct =>
            {
                await using var connection = new NpgsqlConnection(check);
                await connection.OpenAsync(ct);
            },
            IsPostgresAnswer, onFailure, options, time, (delay, ct) => Task.Delay(delay, time, ct), cancellationToken);
    }

    /// <summary>The connection string for the wait's own connections: not pooled.</summary>
    public static string CheckConnectionString(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;

    /// <summary>
    /// PostgreSQL answered with an error that won't go away by waiting (a missing database, a wrong password): the
    /// migration should run now and create the database or fail with it. Its transient errors (starting up, shutting
    /// down, too many connections), as Npgsql sorts them, are "not yet".
    /// </summary>
    public static bool IsPostgresAnswer(Exception exception) => exception is PostgresException { IsTransient: false };

    /// <summary>The messages of <paramref name="exception"/> and the exceptions inside it, down to the cause.</summary>
    public static string Describe(Exception exception)
    {
        var messages = new List<string>();
        for (var e = exception; e is not null; e = e.InnerException)
        {
            messages.Add(e.Message);
        }

        return string.Join(" -> ", messages);
    }

    /// <summary>
    /// Tries <paramref name="connect"/> now and then every <see cref="DatabaseWaitOptions.Interval"/> until it
    /// succeeds or the server answers with an error (<paramref name="isAnswer"/>), or until
    /// <see cref="DatabaseWaitOptions.Budget"/> has passed on the monotonic clock. Each attempt's token is cancelled
    /// after <see cref="DatabaseWaitOptions.AttemptTimeout"/>, so the last one can end up to that long past the budget.
    /// A failed attempt counts as not yet and goes to <paramref name="onFailure"/> with its number: as a
    /// <see cref="TimeoutException"/> when it stopped for its timeout, otherwise as its own error. Only
    /// <paramref name="cancellationToken"/> stops the wait early. True once the server answered.
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
                    // connect honours its token (Npgsql's OpenAsync does), so the timeout ends the attempt.
                    await connect(linked.Token);
                    return true;
                }
                // Only the caller's own cancellation stops the wait; any other one (a timeout) is "not yet".
                catch (Exception e) when (!cancellationToken.IsCancellationRequested)
                {
                    // Timed out only when the attempt stopped for its own timeout: a real error that came as the
                    // timer fired is reported as itself.
                    if (e is OperationCanceledException && timeout.IsCancellationRequested)
                    {
                        var message = string.Create(
                            CultureInfo.InvariantCulture, $"No answer within {options.AttemptTimeout.TotalSeconds} s.");
                        onFailure(attempt, new TimeoutException(message, e));
                    }
                    else if (isAnswer(e))
                    {
                        return true;
                    }
                    else
                    {
                        onFailure(attempt, e);
                    }
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

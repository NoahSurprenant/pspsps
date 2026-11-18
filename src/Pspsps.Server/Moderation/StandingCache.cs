using System.Collections.Concurrent;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Moderation;

namespace Pspsps.Server.Moderation;

/// <summary>
/// A player's ban and mute, for the hub to refuse their every call and their chat: read from the database at most once
/// every <see cref="KeepFor"/> per player on this pod (a move is a call), and again at once after a change made on this
/// pod. A ban or mute decided on another pod is seen here within <see cref="KeepFor"/>; one read here ends at its time.
/// When the database can't answer, the last read holds (a restart doesn't lift a ban), and a player never read plays: a
/// short outage doesn't stop the whole game.
/// </summary>
public sealed partial class StandingCache(
    IServiceScopeFactory scopes,
    [FromKeyedServices(ModerationClock.Key)] TimeProvider time,
    ILogger<StandingCache>? logger = null)
{
    /// <summary>How long a player's standing, once read, is trusted on this pod.</summary>
    public static readonly TimeSpan KeepFor = TimeSpan.FromSeconds(5);

    // Past this many players kept, the stale ones are dropped at the next read.
    private const int PruneAbove = 1000;

    // Each player's last read, or a mark that it changed (Forget), which no read that started before can replace.
    private readonly ConcurrentDictionary<Guid, Read> _read = new();

    // When stale reads were last dropped: at most once every KeepFor, not on every miss.
    private DateTimeOffset _prunedAt = DateTimeOffset.MinValue;

    /// <summary>The ban the player is under now, if any.</summary>
    public async Task<Sanction?> BanOfAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var standing = await StandingOfAsync(playerId, cancellationToken);
        return standing.Ban is { } ban && ban.InForceAt(time.GetUtcNow()) ? ban : null;
    }

    /// <summary>Refuses chat while the player is muted (the Domain's rule), saying why and until when.</summary>
    public async Task EnsureMayChatAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var standing = await StandingOfAsync(playerId, cancellationToken);
        PlayerModeration.EnsureMayChat(standing.Mute, time.GetUtcNow());
    }

    /// <summary>Forgets what was read of the player: their standing changed.</summary>
    public void Forget(Guid playerId) => _read[playerId] = new Read(Standing.None, time.GetUtcNow(), changed: true);

    private async Task<Standing> StandingOfAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        _read.TryGetValue(playerId, out var seen);
        if (seen is { Changed: false } && now - seen.At < KeepFor && now >= seen.At)
        {
            return seen.Standing;
        }

        Standing standing;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            standing = await scope.ServiceProvider.GetRequiredService<IModerationService>()
                .StandingAsync(playerId, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (logger is not null)
            {
                LogNotRead(logger, e, playerId);
            }

            return seen is { Changed: false } ? seen.Standing : Standing.None;
        }

        if (_read.Count > PruneAbove && now - _prunedAt >= KeepFor)
        {
            _prunedAt = now;
            Prune(now);
        }

        // Kept only if nothing changed the player since this read began (atomically: a Forget in between wins).
        var read = new Read(standing, now, changed: false);
        if (seen is null)
        {
            _read.TryAdd(playerId, read);
        }
        else
        {
            _read.TryUpdate(playerId, read, seen);
        }

        return standing;
    }

    // Drops reads (and change marks) older than KeepFor: a read begun before such a mark has long finished.
    private void Prune(DateTimeOffset now)
    {
        foreach (var (playerId, read) in _read)
        {
            if (now - read.At >= KeepFor)
            {
                _read.TryRemove(new KeyValuePair<Guid, Read>(playerId, read));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read whether player {PlayerId} is banned or muted; the last read holds")]
    private static partial void LogNotRead(ILogger logger, Exception exception, Guid playerId);

    // A class, not a record: compared by reference, so a read replaces only the very entry it saw.
    private sealed class Read(Standing standing, DateTimeOffset at, bool changed)
    {
        public Standing Standing { get; } = standing;

        public DateTimeOffset At { get; } = at;

        public bool Changed { get; } = changed;
    }
}

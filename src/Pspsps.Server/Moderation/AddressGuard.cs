using System.Collections.Concurrent;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Moderation;

namespace Pspsps.Server.Moderation;

/// <summary>
/// Keeps banned networks out (<see cref="AddressBan"/>): whether a request's or a connection's network is banned, never
/// for an admin (by the configured list: the owner can't lock themself out), and remembering which networks each player
/// plays from. Does nothing when network bans are off (<see cref="ClientAddresses"/>).
/// </summary>
public sealed partial class AddressGuard(
    ClientAddresses addresses,
    AddressBanCache bans,
    IAdmins admins,
    IServiceScopeFactory scopes,
    ILogger<AddressGuard> logger)
{
    /// <summary>The SignalR group of the connections on a network (not an admin's): told at once when it is banned.</summary>
    public static string GroupOf(string addressHash) => "network:" + addressHash;

    public bool Enabled => addresses.Enabled;

    /// <summary>
    /// The network to hold <paramref name="playerId"/>'s request to: its hash, or null when network bans are off, there's no
    /// address, or the player is an admin.
    /// </summary>
    public string? NetworkOf(HttpContext http, Guid? playerId) =>
        playerId is { } id && admins.Current.Contains(id) ? null : addresses.HashOf(http);

    /// <summary>
    /// The ban on the network the request came from, for <paramref name="playerId"/> (null: someone not signed in yet);
    /// never for an admin.
    /// </summary>
    public Task<Sanction?> BanOfAsync(HttpContext http, Guid? playerId, CancellationToken cancellationToken) =>
        BanOfAsync(NetworkOf(http, playerId), cancellationToken);

    /// <summary>The ban on the network <paramref name="addressHash"/> (null: none to hold to), as this pod last read it.</summary>
    public Task<Sanction?> BanOfAsync(string? addressHash, CancellationToken cancellationToken) =>
        addressHash is null ? Task.FromResult<Sanction?>(null) : bans.BanOfAsync(addressHash, cancellationToken);

    /// <summary>
    /// Remembers that the player played from the request's network (for 7 days). Never throws: a network not remembered
    /// only can't be banned.
    /// </summary>
    public Task SeenAsync(HttpContext http, Guid playerId) => SeenAsync(addresses.HashOf(http), playerId);

    /// <summary>As <see cref="SeenAsync(HttpContext, Guid)"/>, with the network's hash (null: nothing to remember).</summary>
    public async Task SeenAsync(string? addressHash, Guid playerId)
    {
        if (addressHash is null)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            // Not a request's token: a sign-in that is done is remembered even if the browser goes meanwhile.
            await scope.ServiceProvider.GetRequiredService<IAddressBanService>()
                .SeenAsync(playerId, addressHash, CancellationToken.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // The player's ID only: never the network, nor its hash.
            LogNotRemembered(logger, e, playerId);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't remember the network player {PlayerId} plays from")]
    private static partial void LogNotRemembered(ILogger logger, Exception exception, Guid playerId);
}

/// <summary>
/// The networks' bans, for refusing every hub call from one: read from the database at most once every
/// <see cref="KeepFor"/> per network on this pod, as <see cref="StandingCache"/> reads players' (a move is a call). A ban
/// or lift decided on this pod is known at once (<see cref="Know"/>); one decided on another pod is seen here within
/// <see cref="KeepFor"/>. When the database can't answer, the last read holds, and a network never read is let in.
/// </summary>
public sealed partial class AddressBanCache(
    IServiceScopeFactory scopes,
    [FromKeyedServices(ModerationClock.Key)] TimeProvider time,
    ILogger<AddressBanCache>? logger = null)
{
    /// <summary>How long a network's ban, once read, is trusted on this pod.</summary>
    public static readonly TimeSpan KeepFor = StandingCache.KeepFor;

    // Past this many networks kept, the stale ones are dropped at the next read.
    private const int PruneAbove = 1000;

    private readonly ConcurrentDictionary<string, Read> _read = new(StringComparer.Ordinal);

    private DateTimeOffset _prunedAt = DateTimeOffset.MinValue;

    /// <summary>The ban on the network now, if any.</summary>
    public async Task<Sanction?> BanOfAsync(string addressHash, CancellationToken cancellationToken)
    {
        var ban = await LastBanAsync(addressHash, cancellationToken);
        return ban is not null && ban.InForceAt(time.GetUtcNow()) ? ban : null;
    }

    /// <summary>The network's ban changed on this pod: it is <paramref name="ban"/> now (null: lifted).</summary>
    public void Know(string addressHash, Sanction? ban) => _read[addressHash] = new Read(ban, time.GetUtcNow());

    private async Task<Sanction?> LastBanAsync(string addressHash, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        _read.TryGetValue(addressHash, out var seen);
        if (seen is not null && now - seen.At < KeepFor && now >= seen.At)
        {
            return seen.Ban;
        }

        Sanction? ban;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            ban = await scope.ServiceProvider.GetRequiredService<IAddressBanService>().BanOfAsync(addressHash, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (logger is not null)
            {
                LogNotRead(logger, e);
            }

            return seen?.Ban;
        }

        if (now - _prunedAt >= KeepFor && _read.Count > PruneAbove)
        {
            _prunedAt = now;
            foreach (var (hash, read) in _read)
            {
                if (now - read.At >= KeepFor)
                {
                    _read.TryRemove(new KeyValuePair<string, Read>(hash, read));
                }
            }
        }

        // Kept only if nothing changed the network since this read began (a Know in between wins).
        var fresh = new Read(ban, now);
        if (seen is null)
        {
            _read.TryAdd(addressHash, fresh);
        }
        else
        {
            _read.TryUpdate(addressHash, fresh, seen);
        }

        return ban;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't read whether a network is banned; the last read holds")]
    private static partial void LogNotRead(ILogger logger, Exception exception);

    // A class: compared by reference, so a read replaces only the very entry it saw.
    private sealed class Read(Sanction? ban, DateTimeOffset at)
    {
        public Sanction? Ban { get; } = ban;

        public DateTimeOffset At { get; } = at;
    }
}

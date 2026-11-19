using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Moderation;
using Attempt = Pspsps.Application.Moderation.ModerationSaves.Attempt;

namespace Pspsps.Application.Moderation;

internal sealed class AddressBanService(
    IApplicationDbContext db, IAdmins admins, [FromKeyedServices(ModerationClock.Key)] TimeProvider time)
    : IAddressBanService
{
    /// <summary>How many of a player's networks an admin sees (the latest): as many as can be banned at once.</summary>
    internal const int RecentShown = AddressBan.MaxAtOnce;

    private const string NotBanned = "That network isn't banned.";

    public async Task SeenAsync(Guid playerId, string addressHash, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var seen = await db.PlayerAddresses
            .FirstOrDefaultAsync(a => a.PlayerId == playerId && a.AddressHash == addressHash, cancellationToken);
        if (seen is null)
        {
            seen = PlayerAddress.Seen(playerId, addressHash, now);
            db.PlayerAddresses.Add(seen);
        }
        else if (!seen.SeenAgain(now))
        {
            return;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException saving) when (db.IsUniqueViolation(saving))
        {
            // Another tab of the player's saved it at the same moment: seen either way.
            db.Forget(seen);
        }
    }

    public async Task<Sanction?> BanOfAsync(string addressHash, CancellationToken cancellationToken)
    {
        var ban = await db.AddressBans.AsNoTracking()
            .FirstOrDefaultAsync(b => b.AddressHash == addressHash, cancellationToken);
        return ban?.BanAt(time.GetUtcNow());
    }

    public async Task<IReadOnlyList<RecentAddress>> RecentOfAsync(
        Guid adminId, Guid playerId, CancellationToken cancellationToken)
    {
        _ = Moderator.Of(adminId, admins.Current);
        var now = time.GetUtcNow();
        var since = PlayerAddress.RecentSince(now);
        var recent = await db.PlayerAddresses.AsNoTracking()
            .Where(a => a.PlayerId == playerId && a.LastSeen > since)
            .OrderByDescending(a => a.LastSeen)
            .Take(RecentShown)
            .ToListAsync(cancellationToken);
        if (recent.Count == 0)
        {
            return [];
        }

        var hashes = recent.Select(a => a.AddressHash).ToList();
        var players = await PlayersOnAsync(db, hashes, now, cancellationToken);
        var bans = await db.AddressBans.AsNoTracking()
            .Where(b => hashes.Contains(b.AddressHash))
            .ToDictionaryAsync(b => b.AddressHash, cancellationToken);
        return
        [
            .. recent.Select(a =>
            {
                var ban = bans.GetValueOrDefault(a.AddressHash);
                return new RecentAddress(
                    a.AddressHash, AddressHashes.Label(a.AddressHash), players.GetValueOrDefault(a.AddressHash), a.LastSeen,
                    ban?.BanAt(now), ban?.IsBannedWith(playerId, now) ?? false);
            }),
        ];
    }

    public async Task<IReadOnlyList<BannedAddress>> BanAsync(
        Guid adminId, Guid playerId, IReadOnlyCollection<string> addressHashes, TimeSpan length,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(addressHashes);
        var moderator = Moderator.Of(adminId, admins.Current);
        var wanted = AddressBan.Picked(addressHashes);
        return await SaveAsync(async tracked =>
        {
            var now = time.GetUtcNow();
            // Not tracked: read for its ban, which the networks' bans take, and never changed here.
            var player = await db.Moderation.AsNoTracking()
                .FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken) ?? PlayerModeration.For(playerId);
            var seen = await db.PlayerAddresses.AsNoTracking()
                .Where(a => a.PlayerId == playerId && wanted.Contains(a.AddressHash))
                .ToDictionaryAsync(a => a.AddressHash, cancellationToken);
            var existing = await db.AddressBans
                .Where(b => wanted.Contains(b.AddressHash))
                .ToDictionaryAsync(b => b.AddressHash, cancellationToken);
            tracked.AddRange(existing.Values);

            var banned = new List<BannedAddress>();
            foreach (var hash in wanted)
            {
                var address = seen.GetValueOrDefault(hash) ?? throw new DomainException(
                    $"That cat wasn't on that network in the last {PlayerAddress.KeepFor.Days} days.");
                if (!existing.TryGetValue(hash, out var ban))
                {
                    ban = AddressBan.For(hash);
                    db.AddressBans.Add(ban);
                    tracked.Add(ban);
                }

                ban.BanWith(moderator, address, player, length, now);
                banned.Add(new BannedAddress(hash, ban.Label, ban.BanAt(now)!));
            }

            return banned;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<string>> LiftOfPlayerAsync(
        Guid adminId, Guid playerId, CancellationToken cancellationToken)
    {
        var moderator = Moderator.Of(adminId, admins.Current);
        return await SaveAsync<IReadOnlyList<string>>(async tracked =>
        {
            var now = time.GetUtcNow();
            var bans = await db.AddressBans
                .Where(b => b.PlayerId == playerId && b.LiftedAt == null && b.BannedUntil > now)
                .ToListAsync(cancellationToken);
            tracked.AddRange(bans);
            foreach (var ban in bans)
            {
                ban.Lift(moderator, now);
            }

            return [.. bans.Select(b => b.AddressHash)];
        }, cancellationToken);
    }

    public async Task LiftAsync(Guid adminId, string addressHash, CancellationToken cancellationToken)
    {
        var moderator = Moderator.Of(adminId, admins.Current);
        var hash = AddressHashes.Valid(addressHash);
        await SaveAsync(async tracked =>
        {
            var ban = await db.AddressBans.FirstOrDefaultAsync(b => b.AddressHash == hash, cancellationToken)
                ?? throw new DomainException(NotBanned);
            tracked.Add(ban);
            ban.Lift(moderator, time.GetUtcNow());
            return true;
        }, cancellationToken);
    }

    public async Task<int> PruneAsync(bool forgetAll, CancellationToken cancellationToken)
    {
        if (forgetAll)
        {
            return await db.PlayerAddresses.ExecuteDeleteAsync(cancellationToken)
                + await db.AddressBans.ExecuteDeleteAsync(cancellationToken);
        }

        var now = time.GetUtcNow();
        var since = PlayerAddress.RecentSince(now);
        var addresses = await db.PlayerAddresses
            .Where(a => a.LastSeen <= since)
            .ExecuteDeleteAsync(cancellationToken);
        var bans = await db.AddressBans
            .Where(b => b.LiftedAt != null || b.BannedUntil <= now)
            .ExecuteDeleteAsync(cancellationToken);
        return addresses + bans;
    }

    /// <summary>How many players were seen on each of the networks <paramref name="hashes"/> in the last 7 days.</summary>
    internal static async Task<Dictionary<string, int>> PlayersOnAsync(
        IApplicationDbContext db, IReadOnlyCollection<string> hashes, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = PlayerAddress.RecentSince(now);
        return await db.PlayerAddresses.AsNoTracking()
            .Where(a => hashes.Contains(a.AddressHash) && a.LastSeen > since)
            .GroupBy(a => a.AddressHash)
            .Select(g => new { Hash = g.Key, Players = g.Count() })
            .ToDictionaryAsync(g => g.Hash, g => g.Players, cancellationToken);
    }

    // Decides a change to networks' bans (<decide> reads them tracked, into the list it is given, and changes them by the
    // Domain's rules), with their new audit lines, saved at once and decided again on a conflict (ModerationSaves).
    private Task<T> SaveAsync<T>(Func<List<AddressBan>, Task<T>> decide, CancellationToken cancellationToken) =>
        ModerationSaves.SaveAsync(db, async (Attempt attempt) =>
        {
            var tracked = new List<AddressBan>();
            attempt.Forget.Add(() => tracked.ForEach(db.Forget));
            var result = await decide(tracked);
            attempt.BeforeSave.Add(() => tracked.ForEach(ban => db.ModerationEntries.AddRange(ban.Entries)));
            return result;
        }, cancellationToken);
}

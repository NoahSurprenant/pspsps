using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Moderation;

namespace Pspsps.Application.Moderation;

internal sealed class ModerationService(
    IApplicationDbContext db, IAdmins admins, [FromKeyedServices(ModerationClock.Key)] TimeProvider time)
    : IModerationService
{
    /// <summary>How many times a change is decided again when another landed on the same player meanwhile.</summary>
    internal const int Attempts = 5;

    /// <summary>How many lines of the audit trail the admin page shows.</summary>
    internal const int RecentLines = 50;

    private const string NoSuchCat = "That cat doesn't exist.";

    public bool IsAdmin(Guid playerId) => admins.Current.Contains(playerId);

    public async Task<Standing> StandingAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var moderation = await db.Moderation.AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken);
        return Standing.Of(moderation, time.GetUtcNow());
    }

    public async Task<Standing> ModerateAsync(
        Guid adminId, Guid playerId, ModerationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var moderator = Moderator.Of(adminId, admins.Current);
        if (!await db.Players.AnyAsync(p => p.Id == playerId, cancellationToken))
        {
            throw new DomainException(NoSuchCat);
        }

        return await ChangeAsync(playerId, create: true, moderation =>
        {
            var now = time.GetUtcNow();
            switch (request.Action)
            {
                case ModerationAction.Warn:
                    moderation.Warn(moderator, request.Reason ?? "", now);
                    break;
                case ModerationAction.Mute:
                    moderation.MuteUntil(
                        moderator, request.Reason ?? "", now + (request.Length ?? throw new DomainException("Say for how long.")), now);
                    break;
                case ModerationAction.Ban:
                    moderation.BanUntil(moderator, request.Reason ?? "", request.Length is { } length ? now + length : null, now);
                    break;
                case ModerationAction.Lift:
                    moderation.Lift(moderator, now);
                    break;
                default:
                    throw new DomainException("That isn't something an admin can do.");
            }

            return Standing.Of(moderation, now);
        }, cancellationToken) ?? Standing.None;
    }

    public Task<string?> TakeWarningAsync(Guid playerId, CancellationToken cancellationToken) =>
        ChangeAsync(playerId, create: false, moderation => moderation.TakeWarning(), cancellationToken);

    public async Task<ModerationOverview> OverviewAsync(Guid adminId, CancellationToken cancellationToken)
    {
        _ = Moderator.Of(adminId, admins.Current);
        var now = time.GetUtcNow();
        var sanctioned = await db.Moderation.AsNoTracking()
            .Where(m => (m.BanReason != null && (m.BannedUntil == null || m.BannedUntil > now))
                || (m.MuteReason != null && m.MutedUntil > now))
            .ToListAsync(cancellationToken);
        var recent = await db.ModerationEntries.AsNoTracking()
            .OrderByDescending(e => e.At)
            .Take(RecentLines)
            .ToListAsync(cancellationToken);

        var ids = sanctioned.Select(m => m.PlayerId)
            .Concat(sanctioned.SelectMany(m => new[] { m.BannedBy, m.MutedBy }).OfType<Guid>())
            .Concat(recent.SelectMany(e => new[] { e.PlayerId, e.ByPlayerId }))
            .Distinct()
            .ToList();
        var names = await db.Players.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        string NameOf(Guid id) => names.TryGetValue(id, out var name) ? name : "A cat that's gone";

        return new ModerationOverview(
            [
                .. sanctioned
                    .Select(m => (Moderation: m, Ban: m.BanAt(now), Mute: m.MuteAt(now)))
                    .OrderByDescending(s => s.Ban?.Since ?? s.Mute?.Since)
                    .Select(s => new SanctionedPlayer(
                        s.Moderation.PlayerId, NameOf(s.Moderation.PlayerId),
                        s.Ban, s.Ban is null ? null : NameOf(s.Ban.By),
                        s.Mute, s.Mute is null ? null : NameOf(s.Mute.By))),
            ],
            [
                .. recent.Select(e => new ModerationLogLine(
                    e.PlayerId, NameOf(e.PlayerId), e.Kind, e.ByPlayerId, NameOf(e.ByPlayerId), e.At, e.Reason, e.Until)),
            ]);
    }

    // Reads the player's moderation (made when <create> and there is none), applies the change, saves, with what it lines
    // in the audit trail. Version is a concurrency token: when another change landed between the read and the save (two
    // admins at once, a warning taken as another is given), the save is refused and the change decided again on the
    // moderation as it is now. Two firsts at once collide on the key, and are decided again too. Null when there is
    // none and <create> is false.
    private async Task<T?> ChangeAsync<T>(
        Guid playerId, bool create, Func<PlayerModeration, T> change, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var moderation = await db.Moderation.FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken);
            if (moderation is null)
            {
                if (!create)
                {
                    return default;
                }

                moderation = PlayerModeration.For(playerId);
                db.Moderation.Add(moderation);
            }

            var recorded = moderation.Entries.Count;
            try
            {
                var result = change(moderation);
                db.ModerationEntries.AddRange(moderation.Entries.Skip(recorded));
                await db.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DomainException)
            {
                db.Forget(moderation);
                throw;
            }
            catch (DbUpdateException saving) when (saving is DbUpdateConcurrencyException || db.IsUniqueViolation(saving))
            {
                db.Forget(moderation);
                if (attempt == Attempts)
                {
                    throw new DomainException("That changed just now. Try again.");
                }
            }
        }
    }
}

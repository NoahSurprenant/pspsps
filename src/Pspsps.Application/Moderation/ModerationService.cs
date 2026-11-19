using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Players;
using Pspsps.Domain;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Players;
using Pspsps.Domain.Safety;
using Attempt = Pspsps.Application.Moderation.ModerationSaves.Attempt;

namespace Pspsps.Application.Moderation;

internal sealed class ModerationService(
    IApplicationDbContext db, IAdmins admins, IChatFilterProvider chatFilters,
    [FromKeyedServices(ModerationClock.Key)] TimeProvider time)
    : IModerationService
{
    /// <summary>How many lines of the audit trail the admin page shows.</summary>
    internal const int RecentLines = 50;

    /// <summary>How many open reports the admin page shows at once (the oldest).</summary>
    internal const int OpenReports = 50;

    /// <summary>How many banned or muted players the admin page shows (the latest).</summary>
    internal const int SanctionedShown = 100;

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

        return await SaveAsync(async attempt =>
        {
            // Only a rename touches the Player aggregate: everything else still just checks the cat exists, as before.
            Player? player = null;
            if (request.Action == ModerationAction.Rename)
            {
                // Tracked, not just checked exists: a rename changes it, in the same save as the audit line. Read inside
                // the attempt (like the moderation below), so a retry (another change landed on it meanwhile) reads it
                // fresh rather than saving a change already tried against a now-detached, once-renamed copy.
                player = await db.Players.FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken)
                    ?? throw new DomainException(NoSuchCat);
                attempt.Forget.Add(() => db.Forget(player));
            }
            else if (!await db.Players.AnyAsync(p => p.Id == playerId, cancellationToken))
            {
                throw new DomainException(NoSuchCat);
            }

            // Never null: made when there is none.
            var moderation = (await ReadAsync(playerId, create: true, attempt, cancellationToken))!;
            var now = time.GetUtcNow();
            if (player is not null)
            {
                // Checked before the Player aggregate changes: never renamed only to have the save refused.
                moderation.EnsureMayBeActedOnBy(moderator);
                var from = player.DisplayName;
                player.RenameByAdmin(request.Reason ?? "", chatFilters.Current);
                moderation.RecordRename(moderator, from, player.DisplayName, now);
            }
            else
            {
                Apply(moderation, moderator, request, now);
            }

            await FollowNetworksAsync(moderation, moderator, request, now, attempt, cancellationToken);
            return Standing.Of(moderation, now);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<PendingWarning>> WarningsAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var moderation = await db.Moderation.AsNoTracking()
            .FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken);
        if (moderation is null)
        {
            return [];
        }

        var warnings = await db.ModerationEntries.AsNoTracking()
            .Where(e => e.PlayerId == playerId && e.Kind == ModerationKind.Warned)
            .OrderBy(e => e.At)
            .ToListAsync(cancellationToken);
        return [.. warnings.Where(moderation.IsUnseenWarning).Select(e => new PendingWarning(e.Id, e.Reason ?? "", e.At))];
    }

    public async Task SawWarningAsync(Guid playerId, Guid warningId, CancellationToken cancellationToken)
    {
        var warning = await db.ModerationEntries.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.Id == warningId && e.PlayerId == playerId && e.Kind == ModerationKind.Warned, cancellationToken);
        if (warning is not null)
        {
            await ChangeAsync(playerId, create: false, moderation =>
            {
                moderation.SawWarningsUntil(warning.At);
                return true;
            }, cancellationToken);
        }
    }

    public async Task<ModerationOverview> OverviewAsync(Guid adminId, CancellationToken cancellationToken)
    {
        _ = Moderator.Of(adminId, admins.Current);
        var now = time.GetUtcNow();
        var sanctioned = await db.Moderation.AsNoTracking()
            .Where(m => (m.BanReason != null && (m.BannedUntil == null || m.BannedUntil > now))
                || (m.MuteReason != null && m.MutedUntil > now))
            // The latest, by the sanction in force (an ended ban's time stays on the row): bans for good pile up over the years.
            .OrderByDescending(m => m.BanReason != null && (m.BannedUntil == null || m.BannedUntil > now) ? m.BannedAt : m.MutedAt)
            .Take(SanctionedShown)
            .ToListAsync(cancellationToken);
        var recent = await db.ModerationEntries.AsNoTracking()
            .OrderByDescending(e => e.At)
            .Take(RecentLines)
            .ToListAsync(cancellationToken);
        var reports = await db.Reports.AsNoTracking()
            .Where(r => r.Status == ReportStatus.Open)
            .OrderBy(r => r.FiledAt)
            .Take(OpenReports)
            .ToListAsync(cancellationToken);
        var addressBans = await db.AddressBans.AsNoTracking()
            .Where(b => b.LiftedAt == null && b.BannedUntil > now)
            .OrderByDescending(b => b.BannedAt)
            .Take(SanctionedShown)
            .ToListAsync(cancellationToken);
        var sanctionedIds = sanctioned.Select(m => m.PlayerId).ToList();
        var networkBans = await db.AddressBans.AsNoTracking()
            .Where(b => sanctionedIds.Contains(b.PlayerId) && b.LiftedAt == null && b.BannedUntil > now)
            .GroupBy(b => b.PlayerId)
            .Select(g => new { PlayerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PlayerId, g => g.Count, cancellationToken);
        var onNetworks = await AddressBanService.PlayersOnAsync(
            db, [.. addressBans.Select(b => b.AddressHash)], now, cancellationToken);

        var ids = reports.Select(r => r.ReporterId)
            .Concat(sanctioned.Select(m => m.PlayerId))
            .Concat(sanctioned.SelectMany(m => new[] { m.BannedBy, m.MutedBy }).OfType<Guid>())
            .Concat(recent.SelectMany(e => new[] { e.PlayerId, e.ByPlayerId }))
            .Concat(addressBans.SelectMany(b => new[] { b.PlayerId, b.BannedBy }))
            .Distinct()
            .ToList();
        var names = await db.Players.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);
        string NameOf(Guid id) => names.TryGetValue(id, out var name) ? name : "A cat that's gone";

        return new ModerationOverview(
            [
                .. reports.Select(r => new OpenReport(
                    r.Id, r.ReporterId, NameOf(r.ReporterId), r.ReportedId, r.ReportedName, r.Reason, r.Note, r.WorldId, r.Room,
                    r.FiledAt, [.. r.Context.OrderBy(line => line.Position)])),
            ],
            [
                .. sanctioned
                    .Select(m => (Moderation: m, Ban: m.BanAt(now), Mute: m.MuteAt(now)))
                    .OrderByDescending(s => s.Ban?.Since ?? s.Mute?.Since)
                    .Select(s => new SanctionedPlayer(
                        s.Moderation.PlayerId, NameOf(s.Moderation.PlayerId),
                        s.Ban, s.Ban is null ? null : NameOf(s.Ban.By),
                        s.Mute, s.Mute is null ? null : NameOf(s.Mute.By),
                        networkBans.GetValueOrDefault(s.Moderation.PlayerId))),
            ],
            [
                .. recent.Select(e => new ModerationLogLine(
                    e.PlayerId, NameOf(e.PlayerId), e.Kind, e.ByPlayerId, NameOf(e.ByPlayerId), e.At, e.Reason, e.Until,
                    e.Address)),
            ],
            [
                .. addressBans.Select(b => new AddressBanLine(
                    b.AddressHash, b.Label, b.PlayerId, NameOf(b.PlayerId), onNetworks.GetValueOrDefault(b.AddressHash),
                    b.BanAt(now)!, NameOf(b.BannedBy))),
            ]);
    }

    public Task<ReportDecided> DecideReportAsync(
        Guid adminId, Guid reportId, ModerationRequest? request, CancellationToken cancellationToken)
    {
        var moderator = Moderator.Of(adminId, admins.Current);
        // Warned, muted or banned (a lift isn't a decision on a report), checked before anything is read.
        var decision = request is null ? (ReportDecision?)null : DecisionOf(request.Action);
        return SaveAsync(async attempt =>
        {
            var report = await db.Reports.FirstOrDefaultAsync(r => r.Id == reportId, cancellationToken)
                ?? throw new DomainException("That report doesn't exist.");
            attempt.Forget.Add(() => db.Forget(report));
            var now = time.GetUtcNow();
            if (request is null || decision is not { } done)
            {
                report.Dismiss(moderator, now);
                return new ReportDecided(report.ReportedId, null, Standing.None);
            }

            // Reports outlive the players in them: one about a cat that's gone can only be dismissed.
            if (!await db.Players.AnyAsync(p => p.Id == report.ReportedId, cancellationToken))
            {
                throw new DomainException("That cat doesn't exist any more: dismiss the report.");
            }

            var moderation = await ReadAsync(report.ReportedId, create: true, attempt, cancellationToken);
            Apply(moderation!, moderator, request, now);
            await FollowNetworksAsync(moderation!, moderator, request, now, attempt, cancellationToken);
            report.CloseWith(moderator, done, now);
            // One save: the report closes with what was done, or neither happens.
            return new ReportDecided(report.ReportedId, request.Action, Standing.Of(moderation, now));
        }, cancellationToken);
    }

    private static ReportDecision DecisionOf(ModerationAction action) => action switch
    {
        ModerationAction.Warn => ReportDecision.Warned,
        ModerationAction.Mute => ReportDecision.Muted,
        ModerationAction.Ban => ReportDecision.Banned,
        _ => throw new DomainException("A report is dismissed, or the cat warned, muted or banned."),
    };

    // The admin's action, by the aggregate's rules. Rename is handled by the caller (ModerateAsync): it touches the
    // Player aggregate too, not just its moderation, and a report is never decided by renaming the reported cat.
    private static void Apply(PlayerModeration moderation, Moderator moderator, ModerationRequest request, DateTimeOffset now)
    {
        switch (request.Action)
        {
            case ModerationAction.Warn:
                moderation.Warn(moderator, request.Reason ?? "", now);
                break;
            case ModerationAction.Mute:
                moderation.MuteFor(
                    moderator, request.Reason ?? "", request.Length ?? throw new DomainException("Say for how long."), now);
                break;
            case ModerationAction.Ban:
                moderation.BanFor(moderator, request.Reason ?? "", request.Length, now);
                break;
            case ModerationAction.Lift:
                moderation.Lift(moderator, now);
                break;
            default:
                throw new DomainException("That isn't something an admin can do.");
        }
    }

    // A cat banned again: the networks banned with it end by its new ban's end (AddressBan.FollowCat), in the same save.
    private async Task FollowNetworksAsync(
        PlayerModeration moderation, Moderator moderator, ModerationRequest request, DateTimeOffset now, Attempt attempt,
        CancellationToken cancellationToken)
    {
        if (request.Action != ModerationAction.Ban)
        {
            return;
        }

        var bans = await db.AddressBans
            .Where(b => b.PlayerId == moderation.PlayerId && b.LiftedAt == null && b.BannedUntil > now)
            .ToListAsync(cancellationToken);
        foreach (var ban in bans)
        {
            attempt.Forget.Add(() => db.Forget(ban));
            ban.FollowCat(moderator, moderation, now);
            attempt.BeforeSave.Add(() => db.ModerationEntries.AddRange(ban.Entries));
        }
    }

    // Reads the player's moderation, applies the change, saves (SaveAsync). Null, and nothing saved, when there is none and
    // <create> is false.
    private Task<T?> ChangeAsync<T>(
        Guid playerId, bool create, Func<PlayerModeration, T> change, CancellationToken cancellationToken) =>
        SaveAsync<T?>(async attempt =>
        {
            var moderation = await ReadAsync(playerId, create, attempt, cancellationToken);
            return moderation is null ? default : change(moderation);
        }, cancellationToken);

    // The player's moderation, tracked (made when <create> and there is none), with the audit lines the change adds kept
    // for the save; and how to forget it all if the save is refused.
    private async Task<PlayerModeration?> ReadAsync(
        Guid playerId, bool create, Attempt attempt, CancellationToken cancellationToken)
    {
        var moderation = await db.Moderation.FirstOrDefaultAsync(m => m.PlayerId == playerId, cancellationToken);
        if (moderation is null)
        {
            if (!create)
            {
                return null;
            }

            moderation = PlayerModeration.For(playerId);
            db.Moderation.Add(moderation);
        }

        var recorded = moderation.Entries.Count;
        attempt.BeforeSave.Add(() => db.ModerationEntries.AddRange(moderation.Entries.Skip(recorded)));
        attempt.Forget.Add(() => db.Forget(moderation));
        return moderation;
    }

    private Task<T> SaveAsync<T>(Func<Attempt, Task<T>> decide, CancellationToken cancellationToken) =>
        ModerationSaves.SaveAsync(db, decide, cancellationToken);
}

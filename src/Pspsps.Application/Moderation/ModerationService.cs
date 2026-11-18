using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Safety;

namespace Pspsps.Application.Moderation;

internal sealed class ModerationService(
    IApplicationDbContext db, IAdmins admins, [FromKeyedServices(ModerationClock.Key)] TimeProvider time)
    : IModerationService
{
    /// <summary>How many times a change is decided again when another landed on the same player meanwhile.</summary>
    internal const int Attempts = 5;

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
        if (!await db.Players.AnyAsync(p => p.Id == playerId, cancellationToken))
        {
            throw new DomainException(NoSuchCat);
        }

        return await ChangeAsync(playerId, create: true, moderation =>
        {
            var now = time.GetUtcNow();
            Apply(moderation, moderator, request, now);
            return Standing.Of(moderation, now);
        }, cancellationToken) ?? Standing.None;
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
            // The latest: bans for good pile up over the years.
            .OrderByDescending(m => m.BannedAt ?? m.MutedAt)
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

        var ids = reports.Select(r => r.ReporterId)
            .Concat(sanctioned.Select(m => m.PlayerId))
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
                        s.Mute, s.Mute is null ? null : NameOf(s.Mute.By))),
            ],
            [
                .. recent.Select(e => new ModerationLogLine(
                    e.PlayerId, NameOf(e.PlayerId), e.Kind, e.ByPlayerId, NameOf(e.ByPlayerId), e.At, e.Reason, e.Until)),
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

    // The admin's action, by the aggregate's rules.
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

    // One try at a change: what to do just before its save (add the new audit lines), and how to forget what it read if the
    // save is refused.
    private sealed class Attempt
    {
        public List<Action> BeforeSave { get; } = [];

        public List<Action> Forget { get; } = [];
    }

    // Decides a change (<decide>: reads what it needs, tracked, and changes it by the Domain's rules), then saves it all in
    // one go. Version (a player's moderation) and Status (a report) are concurrency tokens: when another change landed
    // between the read and the save (two admins at once, a warning seen as another is given, a report closed meanwhile),
    // the save is refused and the change decided again on what is there now, which may refuse it. Two firsts at once
    // collide on a key, and are decided again too. A rule broken (a DomainException) saves nothing.
    private async Task<T> SaveAsync<T>(Func<Attempt, Task<T>> decide, CancellationToken cancellationToken)
    {
        for (var tries = 1; ; tries++)
        {
            var attempt = new Attempt();
            try
            {
                var result = await decide(attempt);
                attempt.BeforeSave.ForEach(step => step());

                await db.SaveChangesAsync(cancellationToken);
                return result;
            }
            catch (DomainException)
            {
                attempt.Forget.ForEach(step => step());
                throw;
            }
            catch (DbUpdateException saving) when (saving is DbUpdateConcurrencyException || db.IsUniqueViolation(saving))
            {
                attempt.Forget.ForEach(step => step());
                if (tries == Attempts)
                {
                    throw new DomainException("That changed just now. Try again.");
                }
            }
        }
    }
}

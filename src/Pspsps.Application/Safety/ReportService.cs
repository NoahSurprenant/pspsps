using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;

namespace Pspsps.Application.Safety;

internal sealed class ReportService(IApplicationDbContext db, IMuteService mutes, TimeProvider time) : IReportService
{
    /// <summary>What a line by a cat that no longer exists is said by.</summary>
    internal const string UnknownSpeaker = "A cat";

    public async Task<ReportOutcome> FileAsync(
        Guid playerId,
        Guid otherId,
        ReportReason reason,
        string? note,
        string worldId,
        string room,
        IReadOnlyList<ChatLine> chat,
        bool otherIsHere,
        bool alsoMute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chat);
        var reportedName = await db.Players.Where(p => p.Id == otherId).Select(p => p.DisplayName)
            .FirstOrDefaultAsync(cancellationToken) ?? throw new DomainException("That cat doesn't exist.");

        // The speakers' names as they are now: a line's cat may have left the room since.
        var speakers = chat.Select(l => l.PlayerId).Distinct().ToList();
        var names = await db.Players.Where(p => speakers.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.DisplayName, cancellationToken);

        var now = time.GetUtcNow();
        var since = now - Report.Window;
        var past = await db.Reports.Where(r => r.ReporterId == playerId && r.FiledAt > since)
            .Select(r => new { r.ReportedId, r.FiledAt, r.Status })
            .ToListAsync(cancellationToken);

        var report = Report.File(
            playerId,
            otherId,
            reportedName,
            reason,
            note,
            worldId,
            room,
            chat.Select(l => new ChatContextLine(l.PlayerId, names.GetValueOrDefault(l.PlayerId, UnknownSpeaker), l.Text, l.At)),
            otherIsHere,
            past.Select(r => new PastReport(r.ReportedId, r.FiledAt, r.Status)),
            now);
        db.Reports.Add(report);

        Mute? mute = null;
        if (alsoMute)
        {
            var soFar = await MuteService.SoFarAsync(db, playerId, otherId, cancellationToken);
            // A player with every mute used still reports: the report matters more than the mute.
            if (!soFar.IncludesThem && Mute.HasRoom(soFar))
            {
                mute = Mute.Create(playerId, otherId, soFar, now);
                db.Mutes.Add(mute);
            }
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException saving) when (mute is not null && db.IsUniqueViolation(saving))
        {
            // Muted from another tab at the same moment: the report is saved on its own, and they're muted either way.
            db.Forget(mute);
            await db.SaveChangesAsync(cancellationToken);
        }

        var after = await mutes.ListAsync(playerId, cancellationToken);
        return new ReportOutcome(after.Any(m => m.PlayerId == otherId), after);
    }
}

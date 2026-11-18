using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain;
using Pspsps.Domain.Safety;

namespace Pspsps.Application.Safety;

internal sealed class MuteService(IApplicationDbContext db, TimeProvider time) : IMuteService
{
    public async Task<IReadOnlyList<MutedPlayer>> ListAsync(Guid playerId, CancellationToken cancellationToken)
    {
        var muted = await db.Mutes
            .Where(m => m.MuterId == playerId)
            .Join(db.Players, m => m.MutedId, p => p.Id, (m, p) => new MutedPlayer(p.Id, p.DisplayName, p.FurColor))
            .ToListAsync(cancellationToken);
        return
        [
            .. muted
                .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(m => m.PlayerId),
        ];
    }

    public async Task<IReadOnlyList<MutedPlayer>> MuteAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken)
    {
        if (!await db.Players.AnyAsync(p => p.Id == otherId, cancellationToken))
        {
            throw new DomainException("That cat doesn't exist.");
        }

        var mute = Mute.Create(playerId, otherId, await SoFarAsync(db, playerId, otherId, cancellationToken), time.GetUtcNow());
        db.Mutes.Add(mute);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException saving) when (db.IsUniqueViolation(saving))
        {
            // Muted from another tab at the same moment: muted either way.
            db.Forget(mute);
        }

        return await ListAsync(playerId, cancellationToken);
    }

    public async Task<IReadOnlyList<MutedPlayer>> UnmuteAsync(Guid playerId, Guid otherId, CancellationToken cancellationToken)
    {
        var mute = await db.Mutes.FirstOrDefaultAsync(m => m.MuterId == playerId && m.MutedId == otherId, cancellationToken)
            ?? throw new DomainException("You haven't muted that cat.");
        mute.Unmute(playerId);
        db.Mutes.Remove(mute);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Unmuted from another tab at the same moment: unmuted either way.
            db.Forget(mute);
        }

        return await ListAsync(playerId, cancellationToken);
    }

    /// <summary>The player's mutes so far, as <see cref="Mute.Create"/> needs them.</summary>
    internal static async Task<MutesSoFar> SoFarAsync(
        IApplicationDbContext db, Guid playerId, Guid otherId, CancellationToken cancellationToken)
    {
        var mine = db.Mutes.Where(m => m.MuterId == playerId);
        return new MutesSoFar(
            await mine.CountAsync(cancellationToken),
            await mine.AnyAsync(m => m.MutedId == otherId, cancellationToken));
    }
}

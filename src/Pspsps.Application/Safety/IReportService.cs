using Pspsps.Domain.Rooms;
using Pspsps.Domain.Safety;

namespace Pspsps.Application.Safety;

/// <summary>Players reporting other players, for the moderation tools to review.</summary>
public interface IReportService
{
    /// <summary>
    /// Files <paramref name="playerId"/>'s report of <paramref name="otherId"/> from the room <paramref name="room"/> of
    /// <paramref name="worldId"/>, with <paramref name="chat"/>: that room's recent chat as the server has it (never
    /// what a browser sends), oldest first. With <paramref name="alsoMute"/>, the other player is muted in the same save
    /// (if they aren't already). A rule broken (yourself, too many reports, a note too long) is a
    /// <see cref="Domain.DomainException"/>, and nothing is saved.
    /// </summary>
    Task<ReportOutcome> FileAsync(
        Guid playerId,
        Guid otherId,
        ReportReason reason,
        string? note,
        string worldId,
        string room,
        IReadOnlyList<ChatLine> chat,
        bool alsoMute,
        CancellationToken cancellationToken);
}

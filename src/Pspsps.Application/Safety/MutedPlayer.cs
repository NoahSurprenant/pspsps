using Pspsps.Domain.Players;

namespace Pspsps.Application.Safety;

/// <summary>A player another has muted, as the muter's list shows them.</summary>
public sealed record MutedPlayer(Guid PlayerId, string DisplayName, FurColor FurColor);

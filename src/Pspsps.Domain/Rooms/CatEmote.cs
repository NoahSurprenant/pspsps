namespace Pspsps.Domain.Rooms;

/// <summary>A cat sat, meowed, waved or fell asleep: shown to the room, not kept.</summary>
public sealed record CatEmote(Guid PlayerId, string Emote, DateTimeOffset At);

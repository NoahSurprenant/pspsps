namespace Pspsps.Domain.Safety;

/// <summary>A line of a room's recent chat, with the speaker's name, to file with a report (the room's, not the reporter's).</summary>
public sealed record ChatContextLine(Guid PlayerId, string DisplayName, string Text, DateTimeOffset At);

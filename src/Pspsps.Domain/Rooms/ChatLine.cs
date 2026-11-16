namespace Pspsps.Domain.Rooms;

public sealed record ChatLine(Guid PlayerId, string Text, DateTimeOffset At);

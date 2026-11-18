namespace Pspsps.Domain.Safety;

/// <summary>A player's mutes before muting another: how many, and whether the other is among them already.</summary>
public readonly record struct MutesSoFar(int Count, bool IncludesThem);

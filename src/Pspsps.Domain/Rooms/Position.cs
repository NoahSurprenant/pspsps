namespace Pspsps.Domain.Rooms;

/// <summary>A spot on a room's floor, in metres from the room's centre.</summary>
public readonly record struct Position(double X, double Z);

namespace Pspsps.Domain.Worlds;

/// <summary>A world: its own copy of every room, with its own population cap.</summary>
public sealed record WorldDefinition(string Id, string Name, int Capacity);

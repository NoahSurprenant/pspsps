namespace Pspsps.Domain.Worlds;

/// <summary>A world: its own copy of every room, with its own population cap. An admin-only world is neither listed nor entered by anyone else.</summary>
public sealed record WorldDefinition(string Id, string Name, int Capacity, bool AdminOnly = false);

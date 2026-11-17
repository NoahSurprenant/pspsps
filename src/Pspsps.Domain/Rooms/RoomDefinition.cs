namespace Pspsps.Domain.Rooms;

/// <summary>
/// A room's layout: a rectangular floor centred on the origin, where cats arrive, how many fit, a pond to fish in if it
/// has one, the doors out of it, and its props.
/// </summary>
public sealed record RoomDefinition(
    string Id, string Name, double Width, double Depth, Position Spawn, int Capacity, Pond? Pond = null)
{
    /// <summary>The doors out of this room, each to a door of another room.</summary>
    public IReadOnlyList<Door> Doors { get; init; } = [];

    /// <summary>The room's decoration, which cats walk through (<see cref="PropPlacement"/> has where props may stand).</summary>
    public IReadOnlyList<Prop> Props { get; init; } = [];

    /// <summary>
    /// Whether the shop's catalogue stands here (a <see cref="PropKinds.CatalogueStand"/> among the props): the only kind
    /// of room where things are bought (<see cref="Players.Player.Buy"/>).
    /// </summary>
    public bool HasCatalogue => Props.Any(p => p.Kind == PropKinds.CatalogueStand);

    /// <summary>The nearest spot on the floor to <paramref name="target"/>.</summary>
    public Position Clamp(Position target) => new(
        Math.Clamp(target.X, -Width / 2, Width / 2),
        Math.Clamp(target.Z, -Depth / 2, Depth / 2));

    /// <summary>The door with this ID; a <see cref="DomainException"/> when the room has none such.</summary>
    public Door GetDoor(string doorId) =>
        Doors.FirstOrDefault(d => d.Id == doorId) ?? throw new DomainException("There's no such door here.");
}

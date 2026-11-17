namespace Pspsps.Domain.Rooms;

public static class RoomCatalog
{
    public static IReadOnlyList<RoomDefinition> All { get; } =
    [
        new("town-square", "Town Square", Width: 24, Depth: 16, Spawn: new(0, 4), Capacity: 80,
            Pond: new Pond(new Position(-7, -3), Radius: 2.5))
        {
            Doors = [new Door("cafe", new Position(7, 7), Radius: 0.8, ToRoom: "cafe", ToDoor: "town-square")],
        },
        new("cafe", "Cat Café", Width: 14, Depth: 10, Spawn: new(0, 3), Capacity: 40)
        {
            Doors = [new Door("town-square", new Position(-4, 4), Radius: 0.8, ToRoom: "town-square", ToDoor: "cafe")],
        },
    ];

    public static RoomDefinition Default => All[0];

    public static RoomDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}

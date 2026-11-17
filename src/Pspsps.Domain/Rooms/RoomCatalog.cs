namespace Pspsps.Domain.Rooms;

public static class RoomCatalog
{
    public static IReadOnlyList<RoomDefinition> All { get; } =
    [
        new("town-square", "Town Square", Width: 24, Depth: 16, Spawn: new(0, 4), Capacity: 80,
            Pond: new Pond(new Position(-7, -3), Radius: 2.5)),
        new("cafe", "Cat Café", Width: 14, Depth: 10, Spawn: new(0, 3), Capacity: 40),
    ];

    public static RoomDefinition Default => All[0];

    public static RoomDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}

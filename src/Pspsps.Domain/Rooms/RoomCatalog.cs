namespace Pspsps.Domain.Rooms;

public static class RoomCatalog
{
    /// <summary>The shop's ID: the room whose catalogue stand sells everything (<see cref="RoomDefinition.HasCatalogue"/>).</summary>
    public const string ShopId = "shop";

    public static IReadOnlyList<RoomDefinition> All { get; } =
    [
        new("town-square", "Town Square", Width: 24, Depth: 16, Spawn: new(0, 4), Capacity: 80,
            Pond: new Pond(new Position(-7, -3), Radius: 2.5))
        {
            // Two of the shopfronts in the back wall are doorways: the café's, and the shop's.
            Doors =
            [
                new Door("cafe", new Position(7, 7), Radius: 0.8, ToRoom: "cafe", ToDoor: "town-square"),
                new Door(ShopId, new Position(-0.25, 7), Radius: 0.8, ToRoom: ShopId, ToDoor: "town-square"),
            ],
            // A plaza in front of a row of shops: trees and benches along the back and the sides, lamps, flower beds.
            Props =
            [
                new Prop(PropKinds.Tree, new Position(-10, 6), Radius: 1.4),
                new Prop(PropKinds.Tree, new Position(-3.6, 6.6), Radius: 1.2),
                new Prop(PropKinds.Tree, new Position(10.2, 4.6), Radius: 1.4),
                new Prop(PropKinds.Bench, new Position(-6.6, 6.9), Radius: 1),
                new Prop(PropKinds.Bench, new Position(9.8, -1.2), Radius: 1, Facing: Math.PI / 2),
                new Prop(PropKinds.Bench, new Position(1.2, -6.6), Radius: 1, Facing: Math.PI),
                new Prop(PropKinds.Lamp, new Position(9.2, 7.4), Radius: 0.35),
                new Prop(PropKinds.Lamp, new Position(4, -1.5), Radius: 0.35),
                new Prop(PropKinds.Lamp, new Position(-10.8, 1.6), Radius: 0.35),
                new Prop(PropKinds.Lamp, new Position(9.8, -5.8), Radius: 0.35),
                new Prop(PropKinds.FlowerBed, new Position(10.4, 1.6), Radius: 0.9),
                new Prop(PropKinds.FlowerBed, new Position(5.8, -6.6), Radius: 0.9),
                new Prop(PropKinds.FlowerBed, new Position(-3.4, -6.7), Radius: 0.9),
            ],
        },
        new("cafe", "Cat Café", Width: 14, Depth: 10, Spawn: new(0, 3), Capacity: 40)
        {
            Doors = [new Door("town-square", new Position(-4, 4), Radius: 0.8, ToRoom: "town-square", ToDoor: "cafe")],
            // A counter at the back, tables with two chairs each, plants, and a cat tree to climb (to look at).
            Props =
            [
                new Prop(PropKinds.Counter, new Position(4.4, 3), Radius: 1.9),
                new Prop(PropKinds.CafeTable, new Position(-4.3, -1.3), Radius: 1.2, Facing: 0.3),
                new Prop(PropKinds.CafeTable, new Position(0.3, -2.8), Radius: 1.2, Facing: -0.2),
                new Prop(PropKinds.CafeTable, new Position(4.3, -0.4), Radius: 1.2, Facing: 0.5),
                new Prop(PropKinds.Plant, new Position(-6.3, 4.3), Radius: 0.5),
                new Prop(PropKinds.Plant, new Position(1.6, 4.4), Radius: 0.5),
                new Prop(PropKinds.Plant, new Position(6.4, -4.4), Radius: 0.5),
                new Prop(PropKinds.CatTree, new Position(-6.1, -3.9), Radius: 0.8),
            ],
        },
        new(ShopId, "Cat Shop", Width: 13, Depth: 9, Spawn: new(-1.6, 1), Capacity: 40)
        {
            Doors = [new Door("town-square", new Position(-4.9, 3.6), Radius: 0.8, ToRoom: "town-square", ToDoor: ShopId)],
            // Shelves of hats along the back, the counter in the corner, a mirror to try things on in, a chair to wait in,
            // plants; and in the middle, the catalogue on its stand, where everything is bought.
            Props =
            [
                new Prop(PropKinds.CatalogueStand, new Position(1.4, -0.6), Radius: 0.6),
                new Prop(PropKinds.HatShelf, new Position(-1.4, 3.65), Radius: 0.8),
                new Prop(PropKinds.HatShelf, new Position(0.6, 3.65), Radius: 0.8),
                new Prop(PropKinds.ShopCounter, new Position(4.2, 3.3), Radius: 1.2),
                new Prop(PropKinds.Mirror, new Position(5.9, 0.4), Radius: 0.55, Facing: 0.6),
                new Prop(PropKinds.Armchair, new Position(-5.5, -1.4), Radius: 0.7, Facing: -0.9),
                new Prop(PropKinds.Plant, new Position(-6, -3.9), Radius: 0.5),
                new Prop(PropKinds.Plant, new Position(6, -3.9), Radius: 0.5),
                new Prop(PropKinds.Plant, new Position(2.2, 4.0), Radius: 0.45),
            ],
        },
    ];

    public static RoomDefinition Default => All[0];

    public static RoomDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}

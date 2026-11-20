namespace Pspsps.Domain.Rooms;

public static class RoomCatalog
{
    /// <summary>The shop's ID: the room whose catalogue stand sells everything (<see cref="RoomDefinition.HasCatalogue"/>).</summary>
    public const string ShopId = "shop";

    public static IReadOnlyList<RoomDefinition> All { get; } = RoomPlan.Join(
    [
        new RoomDefinition("town-square", "Town Square", Width: 24, Depth: 16, Spawn: new(0, 4), Capacity: 80,
            Pond: new Pond(new Position(-7, -3), Radius: 2.5))
        {
            // A plaza in front of a row of shops: trees and benches along the back and the sides, lamps, flower beds.
            Props =
            [
                new Prop(PropKinds.Tree, new Position(-10.5, 6.6), Radius: 1.4),
                new Prop(PropKinds.MapBoard, new Position(10.2, 4.6), Radius: 1.4),
                Prop.BenchFacing(new(-10.7, -6.7), new(-7, -3)),
                Prop.BenchFacing(new(9.8, -1.2), new(0, 4)),
                Prop.BenchFacing(new(0, -7), new(0, 4)),
                new Prop(PropKinds.Lamp, new Position(9.2, 7.4), Radius: 0.35),
                new Prop(PropKinds.Lamp, new Position(-6.7, -7.5), Radius: 0.35),
                new Prop(PropKinds.Lamp, new Position(9.8, -5.8), Radius: 0.35),
                new Prop(PropKinds.FlowerBed, new Position(10.4, 1.6), Radius: 0.9),
                new Prop(PropKinds.FlowerBed, new Position(-3.4, -6.7), Radius: 0.9),
            ],
        }.WithDoors(
            ("cafe", RoomEdge.North, 7, "cafe", "town-square"),
            (ShopId, RoomEdge.North, -6, ShopId, "town-square"),
            ("garden", RoomEdge.West, 3.3, "garden", "town-square"),
            ("riverside", RoomEdge.South, 6.4, "riverside", "town-square")),
        new RoomDefinition("cafe", "Cat Café", Width: 14, Depth: 10, Spawn: new(0, 3), Capacity: 40)
        {
            Exterior = new(8, 7, RoomEdge.South),
            // A counter at the back, tables with two chairs each, plants, and a cat tree to climb (to look at).
            Props =
            [
                new Prop(PropKinds.Counter, new Position(4.4, 3), Radius: 1.9),
                new Prop(PropKinds.Piano, new Position(-4.3, -1.3), Radius: 1.2, Facing: 0.3),
                new Prop(PropKinds.CafeTable, new Position(0.3, -2.8), Radius: 1.2, Facing: -0.2),
                new Prop(PropKinds.CafeTable, new Position(4.3, -0.4), Radius: 1.2, Facing: 0.5),
                new Prop(PropKinds.Plant, new Position(-6.3, 4.3), Radius: 0.5),
                new Prop(PropKinds.Plant, new Position(1.6, 4.4), Radius: 0.5),
                new Prop(PropKinds.Plant, new Position(6.4, -4.4), Radius: 0.5),
                new Prop(PropKinds.CatTree, new Position(-6.1, -3.9), Radius: 0.8),
            ],
        }.WithDoors(
            ("town-square", RoomEdge.North, -4, "town-square", "cafe")),
        new RoomDefinition(ShopId, "Cat Shop", Width: 13, Depth: 9, Spawn: new(-1.6, 1), Capacity: 40)
        {
            Exterior = new(7, 7, RoomEdge.South),
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
        }.WithDoors(
            ("town-square", RoomEdge.North, -4.9, "town-square", ShopId)),
        new RoomDefinition("garden", "Garden Courtyard", Width: 18, Depth: 12, Spawn: new(0, 1), Capacity: 40)
        {
            Props =
            [
                new Prop(PropKinds.PottingBench, new Position(-7.6, 2), Radius: 1.3),
                new Prop(PropKinds.Tree, new Position(6.9, 4.4), Radius: 1.3),
                new Prop(PropKinds.FlowerBed, new Position(-6.8, -0.4), Radius: 1.1),
                new Prop(PropKinds.FlowerBed, new Position(0.3, -3), Radius: 1.1),
                new Prop(PropKinds.FlowerBed, new Position(0, 3.5), Radius: 0.9),
                Prop.BenchFacing(new(-7.7, -2.5), new(-7.6, 2)),
                Prop.BenchFacing(new(-3.6, -4.8), new(0.3, -3)),
                new Prop(PropKinds.Lamp, new Position(1.2, -4.8), Radius: 0.35),
            ],
        }.WithDoors(
            ("town-square", RoomEdge.East, 1.8, "town-square", "garden"),
            ("pear-orchard", RoomEdge.South, 5.8, "pear-orchard", "garden")),
        new RoomDefinition("riverside", "Riverside Trail", Width: 24, Depth: 14, Spawn: new(0, 3), Capacity: 40,
            Pond: new Pond(new Position(-3, -2), Radius: 2.4))
        {
            Props =
            [
                new Prop(PropKinds.Tree, new Position(-8.4, -3.8), Radius: 1.35),
                new Prop(PropKinds.RodRack, new Position(4.3, -5.5), Radius: 1.4),
                new Prop(PropKinds.Tree, new Position(8.3, -2.5), Radius: 1.4),
                Prop.BenchFacing(new(3.5, -2.5), new(-3, -2)),
                new Prop(PropKinds.FlowerBed, new Position(1.1, -5.7), Radius: 0.9),
                new Prop(PropKinds.Lamp, new Position(8.2, -5.8), Radius: 0.35),
            ],
        }.WithDoors(
            ("pear-orchard", RoomEdge.West, 3.5, "pear-orchard", "riverside"),
            ("town-square", RoomEdge.North, 6.4, "town-square", "riverside")),
        new RoomDefinition("pear-orchard", "Pear Orchard", Width: 18, Depth: 19, Spawn: new(0, 0), Capacity: 40)
        {
            Props =
            [
                new Prop(PropKinds.PearTree, new(-6, 6), Radius: 1.4),
                new Prop(PropKinds.PearTree, new(-6, 0), Radius: 1.4),
                new Prop(PropKinds.PearTree, new(-6, -6), Radius: 1.4),
                new Prop(PropKinds.PackingCrate, new(2, -6), Radius: 1.4),
                Prop.BenchFacing(new(-3.6, -4.8), new(2, -6)),
            ],
        }.WithDoors(
            ("garden", RoomEdge.North, 5.8, "garden", "pear-orchard"),
            ("riverside", RoomEdge.East, 2.5, "riverside", "pear-orchard")),
    ]);

    public static RoomDefinition Default => All[0];

    public static RoomDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);
}

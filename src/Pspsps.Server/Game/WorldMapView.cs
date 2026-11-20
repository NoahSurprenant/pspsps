using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

/// <summary>The public rooms' outdoor floors and storefronts and the routes between their entrances.</summary>
[GenerateSerializer, Immutable]
public sealed record WorldMapView(
    [property: Id(0)] IReadOnlyList<MapRoomView> Rooms,
    [property: Id(1)] IReadOnlyList<MapRouteView> Routes)
{
    internal static WorldMapView From(IReadOnlyList<RoomDefinition> rooms)
    {
        var byId = rooms.ToDictionary(room => room.Id);
        var routes = new List<MapRouteView>();
        foreach (var room in rooms)
        {
            foreach (var exit in room.Doors.Where(exit => StringComparer.Ordinal.Compare(room.Id, exit.ToRoom) < 0))
            {
                var destination = byId[exit.ToRoom];
                var back = destination.GetDoor(exit.ToDoor ?? throw new InvalidOperationException("A public exit needs a return entrance."));
                var from = room.MapExit(exit);
                var to = destination.MapExit(back);
                routes.Add(new MapRouteView(from.X, from.Z, to.X, to.Z));
            }
        }

        return new WorldMapView(
            [.. rooms.Select(room => new MapRoomView(room.Id, room.Name, room.MapCentre.X, room.MapCentre.Z, room.MapWidth, room.MapDepth))],
            routes.ToArray());
    }
}

[GenerateSerializer, Immutable]
public sealed record MapRoomView(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name,
    [property: Id(2)] double X,
    [property: Id(3)] double Z,
    [property: Id(4)] double Width,
    [property: Id(5)] double Depth);

[GenerateSerializer, Immutable]
public sealed record MapRouteView(
    [property: Id(0)] double FromX,
    [property: Id(1)] double FromZ,
    [property: Id(2)] double ToX,
    [property: Id(3)] double ToZ);

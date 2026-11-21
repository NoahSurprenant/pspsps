using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

/// <summary>The island's outdoor places. A place whose room is not built (or not published) yet is shown but closed.</summary>
[GenerateSerializer, Immutable]
public sealed record WorldMapView([property: Id(0)] IReadOnlyList<MapPlaceView> Places)
{
    internal static WorldMapView From(IReadOnlyList<RoomDefinition> rooms)
    {
        var open = rooms.Select(room => room.Id).ToHashSet();
        return new WorldMapView([.. IslandMap.Places.Select(place => new MapPlaceView(
            place.Id, place.Name, place.Category, place.Landmark, place.X, place.Z, place.Blurb,
            place.Room is { } room && open.Contains(room) ? room : null))]);
    }
}

/// <summary>A place on the island; <c>RoomId</c> is null while it cannot be entered.</summary>
[GenerateSerializer, Immutable]
public sealed record MapPlaceView(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name,
    [property: Id(2)] string Category,
    [property: Id(3)] string Landmark,
    [property: Id(4)] double X,
    [property: Id(5)] double Z,
    [property: Id(6)] string Blurb,
    [property: Id(7)] string? RoomId);

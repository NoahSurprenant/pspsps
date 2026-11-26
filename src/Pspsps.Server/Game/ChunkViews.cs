using Pspsps.Domain;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

/// <summary>The open world's rooms as the browser draws them in place.</summary>
public static class ChunkViews
{
    /// <summary>Every room placed on the world map (indoor rooms aren't on it), at world metres.</summary>
    public static IReadOnlyList<ChunkView> Of(IReadOnlyList<RoomDefinition> rooms)
    {
        ArgumentNullException.ThrowIfNull(rooms);
        var space = new WorldSpace(rooms);
        var byId = rooms.ToDictionary(room => room.Id);
        return
        [
            .. space.Rooms.Select(room =>
            {
                var centre = space.ToWorld(room.Id, new Position(0, 0));
                return new ChunkView(
                    room.Id,
                    room.Name,
                    centre.X,
                    centre.Z,
                    room.Width,
                    room.Depth,
                    room.Pond is { } pond ? new PondView(pond.Center.X, pond.Center.Z, pond.Radius) : null,
                    [.. room.Doors.Select(door => DoorView.From(door, byId[door.ToRoom]))],
                    [.. room.Props.Select(PropView.From)],
                    room.Look,
                    room.Paths.Count == 0 ? null : [.. room.Paths.Select(path => PathOf(room, path))],
                    room.Terraces.Count == 0 ? null : [.. room.Terraces.Select(t => new TerraceView(t.X, t.Z, t.Width, t.Depth, t.Height, t.Colour))]);
            }),
        ];
    }

    private static PathView PathOf(RoomDefinition room, RoomPath path)
    {
        var (x, z, width, depth) = path.Floor(0, 0, room.Width, room.Depth);
        return new PathView(x, z, width, depth);
    }
}

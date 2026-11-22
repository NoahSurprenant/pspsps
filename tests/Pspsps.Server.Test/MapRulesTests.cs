using Pspsps.Application.Rooms;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Test;

/// <summary>The rules that tie the rooms' doors to the island map, so the world is laid out the way the map says.</summary>
public sealed class MapRulesTests
{
    /// <summary>The widest a door's wall may turn from the direction of the place it leads to.</summary>
    private const double WidestTurn = 60;

    /// <summary>The farthest apart, in map metres, two places joined by a door may be.</summary>
    private const double FarthestNeighbour = 32;

    /// <summary>Doors that break the rules until their rooms are rebuilt; each leaves this list when it is fixed.</summary>
    private static readonly string[] KnownBreaks =
    [
        "garden/pear-orchard",
        "pear-orchard/garden",
        "pear-orchard/riverside",
        "pear-orchard/mine",
        "riverside/pear-orchard",
        "riverside/dojo-courtyard",
        "dojo-courtyard/riverside",
        "mine/pear-orchard",
    ];

    [Test]
    public void EveryDoorBetweenTwoPlacesOnTheMap_FacesTheOtherPlace_AndLeadsToANeighbour()
    {
        var layouts = RoomCatalog.All.ToDictionary(room => room.Id, RoomLayout.Of);
        foreach (var room in ContentFile.Embedded().RoomList)
        {
            layouts[room.Id] = room.Layout;
        }

        var onMap = IslandMap.Places.Where(p => p.Room is not null).ToDictionary(p => p.Room!);
        var breaks = new List<string>();
        foreach (var (id, layout) in layouts.Where(l => onMap.ContainsKey(l.Key)))
        {
            foreach (var door in layout.Doors.Where(d => onMap.ContainsKey(d.ToRoom)))
            {
                var from = onMap[id];
                var to = onMap[door.ToRoom];
                var (dx, dz) = (to.X - from.X, to.Z - from.Z);
                var distance = Math.Sqrt((dx * dx) + (dz * dz));
                var (ex, ez) = door.Edge switch
                {
                    RoomEdge.North => (0.0, 1.0),
                    RoomEdge.East => (1.0, 0.0),
                    RoomEdge.South => (0.0, -1.0),
                    _ => (-1.0, 0.0),
                };
                var turn = Math.Acos(((dx * ex) + (dz * ez)) / distance) * 180 / Math.PI;
                if (turn > WidestTurn || distance > FarthestNeighbour)
                {
                    breaks.Add($"{id}/{door.ToRoom}");
                }
            }
        }

        Assert.That(breaks, Is.EquivalentTo(KnownBreaks), "a door breaks the map rules, or a known break is fixed: take it off KnownBreaks");
    }
}

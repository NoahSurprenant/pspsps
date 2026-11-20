using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pspsps.Domain.Rooms;

/// <summary>The rooms a new database starts with, read from StartingRooms.json (the first is where cats arrive). Once seeded, the database is the truth.</summary>
public static class RoomCatalog
{
    /// <summary>The shop's ID: the room whose catalogue stand sells everything (<see cref="RoomDefinition.HasCatalogue"/>).</summary>
    public const string ShopId = "shop";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static IReadOnlyList<RoomDefinition> All { get; } = Load();

    public static RoomDefinition Default => All[0];

    public static RoomDefinition? Find(string id) => All.FirstOrDefault(r => r.Id == id);

    private static RoomDefinition[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("StartingRooms.json")
            ?? throw new InvalidOperationException("StartingRooms.json is not embedded.");
        var rooms = JsonSerializer.Deserialize<List<Entry>>(stream, Json) ?? [];
        return [.. RoomPlan.Join([.. rooms.Select(room => room.Layout.ToDefinition(room.Id))])];
    }

    private sealed record Entry(string Id, RoomLayout Layout);
}

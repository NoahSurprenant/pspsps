using System.Reflection;
using System.Text.Json;

namespace Pspsps.Domain.Rooms;

/// <summary>A landmark on the island map. A place with no room yet is shown but cannot be entered.</summary>
public sealed record MapPlace(string Id, string Name, string Category, string Landmark, double X, double Z, string? Room, string Blurb);

/// <summary>The island the cats live on, read from IslandMap.json: the outdoor places only, never a building's inside.</summary>
public static class IslandMap
{
    public static readonly IReadOnlyList<string> Categories = ["places", "games", "shops", "pets"];

    /// <summary>Metres from the island's centre to its shore, east-west and north-south.</summary>
    public const double HalfWidth = 46;
    public const double HalfDepth = 42;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<MapPlace> Places { get; } = Load();

    private static MapPlace[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("IslandMap.json")
            ?? throw new InvalidOperationException("IslandMap.json is not embedded.");
        return JsonSerializer.Deserialize<MapPlace[]>(stream, Json) ?? [];
    }
}

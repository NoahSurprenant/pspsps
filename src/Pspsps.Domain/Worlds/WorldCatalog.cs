namespace Pspsps.Domain.Worlds;

public static class WorldCatalog
{
    /// <summary>The admin-only world that plays each room's newest draft, published or not.</summary>
    public const string PreviewId = "preview";

    /// <summary>The worlds every player can join.</summary>
    public static IReadOnlyList<WorldDefinition> All { get; } =
    [
        new("tabby", "Tabby", Capacity: 300),
        new("calico", "Calico", Capacity: 300),
        new("siamese", "Siamese", Capacity: 300),
    ];

    public static WorldDefinition Preview { get; } = new(PreviewId, "Preview", Capacity: 30, AdminOnly: true);

    /// <summary>The world, an admin-only one included.</summary>
    public static WorldDefinition? Find(string id) => id == PreviewId ? Preview : All.FirstOrDefault(w => w.Id == id);
}

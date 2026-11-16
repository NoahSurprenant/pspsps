namespace Pspsps.Domain.Worlds;

public static class WorldCatalog
{
    public static IReadOnlyList<WorldDefinition> All { get; } =
    [
        new("tabby", "Tabby", Capacity: 300),
        new("calico", "Calico", Capacity: 300),
        new("siamese", "Siamese", Capacity: 300),
    ];

    public static WorldDefinition? Find(string id) => All.FirstOrDefault(w => w.Id == id);
}

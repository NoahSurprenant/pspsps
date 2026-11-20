using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

/// <summary>Puts the looks a new database starts with (RoomLooks.json) in it, once: after that the database is the truth.</summary>
public static class RoomLookSeeder
{
    /// <summary>The look of every player's home, which is not a room of the book.</summary>
    public const string HomeId = "home";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seed(RoomSeed[] Rooms, VariantSeed[] Variants);

    private sealed record RoomSeed(string Id, RoomLook Look);

    private sealed record VariantSeed(string Id, string Sky, string WallColour);

    private static Seed Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("RoomLooks.json")
            ?? throw new InvalidOperationException("RoomLooks.json is not in the assembly.");
        return JsonSerializer.Deserialize<Seed>(stream, Json) ?? new Seed([], []);
    }

    public static IReadOnlyList<(string Id, RoomLook Look)> StartingLooks() => [.. Load().Rooms.Select(room => (room.Id, room.Look))];

    public static IReadOnlyList<(string Name, LookVariant Variant)> StartingVariants() =>
        [.. Load().Variants.Select(variant => (variant.Id, new LookVariant(variant.Sky, variant.WallColour)))];

    public static async Task<bool> SeedAsync(IApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        if (await db.RoomLooks.AnyAsync(cancellationToken))
        {
            return false;
        }

        var now = time.GetUtcNow();
        foreach (var (id, look) in StartingLooks())
        {
            db.RoomLooks.Add(RoomLookEntry.Of(id, look, now));
        }

        foreach (var (name, variant) in StartingVariants())
        {
            db.LookVariants.Add(LookVariantEntry.Of(name, variant, now));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException exception) when (db.IsUniqueViolation(exception))
        {
            return false;
        }
    }
}

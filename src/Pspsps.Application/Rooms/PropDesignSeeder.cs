using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Rooms;

/// <summary>Puts the prop designs a new database starts with (PropDesigns.json) in it, once: after that the database is the truth.</summary>
public static class PropDesignSeeder
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private sealed record Seed(string Kind, double Size, PropPart[] Parts);

    /// <summary>The designs the server starts with.</summary>
    public static IReadOnlyList<(string Kind, double Size, IReadOnlyList<PropPart> Parts)> Starting()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PropDesigns.json")
            ?? throw new InvalidOperationException("PropDesigns.json is not in the assembly.");
        var seeds = JsonSerializer.Deserialize<Seed[]>(stream, Json) ?? [];
        return [.. seeds.Select(seed => (seed.Kind, seed.Size, (IReadOnlyList<PropPart>)seed.Parts))];
    }

    public static async Task<bool> SeedAsync(IApplicationDbContext db, TimeProvider time, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(time);
        if (await db.PropDesigns.AnyAsync(cancellationToken))
        {
            return false;
        }

        var now = time.GetUtcNow();
        foreach (var (kind, size, parts) in Starting())
        {
            db.PropDesigns.Add(PropDesign.Of(kind, size, parts, now));
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

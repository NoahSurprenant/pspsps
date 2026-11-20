using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;

namespace Pspsps.Server.Game;

internal static class RoomLookEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>How each room is painted and what is round it, the colours of the overlays' looks, and the admins' painter and wildlife recipes, for the browser to draw rooms from; read on every page load.</summary>
    public static void MapRoomLooks(this WebApplication app) =>
        app.MapGet("/api/looks", async (IApplicationDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var rooms = await db.RoomLooks.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Look }).ToListAsync(ct);
            var variants = await db.LookVariants.AsNoTracking().OrderBy(v => v.Id).Select(v => new { v.Id, v.Sky, v.WallColour }).ToListAsync(ct);
            var painters = await db.PainterRecipes.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Surface, r.Steps }).ToListAsync(ct);
            var wildlife = await db.WildlifeRecipes.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Parts, r.Motion }).ToListAsync(ct);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Json(new { rooms, variants, painters, wildlife }, Json);
        });
}

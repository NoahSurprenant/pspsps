using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;

namespace Pspsps.Server.Game;

internal static class PropDesignEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>What each kind of prop looks like, for the browser to draw rooms from; read on every page load, so a new design needs no deploy.</summary>
    public static void MapPropDesigns(this WebApplication app) =>
        app.MapGet("/api/props", async (IApplicationDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var designs = await db.PropDesigns.AsNoTracking().OrderBy(d => d.Kind)
                .Select(d => new { d.Kind, d.Size, d.Parts, d.Growth }).ToListAsync(ct);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Json(designs, Json);
        });
}

using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Moderation;
using Pspsps.Domain.Rooms;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Game;

internal static class RoomLookEndpoints
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>How each room is painted and what is round it, the colours of the overlays' looks, and the admins' painter and wildlife recipes, for the browser to draw rooms from; read on every page load.</summary>
    public static void MapRoomLooks(this WebApplication app) =>
        app.MapGet("/api/looks", async (IApplicationDbContext db, IModerationService moderation, ClaimsPrincipal user, HttpContext context, bool? preview, CancellationToken ct) =>
        {
            // The admins' preview world draws each room in the look its draft brings (a room without one keeps the live look).
            if (preview == true && (user.GetPlayerId() is not { } adminId || !moderation.IsAdmin(adminId)))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var rooms = await db.RoomLooks.AsNoTracking().OrderBy(l => l.Id).Select(l => new { l.Id, l.Look }).ToListAsync(ct);
            if (preview == true)
            {
                var drafted = await db.RoomVersions.AsNoTracking()
                    .Where(v => v.State == RoomVersionState.Draft && v.Look != null)
                    .ToDictionaryAsync(v => v.RoomId, v => v.Look!, ct);
                var known = rooms.Select(l => l.Id).ToHashSet();
                // A room that is only a draft has no live look: it comes in with its draft's, or its neighbours would draw it plain.
                rooms = [.. rooms.Select(l => drafted.TryGetValue(l.Id, out var look) ? new { l.Id, Look = look } : l),
                    .. drafted.Where(d => !known.Contains(d.Key)).OrderBy(d => d.Key).Select(d => new { Id = d.Key, Look = d.Value })];
            }

            var variants = (await db.LookVariants.AsNoTracking().OrderBy(v => v.Id).ToListAsync(ct)).Select(v => new { v.Id, v.Sky, v.WallColour, v.Floor }).ToList();
            var painters = await db.PainterRecipes.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Surface, r.Steps }).ToListAsync(ct);
            var wildlife = await db.WildlifeRecipes.AsNoTracking().OrderBy(r => r.Id).Select(r => new { r.Id, r.Parts, r.Motion }).ToListAsync(ct);
            context.Response.Headers.CacheControl = "no-cache";
            return Results.Json(new { rooms, variants, painters, wildlife }, Json);
        });
}

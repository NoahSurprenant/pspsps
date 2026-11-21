using System.Security.Claims;
using Pspsps.Application.Moderation;
using Pspsps.Application.Rooms;
using Pspsps.Domain;
using Pspsps.Domain.Rooms;
using Pspsps.Server.Auth;

namespace Pspsps.Server.Moderation;

/// <summary>What the admin page does to rooms (<c>/api/admin/rooms</c>): admins only, and changes only from the admin page itself.</summary>
public static class RoomEditorEndpoints
{
    internal static readonly System.Text.Json.JsonSerializerOptions Export = new(System.Text.Json.JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapRoomEditor(this RouteGroupBuilder admin)
    {
        var rooms = admin.MapGroup("/rooms");

        rooms.MapGet("", (ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Admin(user, moderation, async _ => Results.Ok(await editor.ListAsync(ct))));

        admin.MapGet("/export", (ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Admin(user, moderation, async _ => Results.Json(await editor.ExportAsync(ct), Export)));

        admin.MapPost("/import", (HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId => Results.Ok(await editor.ImportAsync(ContentFile.Embedded(), adminId, CancellationToken.None))));

        rooms.MapGet("/audit", (int? limit, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Admin(user, moderation, async _ => Results.Ok(await editor.AuditAsync(limit ?? 100, ct))));

        rooms.MapGet("/{roomId}", (string roomId, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Admin(user, moderation, async _ => await editor.GetAsync(roomId, ct) is { } detail ? Results.Ok(detail) : Results.NotFound()));

        rooms.MapPut("/{roomId}/draft", (string roomId, RoomLayout layout, int? revision, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
                Results.Ok(new { problems = await editor.SaveDraftAsync(roomId, layout, adminId, revision, CancellationToken.None) })));

        rooms.MapDelete("/{roomId}/draft", (string roomId, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.DiscardDraftAsync(roomId, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapPost("/{roomId}/publish", (string roomId, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "with")] string[]? together, DateTimeOffset? at, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                if (at is null)
                {
                    await editor.PublishAsync(roomId, together ?? [], adminId, CancellationToken.None);
                }
                else
                {
                    await editor.ScheduleAsync(roomId, together ?? [], at.Value, adminId, CancellationToken.None);
                }

                return Results.NoContent();
            }));

        rooms.MapDelete("/{roomId}/schedule", (string roomId, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.UnscheduleAsync(roomId, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapPost("/{roomId}/rollback/{number:int}", (string roomId, int number, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.RollbackAsync(roomId, number, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapPut("/{roomId}/overlays/{overlayId}", (string roomId, string overlayId, RoomOverlay overlay, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.SaveOverlayAsync(overlay with { RoomId = roomId, Id = overlayId }, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapDelete("/{roomId}/overlays/{overlayId}", (string roomId, string overlayId, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.DeleteOverlayAsync(roomId, overlayId, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        var looks = admin.MapGroup("/looks");

        looks.MapGet("/options", (ClaimsPrincipal user, IModerationService moderation) =>
            Admin(user, moderation, _ => Task.FromResult(Results.Ok(new
            {
                propKinds = PropKinds.All,
                floors = RoomLook.Floors,
                walls = RoomLook.Walls,
                grounds = RoomLook.Grounds,
                generators = RoomLook.Generators,
                wildlife = RoomLook.WildlifeKinds,
            }))));

        looks.MapPut("/rooms/{id}", (string id, RoomLook look, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.SaveLookAsync(id, look, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        looks.MapPut("/variants/{name}", (string name, LookVariant variant, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.SaveVariantAsync(name, variant, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        looks.MapPut("/painters/{name}", (string name, PainterRecipeBody body, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.SavePainterAsync(name, body.Surface, body.Steps ?? [], adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        looks.MapDelete("/painters/{name}", (string name, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.DeletePainterAsync(name, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        looks.MapPut("/wildlife/{name}", (string name, WildlifeRecipeBody body, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.SaveWildlifeAsync(name, body.Parts ?? [], body.Motion!, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        looks.MapDelete("/wildlife/{name}", (string name, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.DeleteWildlifeAsync(name, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        looks.MapDelete("/variants/{name}", (string name, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.DeleteVariantAsync(name, adminId, CancellationToken.None);
                return Results.NoContent();
            }));
    }

    private sealed record PainterRecipeBody(string Surface, IReadOnlyList<PaintStep>? Steps);

    private sealed record WildlifeRecipeBody(IReadOnlyList<PropPart>? Parts, WildlifeMotion? Motion);

    private static async Task<IResult> Admin(ClaimsPrincipal user, IModerationService moderation, Func<Guid, Task<IResult>> act) =>
        user.GetPlayerId() is { } adminId && moderation.IsAdmin(adminId)
            ? await act(adminId)
            : Results.StatusCode(StatusCodes.Status403Forbidden);

    private static async Task<IResult> Change(
        HttpRequest request, ClaimsPrincipal user, IModerationService moderation, Func<Guid, Task<IResult>> act)
    {
        if (!SameOriginRequest.IsFromThisSite(request))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            return await Admin(user, moderation, act);
        }
        catch (DomainException e)
        {
            return Results.BadRequest(new { message = e.Message });
        }
    }
}

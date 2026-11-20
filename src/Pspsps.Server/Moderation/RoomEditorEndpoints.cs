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
    public static void MapRoomEditor(this RouteGroupBuilder admin)
    {
        var rooms = admin.MapGroup("/rooms");

        rooms.MapGet("", (ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Admin(user, moderation, async _ => Results.Ok(await editor.ListAsync(ct))));

        rooms.MapGet("/{roomId}", (string roomId, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Admin(user, moderation, async _ => await editor.GetAsync(roomId, ct) is { } detail ? Results.Ok(detail) : Results.NotFound()));

        rooms.MapPut("/{roomId}/draft", (string roomId, RoomLayout layout, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
                Results.Ok(new { problems = await editor.SaveDraftAsync(roomId, layout, adminId, CancellationToken.None) })));

        rooms.MapDelete("/{roomId}/draft", (string roomId, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async _ =>
            {
                await editor.DiscardDraftAsync(roomId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapPost("/{roomId}/publish", (string roomId, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async _ =>
            {
                await editor.PublishAsync(roomId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapPost("/{roomId}/rollback/{number:int}", (string roomId, int number, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async adminId =>
            {
                await editor.RollbackAsync(roomId, number, adminId, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapPut("/{roomId}/overlays/{overlayId}", (string roomId, string overlayId, RoomOverlay overlay, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async _ =>
            {
                await editor.SaveOverlayAsync(overlay with { RoomId = roomId, Id = overlayId }, CancellationToken.None);
                return Results.NoContent();
            }));

        rooms.MapDelete("/{roomId}/overlays/{overlayId}", (string roomId, string overlayId, HttpRequest request, ClaimsPrincipal user, IModerationService moderation, IRoomEditor editor) =>
            Change(request, user, moderation, async _ =>
            {
                await editor.DeleteOverlayAsync(roomId, overlayId, CancellationToken.None);
                return Results.NoContent();
            }));
    }

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

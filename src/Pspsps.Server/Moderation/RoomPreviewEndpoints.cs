using System.Security.Claims;
using Pspsps.Application.Moderation;
using Pspsps.Application.Rooms;
using Pspsps.Domain;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;

namespace Pspsps.Server.Moderation;

/// <summary>The admin page's preview of a room: what the browser would be sent on joining it, with its draft (and an overlay) in place and no cats.</summary>
public static class RoomPreviewEndpoints
{
    public static void MapRoomPreview(this RouteGroupBuilder admin) =>
        admin.MapGet("/rooms/{roomId}/preview", async (
            string roomId, string? overlay, ClaimsPrincipal user, IModerationService moderation, IRoomPreviewer previewer, TimeProvider time, CancellationToken ct) =>
        {
            if (user.GetPlayerId() is not { } adminId || !moderation.IsAdmin(adminId))
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            try
            {
                return Results.Ok(SnapshotOf(await previewer.PreviewAsync(roomId, overlay, time.GetUtcNow(), ct)));
            }
            catch (DomainException e)
            {
                return Results.BadRequest(new { message = e.Message });
            }
        });

    internal static RoomSnapshot SnapshotOf(RoomPreview preview)
    {
        var room = preview.Room;
        var byId = preview.All.ToDictionary(r => r.Id);
        return new RoomSnapshot(
            "preview",
            room.Id,
            room.Name,
            room.Width,
            room.Depth,
            [],
            [],
            [.. preview.All.Where(r => r.Id != room.Id).Select(r => new RoomLink(r.Id, r.Name))],
            room.Pond is { } pond ? new PondView(pond.Center.X, pond.Center.Z, pond.Radius) : null,
            [.. room.Doors.Select(d => new DoorView(d.Id, d.Spot.X, d.Spot.Z, d.Radius, d.ToRoom, byId[d.ToRoom].Name, d.Edge?.ToString().ToLowerInvariant()))],
            [.. room.Props.Select(PropView.From)],
            WorldMap: WorldMapView.From(preview.All),
            Look: room.Look);
    }
}

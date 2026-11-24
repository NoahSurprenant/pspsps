using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Pspsps.Application.Moderation;
using Pspsps.Application.Rooms;
using Pspsps.Domain;
using Pspsps.Server.Auth;
using Pspsps.Server.Game;

namespace Pspsps.Server.Moderation;

/// <summary>The admin page's preview of a room: what the browser would be sent on joining it, with its draft (and an overlay) in place and no cats.</summary>
public static class RoomPreviewEndpoints
{
    // As the server's other responses are written (names in camelCase, enums as text), so the page reads this like a room it joins.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly JsonSerializerOptions LookJson = new(Json)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static void MapRoomPreview(this RouteGroupBuilder admin)
    {
        admin.MapPost("/rooms/{roomId}/preview", (
            string roomId, string? overlay, UnsavedRoom unsaved, ClaimsPrincipal user, IModerationService moderation, IRoomPreviewer previewer, TimeProvider time, CancellationToken ct) =>
            PreviewAsync(roomId, overlay, unsaved, user, moderation, previewer, time, ct));

        admin.MapGet("/rooms/{roomId}/preview", (
            string roomId, string? overlay, ClaimsPrincipal user, IModerationService moderation, IRoomPreviewer previewer, TimeProvider time, CancellationToken ct) =>
            PreviewAsync(roomId, overlay, null, user, moderation, previewer, time, ct));
    }

    private static async Task<IResult> PreviewAsync(
        string roomId, string? overlay, UnsavedRoom? unsaved, ClaimsPrincipal user, IModerationService moderation, IRoomPreviewer previewer, TimeProvider time, CancellationToken ct)
    {
        if (user.GetPlayerId() is not { } adminId || !moderation.IsAdmin(adminId))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            var preview = await previewer.PreviewAsync(roomId, overlay, time.GetUtcNow(), ct, unsaved);
            var body = JsonSerializer.SerializeToNode(SnapshotOf(preview), Json)!.AsObject();
            if (preview.DraftLook is { } draftLook)
            {
                // The look the draft brings, for the page to draw the room in (the live looks it already has don't include it).
                body["draftLook"] = JsonSerializer.SerializeToNode(draftLook, LookJson);
            }

            return Results.Json(body, Json);
        }
        catch (DomainException e)
        {
            return Results.BadRequest(new { message = e.Message });
        }
    }

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
            Look: room.Look,
            Chunks: room.Place is null ? null : ChunkViews.Of(preview.All));
    }
}

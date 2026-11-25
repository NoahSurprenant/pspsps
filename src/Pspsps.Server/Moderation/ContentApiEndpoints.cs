using System.Security.Cryptography;
using System.Text;
using Pspsps.Application.Moderation;
using Pspsps.Application.Rooms;
using Pspsps.Domain;

namespace Pspsps.Server.Moderation;

/// <summary>
/// The room editor's export, import and publish for automation (<c>/api/content</c>), for a workflow to call with no browser:
/// a bearer token (<c>Content:ApiToken</c>, at least 32 characters) in place of the admin page's sign-in, acting as the admin
/// player in <c>Content:ActAs</c> (who must be on the admin list). Off, as a 404, unless both are set. It carries no cookie, so
/// there is no same-site check to make, and it reaches nothing but these routes.
/// </summary>
public static class ContentApiEndpoints
{
    public static void MapContentApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var api = app.MapGroup("/api/content");

        api.MapGet("/export", (HttpRequest request, IConfiguration config, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Act(request, config, moderation, async _ => Results.Json(await editor.ExportAsync(ct), RoomEditorEndpoints.Export)));

        api.MapGet("/rooms", (HttpRequest request, IConfiguration config, IModerationService moderation, IRoomEditor editor, CancellationToken ct) =>
            Act(request, config, moderation, async _ => Results.Ok(await editor.ListAsync(ct))));

        api.MapGet("/version", (HttpRequest request, IConfiguration config, IModerationService moderation) =>
            Act(request, config, moderation, _ => Task.FromResult(Results.Ok(new { sha256 = ContentFile.EmbeddedSha256() }))));

        api.MapPost("/import", (HttpRequest request, IConfiguration config, IModerationService moderation, IRoomEditor editor) =>
            Act(request, config, moderation, async adminId => Results.Ok(await editor.ImportAsync(ContentFile.Embedded(), adminId, CancellationToken.None))));

        api.MapPost("/rooms/{roomId}/publish", (string roomId, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "with")] string[]? together, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "retire")] string[]? retire, DateTimeOffset? at,
            HttpRequest request, IConfiguration config, IModerationService moderation, IRoomEditor editor) =>
            Act(request, config, moderation, async adminId =>
            {
                if (at is not null && retire is { Length: > 0 })
                {
                    return Results.BadRequest(new { message = "Retiring rooms goes with publishing now, not with a schedule." });
                }

                if (at is null)
                {
                    await editor.PublishAsync(roomId, together ?? [], adminId, CancellationToken.None, retire ?? []);
                }
                else
                {
                    await editor.ScheduleAsync(roomId, together ?? [], at.Value, adminId, CancellationToken.None);
                }

                return Results.NoContent();
            }));

        api.MapPost("/rooms/{roomId}/retire", ([Microsoft.AspNetCore.Mvc.FromRoute] string roomId, [Microsoft.AspNetCore.Mvc.FromQuery(Name = "with")] string[]? together,
            HttpRequest request, IConfiguration config, IModerationService moderation, IRoomEditor editor) =>
            Act(request, config, moderation, async adminId =>
            {
                await editor.RetireAsync([roomId, .. together ?? []], adminId, CancellationToken.None);
                return Results.NoContent();
            }));
    }

    private static async Task<IResult> Act(HttpRequest request, IConfiguration config, IModerationService moderation, Func<Guid, Task<IResult>> act)
    {
        var token = config["Content:ApiToken"];
        if (string.IsNullOrEmpty(token) || token.Length < 32 || !Guid.TryParse(config["Content:ActAs"], out var adminId))
        {
            return Results.NotFound();
        }

        var sent = request.Headers.Authorization.ToString();
        if (!sent.StartsWith("Bearer ", StringComparison.Ordinal) || !Same(sent["Bearer ".Length..], token) || !moderation.IsAdmin(adminId))
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        try
        {
            return await act(adminId);
        }
        catch (DomainException e)
        {
            return Results.BadRequest(new { message = e.Message });
        }
    }

    private static bool Same(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(a)), SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}

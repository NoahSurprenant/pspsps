using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Pspsps.Application.Rooms;
using Pspsps.Domain.Rooms;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

/// <summary>The admin page's room endpoints (<c>/api/admin/rooms</c>, <c>/api/admin/looks</c>) over HTTP: who may call them, and what they do to the rooms.</summary>
public sealed class RoomEditorHttpTests
{
    private static GameFactory Game => PostgresFixture.Game;

    private static PspspsDbContext Db() => new(new DbContextOptionsBuilder<PspspsDbContext>()
        .UseNpgsql(PostgresFixture.ConnectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    private static Uri Api(string path) => new($"/api/admin/{path}", UriKind.Relative);

    private static async Task<HttpClient> AdminClientAsync()
    {
        var (player, cookie) = await Game.SignInGuestAsync();
        Game.Admins.Add(player.Id);
        return ClientFor(cookie);
    }

    private static HttpClient ClientFor(string cookie)
    {
        var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpClient client, HttpMethod method, string path, object? body = null, string site = "same-origin")
    {
        using var request = new HttpRequestMessage(method, Api(path));
        request.Headers.Add("Sec-Fetch-Site", site);
        if (body is not null)
        {
            request.Content = body is JsonNode node ? new StringContent(node.ToJsonString(), System.Text.Encoding.UTF8, "application/json") : JsonContent.Create(body, options: JsonOptions.Web);
        }

        return await client.SendAsync(request);
    }

    private static async Task<JsonNode> JsonOfAsync(HttpResponseMessage response) =>
        JsonNode.Parse(await response.Content.ReadAsStringAsync())!;

    [Test]
    public async Task TheRoomEndpoints_AreForAdminsOnly_AndRefuseRequestsFromOtherSites()
    {
        var (_, cookie) = await Game.SignInGuestAsync();
        using var guest = ClientFor(cookie);
        using var admin = await AdminClientAsync();

        using var list = await SendAsync(guest, HttpMethod.Get, "rooms");
        using var detail = await SendAsync(guest, HttpMethod.Get, "rooms/garden");
        using var draft = await SendAsync(guest, HttpMethod.Put, "rooms/garden/draft", new JsonObject());
        using var publish = await SendAsync(guest, HttpMethod.Post, "rooms/garden/publish");
        using var look = await SendAsync(guest, HttpMethod.Put, "looks/variants/spring", new { sky = "#112233", wallColour = "#445566" });
        using var crossSite = await SendAsync(admin, HttpMethod.Post, "rooms/garden/publish", site: "cross-site");
        using var adminList = await SendAsync(admin, HttpMethod.Get, "rooms");

        Assert.Multiple(() =>
        {
            Assert.That(list.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(detail.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(draft.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(publish.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(look.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(crossSite.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden), "an admin's request from another site is refused too");
            Assert.That(adminList.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task AnAdmin_EditsARoomAsJson_PublishesIt_AndGoesBack()
    {
        using var admin = await AdminClientAsync();
        using var before = await SendAsync(admin, HttpMethod.Get, "rooms/garden");
        var detail = await JsonOfAsync(before);
        var original = detail["published"]!.DeepClone();
        var edited = detail["published"]!.DeepClone();
        edited["name"] = "The Http Garden";
        var firstNumber = detail["versions"]!.AsArray().Single(v => v!["state"]!.GetValue<string>() == "Published")!["number"]!.GetValue<int>();

        using var save = await SendAsync(admin, HttpMethod.Put, "rooms/garden/draft", edited);
        var problems = (await JsonOfAsync(save))["problems"]!.AsArray();
        using var publish = await SendAsync(admin, HttpMethod.Post, "rooms/garden/publish");
        using var after = await SendAsync(admin, HttpMethod.Get, "rooms/garden");
        var published = (await JsonOfAsync(after))["published"]!;

        Assert.Multiple(() =>
        {
            Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(problems, Is.Empty, "the layout as the server sent it is read back whole");
            Assert.That(publish.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(published["name"]!.GetValue<string>(), Is.EqualTo("The Http Garden"));
            Assert.That(JsonNode.DeepEquals(published["doors"], original["doors"]), Is.True, "doors survive the round trip");
            Assert.That(JsonNode.DeepEquals(published["props"], original["props"]), Is.True, "props survive the round trip");
        });

        using var back = await SendAsync(admin, HttpMethod.Post, $"rooms/garden/rollback/{firstNumber}");
        using var gone = await SendAsync(admin, HttpMethod.Post, "rooms/garden/rollback/999");
        using var restored = await SendAsync(admin, HttpMethod.Get, "rooms/garden");

        Assert.Multiple(async () =>
        {
            Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(gone.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That((await JsonOfAsync(restored))["published"]!["name"]!.GetValue<string>(), Is.EqualTo(original["name"]!.GetValue<string>()));
        });
    }

    [Test]
    public async Task AnOverlayOverHttp_IsKept_AndRefusedWhenItNamesALookThatIsNotThere()
    {
        using var admin = await AdminClientAsync();
        var overlay = new JsonObject
        {
            ["name"] = "Spring",
            ["look"] = "no-such-look",
            ["window"] = new JsonObject { ["starts"] = "2026-03-01T00:00:00Z", ["ends"] = "2026-06-01T00:00:00Z", ["yearly"] = true },
        };

        using var unknown = await SendAsync(admin, HttpMethod.Put, "rooms/garden/overlays/spring-http", overlay);
        overlay["look"] = "autumn";
        using var known = await SendAsync(admin, HttpMethod.Put, "rooms/garden/overlays/spring-http", overlay);
        using var read = await SendAsync(admin, HttpMethod.Get, "rooms/garden");
        var overlays = (await JsonOfAsync(read))["overlays"]!.AsArray();
        using var removeLook = await SendAsync(admin, HttpMethod.Delete, "looks/variants/autumn");
        using var deleted = await SendAsync(admin, HttpMethod.Delete, "rooms/garden/overlays/spring-http");

        Assert.Multiple(async () =>
        {
            Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(await unknown.Content.ReadAsStringAsync(), Does.Contain("no-such-look"));
            Assert.That(known.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(overlays.Any(o => o!["id"]!.GetValue<string>() == "spring-http" && o["look"]!.GetValue<string>() == "autumn"), Is.True);
            Assert.That(removeLook.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a look an overlay wears can't be deleted");
            Assert.That(deleted.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task ABrandNewRoom_NeedsALookAndDoorsBothWays_ThenIsPublishedWithTheRoomThatLeadsToIt()
    {
        using var admin = await AdminClientAsync();
        using var gardenRead = await SendAsync(admin, HttpMethod.Get, "rooms/garden");
        var garden = await JsonOfAsync(gardenRead);
        var gardenNumber = garden["versions"]!.AsArray().Single(v => v!["state"]!.GetValue<string>() == "Published")!["number"]!.GetValue<int>();
        var gardenDraft = garden["published"]!.DeepClone();
        gardenDraft["props"] = new JsonArray();
        gardenDraft["doors"]!.AsArray().Add(JsonNode.Parse("""{"id":"loft","edge":"West","offset":0,"toRoom":"loft","toDoor":"garden"}"""));
        var loft = JsonNode.Parse("""
            {"name":"The Loft","width":10,"depth":8,"spawn":{"x":0,"z":0},"capacity":12,"props":[],
             "doors":[{"id":"garden","edge":"East","offset":0,"toRoom":"garden","toDoor":"loft"}]}
            """)!;
        var look = JsonNode.Parse("""
            {"sky":"#112233","floorColour":"#445566","wallColour":"#778899",
             "floor":{"painter":"planks","colour":"#aa8855"},"wall":{"painter":"bricks"},"backdrop":{"parts":[]}}
            """)!;

        try
        {
            using var loftDraft = await SendAsync(admin, HttpMethod.Put, "rooms/loft/draft", loft);
            using var gardenSaved = await SendAsync(admin, HttpMethod.Put, "rooms/garden/draft", gardenDraft);
            using var noLook = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish?with=garden");
            using var badLook = await SendAsync(admin, HttpMethod.Put, "looks/rooms/loft", JsonNode.Parse("""{"sky":"x","floorColour":"#445566","wallColour":"#778899","backdrop":{"parts":[]}}"""));
            using var goodLook = await SendAsync(admin, HttpMethod.Put, "looks/rooms/loft", look);
            using var alone = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish");
            using var together = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish?with=garden");
            using var list = await SendAsync(admin, HttpMethod.Get, "rooms");
            using var served = await admin.GetAsync(new Uri("/api/looks", UriKind.Relative));
            var rooms = (await JsonOfAsync(list)).AsArray();
            var looks = await JsonOfAsync(served);
            var library = Game.Get<IRoomLibrary>();

            Assert.Multiple(async () =>
            {
                Assert.That(loftDraft.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(gardenSaved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(noLook.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(await noLook.Content.ReadAsStringAsync(), Does.Contain("loft: has no look"));
                Assert.That(badLook.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(goodLook.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(alone.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "the loft's door to the garden isn't met until the garden's is published too");
                Assert.That(together.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(rooms.Any(r => r!["roomId"]!.GetValue<string>() == "loft" && r["published"] is not null), Is.True);
                Assert.That(looks["rooms"]!.AsArray().Any(r => r!["id"]!.GetValue<string>() == "loft"), Is.True, "the browser is served the new room's look");
                Assert.That(library.Find("loft")?.Name, Is.EqualTo("The Loft"), "the pod that published knows the room at once");
                Assert.That(library.Find("garden")!.Doors.Any(d => d.Id == "loft"), Is.True);
            });
        }
        finally
        {
            await using var db = Db();
            db.RoomVersions.RemoveRange(db.RoomVersions.Where(v => v.RoomId == "loft"));
            db.RoomLooks.RemoveRange(db.RoomLooks.Where(l => l.Id == "loft"));
            await db.SaveChangesAsync();
            using var discard = await SendAsync(admin, HttpMethod.Delete, "rooms/garden/draft");
            using var back = await SendAsync(admin, HttpMethod.Post, $"rooms/garden/rollback/{gardenNumber}");
            Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
    }
}

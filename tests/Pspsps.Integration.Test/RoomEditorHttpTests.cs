using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Rooms;
using Pspsps.Domain;
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
        using var detail = await SendAsync(guest, HttpMethod.Get, "rooms/shop");
        using var draft = await SendAsync(guest, HttpMethod.Put, "rooms/shop/draft", new JsonObject());
        using var publish = await SendAsync(guest, HttpMethod.Post, "rooms/shop/publish");
        using var look = await SendAsync(guest, HttpMethod.Put, "looks/variants/spring", new { sky = "#112233", wallColour = "#445566" });
        using var crossSite = await SendAsync(admin, HttpMethod.Post, "rooms/shop/publish", site: "cross-site");
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
    public async Task TheEditorsOptions_ListTheNamesARoomMayUse_ForAdminsOnly()
    {
        var (_, cookie) = await Game.SignInGuestAsync();
        using var guest = ClientFor(cookie);
        using var admin = await AdminClientAsync();

        using var refused = await SendAsync(guest, HttpMethod.Get, "looks/options");
        using var allowed = await SendAsync(admin, HttpMethod.Get, "looks/options");
        var options = await JsonOfAsync(allowed);

        Assert.Multiple(() =>
        {
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(options["propKinds"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EquivalentTo(PropKinds.All));
            Assert.That(options["floors"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EquivalentTo(RoomLook.Floors));
            Assert.That(options["wildlife"]!.AsArray().Select(n => n!.GetValue<string>()), Is.EquivalentTo(RoomLook.WildlifeKinds));
        });
    }

    [Test]
    public async Task APreview_IsTheRoomWithItsDraftInPlace_AndNoCats_ForAdminsOnly()
    {
        var (_, cookie) = await Game.SignInGuestAsync();
        using var guest = ClientFor(cookie);
        using var admin = await AdminClientAsync();
        using var opened = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var layout = (await JsonOfAsync(opened))["published"]!.DeepClone();
        layout["name"] = "The Previewed Shop";
        using var saved = await SendAsync(admin, HttpMethod.Put, "rooms/shop/draft", layout);
        using var noLookYet = await SendAsync(admin, HttpMethod.Get, "rooms/shop/preview");
        using var draftLook = await SendAsync(admin, HttpMethod.Put, "looks/rooms/shop", JsonNode.Parse("""{"sky":"#102030","floorColour":"#405060","wallColour":"#708090","backdrop":{"parts":[]}}"""));
        using var withLook = await SendAsync(admin, HttpMethod.Get, "rooms/shop/preview");
        var withoutLookSnapshot = await JsonOfAsync(noLookYet);
        var withLookSnapshot = await JsonOfAsync(withLook);

        using var refused = await SendAsync(guest, HttpMethod.Get, "rooms/shop/preview");
        using var preview = await SendAsync(admin, HttpMethod.Get, "rooms/shop/preview");
        using var noOverlay = await SendAsync(admin, HttpMethod.Get, "rooms/shop/preview?overlay=nope");
        using var noRoom = await SendAsync(admin, HttpMethod.Get, "rooms/attic/preview");
        using var published = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var snapshot = await JsonOfAsync(preview);
        using var discard = await SendAsync(admin, HttpMethod.Delete, "rooms/shop/draft");

        Assert.Multiple(() =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(refused.StatusCode, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That(preview.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(snapshot["roomName"]!.GetValue<string>(), Is.EqualTo("The Previewed Shop"));
            Assert.That(draftLook.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(withoutLookSnapshot["draftLook"], Is.Null, "no look in the draft, so the preview has none to show");
            Assert.That(withLookSnapshot["draftLook"]!["sky"]!.GetValue<string>(), Is.EqualTo("#102030"), "the preview carries the draft's look");
            Assert.That(snapshot["cats"]!.AsArray(), Is.Empty);
            Assert.That(snapshot["doors"]!.AsArray(), Is.Not.Empty);
            Assert.That(snapshot["worldMap"], Is.Not.Null);
            Assert.That(noOverlay.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(noRoom.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(discard.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task SavingOverADraftThatChangedSinceItWasOpened_IsRefused_AndEveryChangeIsRecorded()
    {
        using var admin = await AdminClientAsync();
        using var opened = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var layout = (await JsonOfAsync(opened))["published"]!.DeepClone();
        layout["name"] = "Cafe, first edit";

        using var first = await SendAsync(admin, HttpMethod.Put, "rooms/cafe/draft", layout);
        using var draftOpened = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var revision = (await JsonOfAsync(draftOpened))["draftRevision"]!.GetValue<int>();
        layout["name"] = "Cafe, second edit";
        using var current = await SendAsync(admin, HttpMethod.Put, $"rooms/cafe/draft?revision={revision}", layout);
        layout["name"] = "Cafe, from a stale page";
        using var stale = await SendAsync(admin, HttpMethod.Put, $"rooms/cafe/draft?revision={revision}", layout);
        using var staleBody = await SendAsync(admin, HttpMethod.Put, $"rooms/cafe/draft?revision={revision}", layout);
        var message = (await JsonOfAsync(staleBody))["message"]!.GetValue<string>();
        using var discard = await SendAsync(admin, HttpMethod.Delete, "rooms/cafe/draft");
        using var audit = await SendAsync(admin, HttpMethod.Get, "rooms/audit?limit=20");
        var actions = (await JsonOfAsync(audit)).AsArray().Where(line => line!["target"]!.GetValue<string>() == "cafe").Select(line => line!["action"]!.GetValue<string>()).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(current.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(stale.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(message, Does.Contain("has been changed"));
            Assert.That(discard.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(actions, Does.Contain("draft-saved").And.Contain("draft-discarded"));
        });
    }

    [Test]
    public async Task AnAdmin_EditsARoomAsJson_PublishesIt_AndGoesBack()
    {
        using var admin = await AdminClientAsync();
        using var before = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var detail = await JsonOfAsync(before);
        var original = detail["published"]!.DeepClone();
        var edited = detail["published"]!.DeepClone();
        edited["name"] = "The Http Shop";
        var firstNumber = detail["versions"]!.AsArray().Single(v => v!["state"]!.GetValue<string>() == "Published")!["number"]!.GetValue<int>();

        using var save = await SendAsync(admin, HttpMethod.Put, "rooms/shop/draft", edited);
        var problems = (await JsonOfAsync(save))["problems"]!.AsArray();
        using var publish = await SendAsync(admin, HttpMethod.Post, "rooms/shop/publish");
        using var after = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var published = (await JsonOfAsync(after))["published"]!;

        Assert.Multiple(() =>
        {
            Assert.That(save.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(problems, Is.Empty, "the layout as the server sent it is read back whole");
            Assert.That(publish.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(published["name"]!.GetValue<string>(), Is.EqualTo("The Http Shop"));
            Assert.That(JsonNode.DeepEquals(published["doors"], original["doors"]), Is.True, "doors survive the round trip");
            Assert.That(JsonNode.DeepEquals(published["props"], original["props"]), Is.True, "props survive the round trip");
        });

        using var back = await SendAsync(admin, HttpMethod.Post, $"rooms/shop/rollback/{firstNumber}");
        using var gone = await SendAsync(admin, HttpMethod.Post, "rooms/shop/rollback/999");
        using var restored = await SendAsync(admin, HttpMethod.Get, "rooms/shop");

        await Assert.MultipleAsync(async () =>
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

        using var unknown = await SendAsync(admin, HttpMethod.Put, "rooms/shop/overlays/spring-http", overlay);
        overlay["look"] = "autumn";
        using var known = await SendAsync(admin, HttpMethod.Put, "rooms/shop/overlays/spring-http", overlay);
        using var read = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var overlays = (await JsonOfAsync(read))["overlays"]!.AsArray();
        using var removeLook = await SendAsync(admin, HttpMethod.Delete, "looks/variants/autumn");
        using var deleted = await SendAsync(admin, HttpMethod.Delete, "rooms/shop/overlays/spring-http");

        await Assert.MultipleAsync(async () =>
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
        using var shopRead = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var shop = await JsonOfAsync(shopRead);
        var shopNumber = shop["versions"]!.AsArray().Single(v => v!["state"]!.GetValue<string>() == "Published")!["number"]!.GetValue<int>();
        var shopDraft = shop["published"]!.DeepClone();
        shopDraft["props"] = new JsonArray();
        shopDraft["doors"]!.AsArray().Add(JsonNode.Parse("""{"id":"loft","edge":"West","offset":0,"toRoom":"loft","toDoor":"shop"}"""));
        var loft = JsonNode.Parse("""
            {"name":"The Loft","width":10,"depth":8,"spawn":{"x":0,"z":0},"capacity":12,"props":[],
             "doors":[{"id":"shop","edge":"East","offset":0,"toRoom":"shop","toDoor":"loft"}]}
            """)!;
        var look = JsonNode.Parse("""
            {"sky":"#112233","floorColour":"#445566","wallColour":"#778899",
             "floor":{"painter":"planks","colour":"#aa8855"},"wall":{"painter":"bricks"},"backdrop":{"parts":[]}}
            """)!;

        try
        {
            using var loftDraft = await SendAsync(admin, HttpMethod.Put, "rooms/loft/draft", loft);
            using var shopSaved = await SendAsync(admin, HttpMethod.Put, "rooms/shop/draft", shopDraft);
            using var noLook = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish?with=shop");
            using var badLook = await SendAsync(admin, HttpMethod.Put, "looks/rooms/loft", JsonNode.Parse("""{"sky":"x","floorColour":"#445566","wallColour":"#778899","backdrop":{"parts":[]}}"""));
            using var goodLook = await SendAsync(admin, HttpMethod.Put, "looks/rooms/loft", look);
            using var alone = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish");
            using var together = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish?with=shop");
            using var list = await SendAsync(admin, HttpMethod.Get, "rooms");
            using var served = await admin.GetAsync(new Uri("/api/looks", UriKind.Relative));
            var rooms = (await JsonOfAsync(list)).AsArray();
            var looks = await JsonOfAsync(served);
            var library = Game.Get<IRoomLibrary>();

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(loftDraft.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(shopSaved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(noLook.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(await noLook.Content.ReadAsStringAsync(), Does.Contain("loft: has no look"));
                Assert.That(badLook.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(goodLook.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(alone.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "the loft's door to the shop isn't met until the shop's is published too");
                Assert.That(together.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(rooms.Any(r => r!["roomId"]!.GetValue<string>() == "loft" && r["published"] is not null), Is.True);
                Assert.That(looks["rooms"]!.AsArray().Any(r => r!["id"]!.GetValue<string>() == "loft"), Is.True, "the browser is served the new room's look");
                Assert.That(library.Find("loft")?.Name, Is.EqualTo("The Loft"), "the pod that published knows the room at once");
                Assert.That(library.Find("shop")!.Doors.Any(d => d.Id == "loft"), Is.True);
            });
        }
        finally
        {
            await using var db = Db();
            db.RoomVersions.RemoveRange(await db.RoomVersions.Where(v => v.RoomId == "loft").ToListAsync());
            db.RoomLooks.RemoveRange(await db.RoomLooks.Where(l => l.Id == "loft").ToListAsync());
            await db.SaveChangesAsync();
            using var discard = await SendAsync(admin, HttpMethod.Delete, "rooms/shop/draft");
            using var back = await SendAsync(admin, HttpMethod.Post, $"rooms/shop/rollback/{shopNumber}");
            Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
    }

    [Test]
    public async Task ARoom_CanBeRetired_OnlyWhenNoRoomThatStaysLeadsToIt_AndItsVersionStaysInTheHistory()
    {
        using var admin = await AdminClientAsync();
        using var shopRead = await SendAsync(admin, HttpMethod.Get, "rooms/shop");
        var shop = await JsonOfAsync(shopRead);
        var shopNumber = shop["versions"]!.AsArray().Single(v => v!["state"]!.GetValue<string>() == "Published")!["number"]!.GetValue<int>();
        var withDoor = shop["published"]!.DeepClone();
        withDoor["props"] = new JsonArray();
        withDoor["doors"]!.AsArray().Add(JsonNode.Parse("""{"id":"loft","edge":"West","offset":0,"toRoom":"loft","toDoor":"shop"}"""));
        var withoutDoor = shop["published"]!.DeepClone();
        var loft = JsonNode.Parse("""
            {"name":"The Loft","width":10,"depth":8,"spawn":{"x":0,"z":0},"capacity":12,"props":[],
             "doors":[{"id":"shop","edge":"East","offset":0,"toRoom":"shop","toDoor":"loft"}]}
            """)!;
        var look = JsonNode.Parse("""{"sky":"#112233","floorColour":"#445566","wallColour":"#778899","floor":{"painter":"planks","colour":"#aa8855"},"wall":{"painter":"bricks"},"backdrop":{"parts":[]}}""")!;

        try
        {
            (await SendAsync(admin, HttpMethod.Put, "rooms/loft/draft", loft)).Dispose();
            (await SendAsync(admin, HttpMethod.Put, "rooms/shop/draft", withDoor)).Dispose();
            (await SendAsync(admin, HttpMethod.Put, "looks/rooms/loft", look)).Dispose();
            using var opened = await SendAsync(admin, HttpMethod.Post, "rooms/loft/publish?with=shop");
            Assert.That(opened.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "the loft is live, with a door from the shop");

            using var tooSoon = await SendAsync(admin, HttpMethod.Post, "rooms/loft/retire");
            using var notLive = await SendAsync(admin, HttpMethod.Post, "rooms/no-such-room/retire");
            (await SendAsync(admin, HttpMethod.Put, "rooms/shop/draft", withoutDoor)).Dispose();
            using var scheduled = await SendAsync(admin, HttpMethod.Post, "rooms/shop/publish?retire=loft&at=2099-01-01T00:00:00Z");
            using var together = await SendAsync(admin, HttpMethod.Post, "rooms/shop/publish?retire=loft");
            using var after = await SendAsync(admin, HttpMethod.Get, "rooms/loft");
            using var audit = await SendAsync(admin, HttpMethod.Get, "rooms/audit?limit=20");
            var versions = (await JsonOfAsync(after))["versions"]!.AsArray();
            var library = Game.Get<IRoomLibrary>();

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(tooSoon.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "the shop still has a door to the loft");
                Assert.That(await tooSoon.Content.ReadAsStringAsync(), Does.Contain("loft"));
                Assert.That(notLive.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(await notLive.Content.ReadAsStringAsync(), Does.Contain("not live"));
                Assert.That(scheduled.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a retirement goes with a publish now, not a schedule");
                Assert.That(together.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "the shop's door to the loft goes in the same step");
                Assert.That(versions.Select(v => v!["state"]!.GetValue<string>()), Is.All.EqualTo("Archived"), "the loft's version stays in the history");
                Assert.That(library.Find("loft"), Is.Null, "the pod that retired it no longer has the room");
                Assert.That(library.Find("shop")!.Doors.Any(d => d.ToRoom == "loft"), Is.False);
                Assert.That((await audit.Content.ReadAsStringAsync()), Does.Contain("retired"));
            });
        }
        finally
        {
            await using var db = Db();
            db.RoomVersions.RemoveRange(await db.RoomVersions.Where(v => v.RoomId == "loft").ToListAsync());
            db.RoomLooks.RemoveRange(await db.RoomLooks.Where(l => l.Id == "loft").ToListAsync());
            await db.SaveChangesAsync();
            using var discard = await SendAsync(admin, HttpMethod.Delete, "rooms/shop/draft");
            using var back = await SendAsync(admin, HttpMethod.Post, $"rooms/shop/rollback/{shopNumber}");
            Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        }
    }

    [Test]
    public async Task APainterAndACreatureRecipe_AreServedToTheBrowser_UsedByALook_AndKeptWhileALookUsesThem()
    {
        using var admin = await AdminClientAsync();
        var look = JsonNode.Parse("""{"sky":"#112233","floorColour":"#445566","wallColour":"#778899","floor":{"painter":"recipe-stripes","colour":"#808080"},"wildlife":"recipe-moth","backdrop":{"parts":[]}}""");
        try
        {
            using var badPainter = await SendAsync(admin, HttpMethod.Put, "looks/painters/recipe-stripes", JsonNode.Parse("""{"surface":"floor","steps":[{"op":"splat"}]}"""));
            using var painter = await SendAsync(admin, HttpMethod.Put, "looks/painters/recipe-stripes", JsonNode.Parse("""{"surface":"floor","steps":[{"op":"fill","colour":"main"}]}"""));
            using var moth = await SendAsync(admin, HttpMethod.Put, "looks/wildlife/recipe-moth", JsonNode.Parse("""{"parts":[{"shape":"ball","diameter":0.2,"height":0.05,"at":[0,0,0],"colour":"#d9d2c3"}],"motion":{"kind":"wander","speed":0.6,"height":1.2,"reach":3}}"""));
            using var unknown = await SendAsync(admin, HttpMethod.Put, "looks/rooms/recipe-loft", look);
            using var served = await admin.GetAsync(new Uri("/api/looks", UriKind.Relative));
            using var refusedPainter = await SendAsync(admin, HttpMethod.Delete, "looks/painters/recipe-stripes");
            using var refusedMoth = await SendAsync(admin, HttpMethod.Delete, "looks/wildlife/recipe-moth");
            var looks = await JsonOfAsync(served);

            await Assert.MultipleAsync(async () =>
            {
                Assert.That(badPainter.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(painter.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(moth.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(unknown.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), "a look may name the recipes");
                Assert.That(looks["painters"]!.AsArray().Any(p => p!["id"]!.GetValue<string>() == "recipe-stripes"), Is.True);
                Assert.That(looks["wildlife"]!.AsArray().Any(w => w!["id"]!.GetValue<string>() == "recipe-moth"), Is.True);
                Assert.That(refusedPainter.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "a look still uses it");
                Assert.That(refusedMoth.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
                Assert.That(await refusedPainter.Content.ReadAsStringAsync(), Does.Contain("recipe-loft"));
            });

            await using var db = Db();
            db.RoomLooks.RemoveRange(await db.RoomLooks.Where(l => l.Id == "recipe-loft").ToListAsync());
            await db.SaveChangesAsync();
            using var deletedPainter = await SendAsync(admin, HttpMethod.Delete, "looks/painters/recipe-stripes");
            using var deletedMoth = await SendAsync(admin, HttpMethod.Delete, "looks/wildlife/recipe-moth");

            Assert.Multiple(() =>
            {
                Assert.That(deletedPainter.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
                Assert.That(deletedMoth.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            });
        }
        finally
        {
            await using var db = Db();
            db.RoomLooks.RemoveRange(await db.RoomLooks.Where(l => l.Id == "recipe-loft").ToListAsync());
            db.PainterRecipes.RemoveRange(await db.PainterRecipes.Where(p => p.Id == "recipe-stripes").ToListAsync());
            db.WildlifeRecipes.RemoveRange(await db.WildlifeRecipes.Where(w => w.Id == "recipe-moth").ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Test]

    public async Task ADraftScheduledForLater_GoesLiveWhenItsTimeComes_AndNotBefore()
    {
        using var admin = await AdminClientAsync();
        using var opened = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var detail = await JsonOfAsync(opened);
        var liveNumber = detail["versions"]!.AsArray().First(v => v!["state"]!.GetValue<string>() == "Published")!["number"]!.GetValue<int>();
        var layout = detail["published"]!.DeepClone();
        layout["name"] = "Cafe, scheduled";
        using var saved = await SendAsync(admin, HttpMethod.Put, "rooms/cafe/draft", layout);
        var at = DateTimeOffset.UtcNow.AddHours(1);
        var when = Uri.EscapeDataString(at.ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        using var inThePast = await SendAsync(admin, HttpMethod.Post, $"rooms/cafe/publish?at={Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O", System.Globalization.CultureInfo.InvariantCulture))}");
        using var scheduled = await SendAsync(admin, HttpMethod.Post, $"rooms/cafe/publish?at={when}");
        using var afterScheduling = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var goLiveAt = (await JsonOfAsync(afterScheduling))["goLiveAt"];
        using var taken = await SendAsync(admin, HttpMethod.Delete, "rooms/cafe/schedule");
        using var notScheduled = await SendAsync(admin, HttpMethod.Delete, "rooms/cafe/schedule");
        using var again = await SendAsync(admin, HttpMethod.Post, $"rooms/cafe/publish?at={when}");

        await using var scope = Game.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IApplicationDbContext>();
        var library = scope.ServiceProvider.GetRequiredService<IRoomLibrary>();
        var early = await new RoomEditor(db, library, TimeProvider.System).PublishDueAsync(CancellationToken.None);
        var late = await new RoomEditor(db, library, new FakeTimeProvider(at.AddMinutes(1))).PublishDueAsync(CancellationToken.None);
        using var live = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var published = (await JsonOfAsync(live))["published"]!["name"]!.GetValue<string>();
        using var audit = await SendAsync(admin, HttpMethod.Get, "rooms/audit?limit=50");
        var actions = (await JsonOfAsync(audit)).AsArray().Where(line => line!["target"]!.GetValue<string>() == "cafe").Select(line => line!["action"]!.GetValue<string>()).ToList();
        using var back = await SendAsync(admin, HttpMethod.Post, $"rooms/cafe/rollback/{liveNumber}");

        Assert.Multiple(() =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(inThePast.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(scheduled.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(goLiveAt, Is.Not.Null);
            Assert.That(taken.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(notScheduled.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(early, Is.Zero, "not before its time");
            Assert.That(late, Is.EqualTo(1));
            Assert.That(published, Is.EqualTo("Cafe, scheduled"));
            Assert.That(actions, Does.Contain("publish-scheduled").And.Contain("schedule-removed").And.Contain("published"));
            Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
        });
    }

    [Test]
    public async Task TheExport_IsForAdminsOnly_AndHoldsThePublishedContentStartingRoomFirst()
    {
        var (_, cookie) = await Game.SignInGuestAsync();
        using var guest = ClientFor(cookie);
        using var admin = await AdminClientAsync();

        using var refused = await SendAsync(guest, HttpMethod.Get, "export");
        using var allowed = await SendAsync(admin, HttpMethod.Get, "export");
        var export = await JsonOfAsync(allowed);

        Assert.Multiple(() =>
        {
            Assert.That(refused.StatusCode, Is.Not.EqualTo(HttpStatusCode.OK));
            Assert.That(allowed.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(export["rooms"]![0]!["id"]!.GetValue<string>(), Is.EqualTo(RoomBook.StartingRoom));
            Assert.That(export["rooms"]![0]!["layout"]!["props"]!.AsArray(), Is.Not.Empty);
            Assert.That(export["looks"]!["rooms"]!.AsArray(), Is.Not.Empty);
            Assert.That(export["propDesigns"]!.AsArray(), Is.Not.Empty);
            Assert.That(export["painters"], Is.Not.Null);
            Assert.That(export["wildlife"], Is.Not.Null);
        });
    }

    [Test]
    public async Task TheContentApi_NeedsItsTokenAndAnAdminToActAs_AndReachesNothingElse()
    {
        const string token = "a-content-token-for-the-tests-0123456789";
        var (adminPlayer, cookie) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var config = Game.Get<Microsoft.Extensions.Configuration.IConfiguration>();
        using var client = Game.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });

        async Task<HttpStatusCode> StatusAsync(string path, string? bearer, string? withCookie = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (bearer is not null)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
            }

            if (withCookie is not null)
            {
                request.Headers.Add("Cookie", withCookie);
            }

            using var response = await client.SendAsync(request);
            return response.StatusCode;
        }

        try
        {
            var off = await StatusAsync("/api/content/export", token);
            config["Content:ActAs"] = adminPlayer.Id.ToString();
            var none = await StatusAsync("/api/content/export", null);
            var wrong = await StatusAsync("/api/content/export", "not-the-token-not-the-token-not-the-token");
            var cookieOnly = await StatusAsync("/api/content/export", null, cookie);
            var allowed = await StatusAsync("/api/content/export", token);
            var list = await StatusAsync("/api/content/rooms", token);
            config["Content:ActAs"] = Guid.NewGuid().ToString();
            var notAdmin = await StatusAsync("/api/content/export", token);

            Assert.Multiple(() =>
            {
                Assert.That(off, Is.EqualTo(HttpStatusCode.NotFound), "off until the player to act as is set");
                Assert.That(none, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(wrong, Is.EqualTo(HttpStatusCode.Forbidden));
                Assert.That(cookieOnly, Is.EqualTo(HttpStatusCode.Forbidden), "a signed-in admin's cookie is not the token");
                Assert.That(allowed, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(list, Is.EqualTo(HttpStatusCode.OK));
                Assert.That(notAdmin, Is.EqualTo(HttpStatusCode.Forbidden), "it only acts as an admin");
            });
        }
        finally
        {
            config["Content:ActAs"] = null;
        }
    }

    [Test]
    public async Task LoadingTheContentFile_AddsANewPropDesignAndOverlay_ReplacesALiveOverlay_AndLeavesALiveDesignAlone()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        var design = new DraftPropDesign("import-test-lamp", 0.5, [new PropPart("ball", [0, 0.5, 0], "#ffd54a", Diameter: 0.4)]);
        var overlay = new RoomOverlay("import-test", "shop", "Import test", new OverlayWindow(new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2090, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        var file = new ContentFile(PropDesigns: [design], Overlays: [overlay]);
        try
        {
            var first = await editor.ImportAsync(file, adminPlayer.Id, CancellationToken.None);
            var again = await editor.ImportAsync(file, adminPlayer.Id, CancellationToken.None);
            var changed = await editor.ImportAsync(new ContentFile(PropDesigns: [design with { Size = 0.6 }], Overlays: [overlay with { Name = "Changed" }]), adminPlayer.Id, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(first.Problems, Is.Empty);
                Assert.That(first.Applied, Has.Some.Contains("prop design import-test-lamp").And.Some.Contains("overlay shop/import-test"));
                Assert.That(again.Applied, Is.Empty, "loading it twice changes nothing");
                Assert.That(again.Unchanged, Has.Some.Contains("prop design import-test-lamp").And.Some.Contains("overlay shop/import-test"));
                Assert.That(changed.Applied, Has.Count.EqualTo(1).And.Some.Contains("overlay shop/import-test"), "the file wins for an overlay");
                Assert.That(changed.Problems, Has.Count.EqualTo(1).And.Some.Contains("import-test-lamp"), "a live design is not changed from the file");
            });
            await using var db = Db();
            Assert.That((await db.RoomOverlays.AsNoTracking().SingleAsync(o => o.RoomId == "shop" && o.Id == "import-test")).ToOverlay().Name, Is.EqualTo("Changed"));
        }
        finally
        {
            await editor.DeleteOverlayAsync("shop", "import-test", adminPlayer.Id, CancellationToken.None);
        }
    }

    [Test]
    public async Task LoadingTheContentFile_ReplacesALiveVariantThatDiffers()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        var old = new DraftVariant("import-test-swap", "#cfe3f0", "#e8eef4", new LookPaint("tiles", "#d9f0fa", "#bfe2f2"));
        try
        {
            await editor.ImportAsync(new ContentFile(Looks: new DraftLooks(Variants: [old])), adminPlayer.Id, CancellationToken.None);
            var swapped = await editor.ImportAsync(new ContentFile(Looks: new DraftLooks(Variants: [old with { Sky = "#112233" }])), adminPlayer.Id, CancellationToken.None);
            await using var db = Db();

            Assert.Multiple(() =>
            {
                Assert.That(swapped.Problems, Is.Empty);
                Assert.That(swapped.Applied, Has.Some.Contains("variant import-test-swap"));
            });
            Assert.That((await db.LookVariants.AsNoTracking().SingleAsync(v => v.Id == "import-test-swap")).Sky, Is.EqualTo("#112233"));
        }
        finally
        {
            await using var db = Db();
            db.LookVariants.RemoveRange(await db.LookVariants.Where(v => v.Id.StartsWith("import-test-")).ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public async Task ALookVariant_CanCarryAFloorOfItsOwn_ThatMustNameAFloorPainter()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        var good = new DraftVariant("import-test-frozen", "#cfe3f0", "#e8eef4", new LookPaint("tiles", "#d9f0fa", "#bfe2f2"));
        var bad = new DraftVariant("import-test-marble", "#cfe3f0", "#e8eef4", new LookPaint("marble"));
        try
        {
            var first = await editor.ImportAsync(new ContentFile(Looks: new DraftLooks(Variants: [good, bad])), adminPlayer.Id, CancellationToken.None);
            var again = await editor.ImportAsync(new ContentFile(Looks: new DraftLooks(Variants: [good])), adminPlayer.Id, CancellationToken.None);
            await using var db = Db();
            var kept = await db.LookVariants.AsNoTracking().SingleAsync(v => v.Id == "import-test-frozen");

            Assert.Multiple(() =>
            {
                Assert.That(first.Applied, Has.Some.Contains("variant import-test-frozen"));
                Assert.That(first.Problems, Has.Count.EqualTo(1).And.Some.Contains("marble"));
                Assert.That(again.Unchanged, Has.Some.Contains("variant import-test-frozen"), "the floor is part of what is compared");
                Assert.That(kept.Floor, Is.EqualTo(new LookPaint("tiles", "#d9f0fa", "#bfe2f2")));
            });
        }
        finally
        {
            await using var db = Db();
            db.LookVariants.RemoveRange(await db.LookVariants.Where(v => v.Id.StartsWith("import-test-")).ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public async Task APainter_ThatAVariantsFloorUses_IsNotDeletedOrChangedToAWall()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        var steps = new[] { new PaintStep("fill", Colour: "main") };
        try
        {
            await editor.SavePainterAsync("import-test-ice", PaintSurfaces.Floor, steps, adminPlayer.Id, CancellationToken.None);
            await editor.SaveVariantAsync("import-test-frozen", new LookVariant("#cfe3f0", "#e8eef4", new LookPaint("import-test-ice")), adminPlayer.Id, CancellationToken.None);

            var deleted = Assert.ThrowsAsync<Pspsps.Domain.DomainException>(() => editor.DeletePainterAsync("import-test-ice", adminPlayer.Id, CancellationToken.None));
            var changed = Assert.ThrowsAsync<Pspsps.Domain.DomainException>(() => editor.SavePainterAsync("import-test-ice", PaintSurfaces.Wall, steps, adminPlayer.Id, CancellationToken.None));
            await editor.DeleteVariantAsync("import-test-frozen", adminPlayer.Id, CancellationToken.None);
            await editor.DeletePainterAsync("import-test-ice", adminPlayer.Id, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(deleted!.Message, Does.Contain("variant import-test-frozen"));
                Assert.That(changed!.Message, Does.Contain("variant import-test-frozen"));
            });
        }
        finally
        {
            await using var db = Db();
            db.LookVariants.RemoveRange(await db.LookVariants.Where(v => v.Id.StartsWith("import-test-")).ToListAsync());
            db.PainterRecipes.RemoveRange(await db.PainterRecipes.Where(r => r.Id.StartsWith("import-test-")).ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public async Task AnOverlay_IsSavedForARoomThatIsOnlyADraft_WithoutBreakingOtherRooms_AndAnUnknownRoomIsAMistake()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var editor = Game.Get<IRoomEditor>();
        var window = new OverlayWindow(new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2090, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var layout = (await editor.GetAsync("cafe", CancellationToken.None))!.Published!;
        try
        {
            await editor.SaveDraftAsync("import-test-drafted", layout, adminPlayer.Id, null, CancellationToken.None);
            var first = await editor.ImportAsync(new ContentFile(Overlays: [new RoomOverlay("wait", "import-test-drafted", "Waits", window)]), adminPlayer.Id, CancellationToken.None);
            var second = await editor.ImportAsync(new ContentFile(Overlays: [new RoomOverlay("wait", "import-test-nowhere", "Nowhere", window)]), adminPlayer.Id, CancellationToken.None);
            var other = await editor.SaveDraftAsync("cafe", layout, adminPlayer.Id, null, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(first.Applied, Has.Some.Contains("overlay import-test-drafted/wait"), "no player can see the room yet, so its overlay is saved now");
                Assert.That(first.Problems, Is.Empty);
                Assert.That(second.Problems, Has.Some.Contains("import-test-nowhere/wait"), "a room that is neither published nor a draft is a mistake");
                Assert.That(other, Has.None.Contains("import-test-drafted"), "the overlay of a draft room does not make the checks of other rooms complain");
            });
        }
        finally
        {
            await editor.DeleteOverlayAsync("import-test-drafted", "wait", adminPlayer.Id, CancellationToken.None);
            await editor.DiscardDraftAsync("import-test-drafted", adminPlayer.Id, CancellationToken.None);
            await editor.DiscardDraftAsync("cafe", adminPlayer.Id, CancellationToken.None);
        }
    }

    [Test]
    public async Task AnOverlayInTheFile_OfARoomThatIsNotPublished_IsSavedNotWaiting()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var editor = Game.Get<IRoomEditor>();
        try
        {
            await editor.ImportAsync(ContentFile.Embedded(), adminPlayer.Id, CancellationToken.None);
            var rink = await editor.GetAsync("ice-rink", CancellationToken.None);
            var cafe = await editor.GetAsync("cafe", CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(rink!.Overlays.Select(o => o.Id), Does.Contain("winter"));
                Assert.That(cafe!.Overlays, Is.Empty);
            });
        }
        finally
        {
            await editor.DeleteOverlayAsync("ice-rink", "winter", adminPlayer.Id, CancellationToken.None);
            foreach (var room in ContentFile.Embedded().RoomList)
            {
                await editor.DiscardDraftAsync(room.Id, adminPlayer.Id, CancellationToken.None);
            }
        }
    }

    private static string Json(object? value) => JsonSerializer.Serialize(value);

    [Test]
    public async Task ARoomsLook_IsKeptInItsDraft_GoesLiveOnPublish_AndIsExportedWithTheRoom()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var editor = Game.Get<IRoomEditor>();
        var before = await editor.GetAsync("cafe", CancellationToken.None);
        var original = (await Db().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "cafe")).Look;
        var number = before!.Versions.Single(v => v.State == RoomVersionState.Published).Number;
        var look = new RoomLook("#102030", "#405060", "#708090", new LookBackdrop([]));
        try
        {
            await editor.SaveDraftAsync("cafe", before.Published!, adminPlayer.Id, null, CancellationToken.None, look);
            var drafted = await editor.GetAsync("cafe", CancellationToken.None);
            var liveBefore = await Db().RoomLooks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == "cafe");

            await editor.PublishAsync("cafe", [], adminPlayer.Id, CancellationToken.None);
            var published = await editor.GetAsync("cafe", CancellationToken.None);
            var liveAfter = await Db().RoomLooks.AsNoTracking().SingleOrDefaultAsync(l => l.Id == "cafe");
            var exported = (await editor.ExportAsync(CancellationToken.None)).Rooms.Single(r => r.Id == "cafe");

            Assert.Multiple(() =>
            {
                Assert.That(Json(drafted!.DraftLook), Is.EqualTo(Json(look)), "the look sits in the draft");
                Assert.That(liveBefore?.Look, Is.Not.EqualTo(look), "and is not live yet");
                Assert.That(published!.DraftLook, Is.Null);
                Assert.That(Json(liveAfter?.Look), Is.EqualTo(Json(look)), "publishing puts it live");
                Assert.That(Json(exported.Look), Is.EqualTo(Json(look)), "an export carries the room's look with it");
            });

            await editor.RollbackAsync("cafe", number, adminPlayer.Id, CancellationToken.None);
            var rolledBack = await Db().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "cafe");

            Assert.That(Json(rolledBack.Look), Is.EqualTo(Json(original)), "going back to a version brings back the look it went live with");
        }
        finally
        {
            if ((await editor.GetAsync("cafe", CancellationToken.None))?.Draft is not null)
            {
                await editor.DiscardDraftAsync("cafe", adminPlayer.Id, CancellationToken.None);
            }

            await editor.RollbackAsync("cafe", number, adminPlayer.Id, CancellationToken.None);
            await using var db = Db();
            (await db.RoomLooks.SingleAsync(l => l.Id == "cafe")).Replace(original, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
    }

    private static async Task WithCafeAsync(Func<IRoomEditor, Guid, RoomDetail, Task> test)
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var editor = Game.Get<IRoomEditor>();
        var before = (await editor.GetAsync("cafe", CancellationToken.None))!;
        try
        {
            await test(editor, adminPlayer.Id, before);
        }
        finally
        {
            if ((await editor.GetAsync("cafe", CancellationToken.None))?.Draft is not null)
            {
                await editor.DiscardDraftAsync("cafe", adminPlayer.Id, CancellationToken.None);
            }
        }
    }

    [Test]
    public Task SavingTheLookOfAScheduledDraft_TakesTheScheduleOff_AndTheAuditSaysSo() => WithCafeAsync(async (editor, admin, before) =>
    {
        var look = new RoomLook("#102030", "#405060", "#708090", new LookBackdrop([]));
        await editor.SaveDraftAsync("cafe", before.Published!, admin, null, CancellationToken.None);
        await editor.ScheduleAsync("cafe", [], DateTimeOffset.UtcNow.AddDays(30), admin, CancellationToken.None);
        await editor.SaveLookAsync("cafe", look, admin, CancellationToken.None);
        var after = await editor.GetAsync("cafe", CancellationToken.None);
        var audit = await editor.AuditAsync(20, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(after!.GoLiveAt, Is.Null);
            Assert.That(audit, Has.Some.Matches<AuditLine>(a => a.Action == "draft-look-saved" && a.Target == "cafe" && a.Detail.Contains("schedule was taken off")));
        });
    });

    [Test]
    public Task ANewOverlayInTheFile_OfARoomTheFileAlsoChanges_IsCheckedAgainstTheDraft_NotTheLiveRoom() => WithCafeAsync(async (editor, admin, before) =>
    {
        var gone = before.Published!.Props[^1];
        var draft = before.Published with { Props = [.. before.Published.Props.Take(before.Published.Props.Count - 1)] };
        var overlay = new RoomOverlay("import-test-new", "cafe", "Takes its place", new OverlayWindow(new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2090, 1, 2, 0, 0, 0, TimeSpan.Zero))) { Adds = [gone] };
        try
        {
            var report = await editor.ImportAsync(new ContentFile(Rooms: [new DraftRoom("cafe", draft)], Overlays: [overlay]), admin, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(report.Problems, Is.Empty);
                Assert.That(report.Applied, Has.Some.Contains("overlay cafe/import-test-new"));
            });
        }
        finally
        {
            await editor.DeleteOverlayAsync("cafe", "import-test-new", admin, CancellationToken.None);
        }
    });

    [Test]
    public Task ANewOverlayInTheFile_OfARoomTheFileLeavesAsItIsLive_IsCheckedAgainstTheLiveRoom() => WithCafeAsync(async (editor, admin, before) =>
    {
        var overlay = new RoomOverlay("import-test-live", "cafe", "Live", new OverlayWindow(new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2090, 1, 2, 0, 0, 0, TimeSpan.Zero)));
        try
        {
            var report = await editor.ImportAsync(new ContentFile(Rooms: [new DraftRoom("cafe", before.Published!)], Overlays: [overlay]), admin, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(report.Problems, Is.Empty);
                Assert.That(report.Applied, Has.Some.Contains("overlay cafe/import-test-live"));
            });
        }
        finally
        {
            await editor.DeleteOverlayAsync("cafe", "import-test-live", admin, CancellationToken.None);
        }
    });

    [Test]
    public Task ADraftsLook_CanBeCleared_AndAFileRoomWithoutALookClearsIt() => WithCafeAsync(async (editor, admin, before) =>
    {
        var look = new RoomLook("#102030", "#405060", "#708090", new LookBackdrop([]));
        await editor.SaveDraftAsync("cafe", before.Published!, admin, null, CancellationToken.None, look);
        await editor.ClearDraftLookAsync("cafe", admin, CancellationToken.None);
        var cleared = await editor.GetAsync("cafe", CancellationToken.None);
        var again = Assert.ThrowsAsync<DomainException>(() => editor.ClearDraftLookAsync("cafe", admin, CancellationToken.None));

        await editor.SaveLookAsync("cafe", look, admin, CancellationToken.None);
        var changed = before.Published! with { Name = "Café changed" };
        var report = await editor.ImportAsync(new ContentFile(Rooms: [new DraftRoom("cafe", changed)]), admin, CancellationToken.None);
        var afterImport = await editor.GetAsync("cafe", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(cleared!.DraftLook, Is.Null);
            Assert.That(cleared.Draft, Is.Not.Null, "the draft itself stays");
            Assert.That(again!.Message, Does.Contain("no draft with a look"));
            Assert.That(report.Problems, Is.Empty);
            Assert.That(afterImport!.DraftLook, Is.Null, "the file has no look for the room, so its draft is left with none");
            Assert.That(afterImport.Draft!.Name, Is.EqualTo("Café changed"));
        });
    });

    [Test]
    public async Task ADraftWithALook_ScheduledForLater_PutsTheLookLiveWithTheLayout_AndRollbackTakesItBack()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var editor = Game.Get<IRoomEditor>();
        var before = (await editor.GetAsync("cafe", CancellationToken.None))!;
        var number = before.Versions.Single(v => v.State == RoomVersionState.Published).Number;
        var original = (await Db().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "cafe")).Look;
        var look = new RoomLook("#102030", "#405060", "#708090", new LookBackdrop([]));
        var at = DateTimeOffset.UtcNow.AddHours(1);
        try
        {
            await editor.SaveDraftAsync("cafe", before.Published!, adminPlayer.Id, null, CancellationToken.None, look);
            await editor.ScheduleAsync("cafe", [], at, adminPlayer.Id, CancellationToken.None);
            await using var scope = Game.Services.CreateAsyncScope();
            var due = await new RoomEditor(scope.ServiceProvider.GetRequiredService<IApplicationDbContext>(), Game.Get<IRoomLibrary>(), new FakeTimeProvider(at.AddMinutes(1))).PublishDueAsync(CancellationToken.None);
            var live = (await Db().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "cafe")).Look;
            await editor.RollbackAsync("cafe", number, adminPlayer.Id, CancellationToken.None);
            var back = (await Db().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "cafe")).Look;

            Assert.Multiple(() =>
            {
                Assert.That(due, Is.EqualTo(1));
                Assert.That(Json(live), Is.EqualTo(Json(look)), "the scheduled publish put the draft's look live");
                Assert.That(Json(back), Is.EqualTo(Json(original)), "and rolling back brought the old one back");
            });
        }
        finally
        {
            if ((await editor.GetAsync("cafe", CancellationToken.None))?.Draft is not null)
            {
                await editor.DiscardDraftAsync("cafe", adminPlayer.Id, CancellationToken.None);
            }

            await using var db = Db();
            (await db.RoomLooks.SingleAsync(l => l.Id == "cafe")).Replace(original, DateTimeOffset.UtcNow);
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public Task ARecipeNamedByADraftsLook_CannotBeDeleted() => WithCafeAsync(async (editor, admin, before) =>
    {
        var look = new RoomLook("#102030", "#405060", "#708090", new LookBackdrop([]), Floor: new LookPaint("recipe-draft-floor"));
        await editor.SavePainterAsync("recipe-draft-floor", "floor", [new PaintStep("fill", Colour: "#808080")], admin, CancellationToken.None);
        try
        {
            await editor.SaveDraftAsync("cafe", before.Published!, admin, null, CancellationToken.None, look);
            var refused = Assert.ThrowsAsync<DomainException>(() => editor.DeletePainterAsync("recipe-draft-floor", admin, CancellationToken.None));
            await editor.DiscardDraftAsync("cafe", admin, CancellationToken.None);
            await editor.DeletePainterAsync("recipe-draft-floor", admin, CancellationToken.None);

            Assert.That(refused!.Message, Does.Contain("cafe (draft)"));
        }
        finally
        {
            await using var db = Db();
            db.PainterRecipes.RemoveRange(await db.PainterRecipes.Where(r => r.Id == "recipe-draft-floor").ToListAsync());
            await db.SaveChangesAsync();
        }
    });

    [Test]
    public async Task LoadingTheContentFile_SavesWildlifeBeforeTheLookThatNamesIt()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        var moth = new DraftWildlife("import-test-moth", [new PropPart("ball", [0, 0.05, 0], "#d9d2c3", Diameter: 0.2)], new WildlifeMotion("wander", 0.6, 1.2, 3));
        var look = new RoomLook("#112233", "#445566", "#778899", new LookBackdrop([]), Wildlife: "import-test-moth");
        var file = new ContentFile(Looks: new DraftLooks([new DraftLook("import-test-look", look)]), Wildlife: [moth]);
        try
        {
            var report = await editor.ImportAsync(file, adminPlayer.Id, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(report.Problems, Is.Empty, "the recipe is saved first, so the look finds it");
                Assert.That(report.Applied, Has.Some.Contains("wildlife import-test-moth").And.Some.Contains("look import-test-look"));
            });
        }
        finally
        {
            await using var db = Db();
            db.RoomLooks.RemoveRange(await db.RoomLooks.Where(l => l.Id == "import-test-look").ToListAsync());
            db.WildlifeRecipes.RemoveRange(await db.WildlifeRecipes.Where(w => w.Id == "import-test-moth").ToListAsync());
            await db.SaveChangesAsync();
        }
    }

    [Test]
    public async Task LoadingTheContentFile_SavesAnOverlayOfAnUnpublishedRoom_AndReportsAMistypedRoomAsAProblem()
    {
        var (adminPlayer, cookie) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        using var admin = ClientFor(cookie);
        using var opened = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var layout = (await JsonOfAsync(opened))["published"]!.Deserialize<RoomLayout>(ContentFile.Json)!;
        var window = new OverlayWindow(new DateTimeOffset(2090, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2090, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var file = new ContentFile(
            [new DraftRoom("import-test-room", layout)],
            Overlays: [new RoomOverlay("wait", "import-test-room", "Waits", window), new RoomOverlay("typo", "import-test-rooom", "Mistyped", window)]);
        var editor = Game.Get<IRoomEditor>();
        try
        {
            var report = await editor.ImportAsync(file, adminPlayer.Id, CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Has.Some.Contains("overlay import-test-room/wait"), "the room is a draft from this same file, and its overlay is saved with it");
                Assert.That(report.Problems, Has.Some.Contains("import-test-rooom/typo"));
                Assert.That(report.Problems, Has.None.Contains("import-test-room/wait"));
                Assert.That(report.Unchanged, Has.None.Contains("overlay"), "a skipped overlay is not reported as unchanged");
            });
        }
        finally
        {
            await editor.DeleteOverlayAsync("import-test-room", "wait", adminPlayer.Id, CancellationToken.None);
            await editor.DiscardDraftAsync("import-test-room", adminPlayer.Id, CancellationToken.None);
        }
    }

    [Test]
    public async Task LoadingTheContentFile_MakesDrafts_ThatAreNotLiveUntilPublished()
    {
        var (adminPlayer, cookie) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        using var admin = ClientFor(cookie);
        using var opened = await SendAsync(admin, HttpMethod.Get, "rooms/cafe");
        var layout = (await JsonOfAsync(opened))["published"]!.DeepClone();
        layout["name"] = "Cafe (draft)";
        var file = new ContentFile([new DraftRoom("cafe", layout.Deserialize<RoomLayout>(ContentFile.Json)!)]);
        var editor = Game.Get<IRoomEditor>();
        var library = Game.Get<IRoomLibrary>();
        var player = adminPlayer.Id;
        try
        {
            var report = await editor.ImportAsync(file, player, CancellationToken.None);
            var again = await editor.ImportAsync(file, player, CancellationToken.None);
            var detail = await editor.GetAsync("cafe", CancellationToken.None);

            Assert.Multiple(() =>
            {
                Assert.That(report.Applied, Has.Some.Contains("cafe").And.Some.Contains("saved as a draft"));
                Assert.That(again.Applied, Is.Empty, "loading it twice changes nothing");
                Assert.That(detail!.Draft!.Name, Is.EqualTo("Cafe (draft)"));
                Assert.That(library.Find("cafe")!.Name, Is.Not.EqualTo("Cafe (draft)"), "players still have the published room");
            });
        }
        finally
        {
            await editor.DiscardDraftAsync("cafe", player, CancellationToken.None);
        }
    }

    [Test]
    public async Task LoadingTheContentFile_SavesWhatALookNamesBeforeTheLook_SoOneFileCanBringBoth()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        var floor = new PaintStep("fill", Colour: "#c0b090");
        var creature = new DraftWildlife("import-order-bird", [new PropPart("ball", [0, 0.5, 0], "#f5f5f5", Diameter: 0.3)], new WildlifeMotion("wander", 0.5, 1, Reach: 3));
        var look = new RoomLook("#aaccee", "#c0b090", "#d8c8b0", new LookBackdrop([]), Floor: new LookPaint("import-order-floor"), Wildlife: "import-order-bird");
        var file = new ContentFile(
            Looks: new DraftLooks([], [new DraftVariant("import-order-dusk", "#334466", "#554477")]),
            Painters: [new DraftPainter("import-order-floor", "floor", [floor])],
            Wildlife: [creature],
            Rooms: [new DraftRoom("import-order-room", new RoomLayout("Import order", 8, 8, new Position(0, 0), 4) { Look = "import-order-dusk" }, look)]);

        var report = await editor.ImportAsync(file, adminPlayer.Id, CancellationToken.None);

        Assert.Multiple(() =>
        {
            // The lone room has no route from the square (a book rule, unrelated to looks), so that is the only problem allowed.
            Assert.That(report.Problems, Is.EqualTo(new[] { "Every public room needs a walking route from the square." }), "a room painted in a variant from the same file, and a look naming a painter and a creature from it, raise nothing about looks");
            Assert.That(report.Applied, Has.Some.Contains("painter import-order-floor").And.Some.Contains("wildlife import-order-bird").And.Some.Contains("variant import-order-dusk").And.Some.Contains("room import-order-room"));
        });
    }

    [Test]
    public async Task LoadingTheContentFile_ChecksItsRoomsAsASet_SoRoomsThatLeadToEachOtherAreNotProblems()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        Game.Admins.Add(adminPlayer.Id);
        var file = ContentFile.Embedded();
        var editor = Game.Get<IRoomEditor>();
        try
        {
            var report = await editor.ImportAsync(file, adminPlayer.Id, CancellationToken.None);

            Assert.That(report.Problems, Is.Empty, "the rooms in the file are a valid book together, so loading them reports nothing");
        }
        finally
        {
            foreach (var room in file.RoomList)
            {
                await editor.DiscardDraftAsync(room.Id, adminPlayer.Id, CancellationToken.None);
            }
        }
    }

    [Test]
    public async Task LoadingTheContentFile_UpdatesTheLookOfARoomThatIsNotPublished_SoUnfinishedWorkCanKeepChanging()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();
        static ContentFile FileWith(string sky) => new(Looks: new DraftLooks([new DraftLook("unpublished-look-room", new RoomLook(sky, "#c0b090", "#d8c8b0", new LookBackdrop([])))]));

        var first = await editor.ImportAsync(FileWith("#aaccee"), adminPlayer.Id, CancellationToken.None);
        var changed = await editor.ImportAsync(FileWith("#112233"), adminPlayer.Id, CancellationToken.None);
        var again = await editor.ImportAsync(FileWith("#112233"), adminPlayer.Id, CancellationToken.None);

        await using var scope = Game.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "unpublished-look-room");
        Assert.Multiple(() =>
        {
            Assert.That(first.Applied, Has.Some.Contains("look unpublished-look-room"));
            Assert.That(changed.Problems, Is.Empty, "no room has it, so a different look in the file replaces the stored one");
            Assert.That(changed.Applied, Has.Some.Contains("look unpublished-look-room: replaced (no room has it)"));
            Assert.That(stored.Look.Sky, Is.EqualTo("#112233"));
            Assert.That(again.Unchanged, Has.Some.Contains("look unpublished-look-room"));
        });
    }

    [Test]
    public async Task LoadingTheContentFile_LeavesTheLookOfAPublishedRoomAlone_AndSaysSo()
    {
        var (adminPlayer, _) = await Game.SignInGuestAsync();
        var editor = Game.Get<IRoomEditor>();

        var report = await editor.ImportAsync(new ContentFile(Looks: new DraftLooks([new DraftLook("shop", new RoomLook("#010203", "#c0b090", "#d8c8b0", new LookBackdrop([])))])), adminPlayer.Id, CancellationToken.None);

        await using var scope = Game.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<IApplicationDbContext>().RoomLooks.AsNoTracking().SingleAsync(l => l.Id == "shop");
        Assert.Multiple(() =>
        {
            Assert.That(report.Problems, Has.Some.Contains("look shop: shop is a room, so its look goes in the room's own entry"));
            Assert.That(report.Applied, Has.None.Contains("look shop"));
            Assert.That(stored.Look.Sky, Is.Not.EqualTo("#010203"), "a room's look only comes in with the room");
        });
    }
}

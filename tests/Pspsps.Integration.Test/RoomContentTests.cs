using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Application.Abstractions;
using Pspsps.Application.Rooms;
using Pspsps.Domain.Rooms;
using Pspsps.Infrastructure.Persistence;

namespace Pspsps.Integration.Test;

/// <summary>Rooms as content in PostgreSQL: the starting rooms seeded, layouts and overlays kept as they were written.</summary>
public sealed class RoomContentTests
{
    private static PspspsDbContext Db() => new(new DbContextOptionsBuilder<PspspsDbContext>()
        .UseNpgsql(PostgresFixture.ConnectionString)
        .UseSnakeCaseNamingConvention()
        .Options);

    [Test]
    public async Task TheServer_SeedsTheStartingRooms_AsPublishedVersions_WhichComeBackAsTheyWentIn()
    {
        await using var db = Db();

        var published = await db.RoomVersions.Where(v => v.State == RoomVersionState.Published).ToListAsync();

        Assert.Multiple(() =>
        {
            Assert.That(published.Select(v => v.RoomId), Is.EquivalentTo(RoomCatalog.All.Select(room => room.Id)));
            foreach (var room in RoomCatalog.All)
            {
                var layout = published.Single(v => v.RoomId == room.Id).Layout;
                Assert.That(layout.ToDefinition(room.Id).Props, Is.EqualTo(room.Props), room.Id);
                Assert.That(layout.Doors, Is.EqualTo(RoomLayout.Of(room).Doors), room.Id);
                Assert.That(layout.Pond, Is.EqualTo(room.Pond), room.Id);
                Assert.That(layout.Exterior, Is.EqualTo(room.Exterior), room.Id);
            }
        });
    }

    [Test]
    public async Task TheLibrary_ReadsThePublishedRooms_AndTheirOverlaysOnceItsToldTo()
    {
        var services = new ServiceCollection();
        services.AddScoped<IApplicationDbContext>(_ => Db());
        await using var provider = services.BuildServiceProvider();
        var library = new RoomLibrary(provider.GetRequiredService<IServiceScopeFactory>(), TimeProvider.System);

        await library.RefreshAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(library.Start.Id, Is.EqualTo(RoomBook.StartingRoom));
            Assert.That(library.All.Select(room => room.Id), Is.EquivalentTo(RoomCatalog.All.Select(room => room.Id)));
            Assert.That(library.Find("garden")!.Props, Is.EqualTo(RoomCatalog.Find("garden")!.Props));
            Assert.That(library.Find("attic"), Is.Null);
        });
    }

    [Test]
    public async Task EveryKindOfPropTheServerPlaces_HasASeededDesign_ThatTheEndpointServes()
    {
        await using var db = Db();
        var designs = await db.PropDesigns.ToListAsync();

        Assert.That(designs.Select(d => d.Kind), Is.EquivalentTo(PropKinds.All));
        Assert.That(designs.All(d => PropDesign.Problems(d.Kind, d.Size, d.Parts).Count == 0), Is.True);
        Assert.That(await PropDesignSeeder.SeedAsync(db, TimeProvider.System, CancellationToken.None), Is.False);

        using var client = PostgresFixture.Game.CreateClient();
        var served = await client.GetFromJsonAsync<System.Text.Json.JsonElement>(new Uri("/api/props", UriKind.Relative));

        Assert.That(served.GetArrayLength(), Is.EqualTo(PropKinds.All.Count));
        Assert.That(served[0].GetProperty("parts")[0].TryGetProperty("top", out _), Is.False, "nothing is written as null");
    }

    [Test]
    public async Task SeedingAgain_ChangesNothing()
    {
        await using var db = Db();
        var before = await db.RoomVersions.CountAsync();

        var seeded = await RoomSeeder.SeedAsync(db, TimeProvider.System, CancellationToken.None);

        Assert.That((seeded, await db.RoomVersions.CountAsync()), Is.EqualTo((false, before)));
    }

    [Test]
    public async Task ARoom_CannotHaveTwoPublishedVersions_EvenWhenTwoPodsPublishAtOnce()
    {
        await using var db = Db();
        var layout = RoomLayout.Of(RoomCatalog.Find("garden")!);
        db.RoomVersions.Add(RoomVersion.Draft("garden", 90, layout, null, DateTimeOffset.UtcNow));
        var rival = RoomVersion.Draft("garden", 91, layout, null, DateTimeOffset.UtcNow);
        rival.Publish(DateTimeOffset.UtcNow);
        db.RoomVersions.Add(rival);

        var refused = Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        Assert.That(db.IsUniqueViolation(refused!), Is.True);
    }

    [Test]
    public async Task AnOverlay_IsKeptWithItsWindowItsPropsAndItsLook()
    {
        var lamp = new Prop(PropKinds.Lamp, new Position(-10.5, 0), 0.35, Facing: 0.5);
        var overlay = new RoomOverlay("winter-test", "town-square", "Winter",
            new OverlayWindow(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2027, 1, 6, 0, 0, 0, TimeSpan.Zero), Yearly: true))
        {
            Adds = [lamp],
            Removes = [RoomCatalog.Find("town-square")!.Props[0]],
            Look = "winter",
        };
        await using (var write = Db())
        {
            write.RoomOverlays.Add(RoomOverlayEntry.Of(overlay));
            await write.SaveChangesAsync();
        }

        await using var read = Db();
        var back = (await read.RoomOverlays.SingleAsync(o => o.Id == "winter-test")).ToOverlay();

        Assert.Multiple(() =>
        {
            Assert.That(back.Window, Is.EqualTo(overlay.Window));
            Assert.That(back.Adds, Is.EqualTo(overlay.Adds));
            Assert.That(back.Removes, Is.EqualTo(overlay.Removes));
            Assert.That((back.Name, back.Look, back.RoomId), Is.EqualTo(("Winter", "winter", "town-square")));
        });

        read.RoomOverlays.RemoveRange(read.RoomOverlays.Where(o => o.Id == "winter-test"));
        await read.SaveChangesAsync();
    }

    [Test]
    public async Task TheEditor_PublishesADraft_ArchivesTheOldVersion_AndRollsBack_AndRefusesABrokenBook()
    {
        await using var db = Db();
        var editor = new RoomEditor(db, FixedRoomLibrary.Starting(), TimeProvider.System);
        var ct = CancellationToken.None;
        var player = Pspsps.Domain.Players.Player.CreateGuest("Editor", Pspsps.Domain.Players.FurColor.Grey, DateTimeOffset.UtcNow);
        db.Players.Add(player);
        await db.SaveChangesAsync(ct);
        var admin = player.Id;
        var before = (await editor.GetAsync("garden", ct))!;
        var original = before.Published!;

        var problems = await editor.SaveDraftAsync("garden", original with { Name = "The Big Garden" }, admin, ct);
        Assert.That(problems, Is.Empty);
        await editor.PublishAsync("garden", ct);
        var published = (await editor.GetAsync("garden", ct))!;
        Assert.Multiple(() =>
        {
            Assert.That(published.Published!.Name, Is.EqualTo("The Big Garden"));
            Assert.That(published.Draft, Is.Null);
            Assert.That(published.Versions.Count(v => v.State == RoomVersionState.Published), Is.EqualTo(1));
        });

        await editor.RollbackAsync("garden", before.Versions.First(v => v.State == RoomVersionState.Published).Number, admin, ct);
        Assert.That((await editor.GetAsync("garden", ct))!.Published!.Name, Is.EqualTo(original.Name));

        await editor.SaveDraftAsync("garden", original with { Doors = [] }, admin, ct);
        Assert.That(async () => await editor.PublishAsync("garden", ct), Throws.TypeOf<Pspsps.Domain.DomainException>());
        await editor.DiscardDraftAsync("garden", ct);
        Assert.That((await editor.GetAsync("garden", ct))!.Draft, Is.Null);
    }
}

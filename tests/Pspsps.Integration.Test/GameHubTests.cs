using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Pspsps.Domain.Rooms;
using Pspsps.Server.Game;
using Pspsps.Server.Hubs;

namespace Pspsps.Integration.Test;

/// <summary>Two guests in the same room, end to end: cookies, hub, world and room grains, broadcasts.</summary>
public sealed class GameHubTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
    private static readonly string[] WorldIds = ["tabby", "calico", "siamese"];
    private static GameFactory Game => PostgresFixture.Game;

    [Test]
    public async Task CatsSeeEachOtherJoinMoveAndChat()
    {
        var (tom, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var tomSees = Record(tomHub);

        var tomRoom = await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        var felixRoom = await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");

        Assert.That(tomRoom.RoomId, Is.EqualTo("town-square"));
        Assert.That(tomRoom.Props, Is.EqualTo(RoomCatalog.Default.Props.Select(PropView.From)), "the square's props, to draw");
        Assert.That(felixRoom.Cats.Select(c => c.PlayerId), Is.EquivalentTo(new[] { tom.Id, felix.Id }));
        Assert.That(await tomSees.Next<CatView>("CatJoined", c => c.PlayerId == felix.Id), Is.Not.Null);

        await felixHub.InvokeAsync("Move", 1000.0, 2.5);
        var moved = await tomSees.Next<(Guid, double, double)>("CatMoved", m => m.Item1 == felix.Id);
        Assert.That(moved, Is.EqualTo((felix.Id, 12.0, 2.5)), "clamped to the 24 m wide square");

        await felixHub.InvokeAsync("Say", "  pspsps  ");
        var line = await tomSees.Next<ChatView>("ChatSaid", c => c.PlayerId == felix.Id);
        Assert.That(line.Text, Is.EqualTo("pspsps"));

        await felixHub.InvokeAsync<RoomSnapshot>("GoToRoom", "cafe");
        Assert.That(await tomSees.Next<Guid>("CatLeft", id => id == felix.Id), Is.EqualTo(felix.Id));
    }

    [Test]
    public async Task FilteredWordsNeverReachTheRoom()
    {
        // The test lists (GameFactory): "darn" masked, "woof" refused.
        var (_, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var tomSees = Record(tomHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");

        await felixHub.InvokeAsync("Say", "oh d4rn it");
        Assert.That((await tomSees.Next<ChatView>("ChatSaid", c => c.PlayerId == felix.Id)).Text, Is.EqualTo("oh **** it"));

        var refused = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync("Say", "w o o f"));
        Assert.That(refused!.Message, Does.Contain(ChatFilter.RefusedMessage));

        // The refused line was never said: Felix's next line is the next one Tom hears, and the room kept only those two.
        await felixHub.InvokeAsync("Say", "pspsps");
        Assert.That((await tomSees.Next<ChatView>("ChatSaid", c => c.PlayerId == felix.Id)).Text, Is.EqualTo("pspsps"));
        var room = await tomHub.InvokeAsync<RoomSnapshot>("GoToRoom", "town-square");
        Assert.That(room.Chat.Where(c => c.PlayerId == felix.Id).Select(c => c.Text), Is.EqualTo(new[] { "oh **** it", "pspsps" }));
    }

    [Test]
    public async Task RuleBreaksComeBackAsMessages()
    {
        var (_, hub) = await Game.ConnectGuestAsync();
        await using var _ = hub;

        var noWorld = Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync<RoomSnapshot>("EnterWorld", "nowhere"));
        Assert.That(noWorld!.Message, Does.Contain("That world doesn't exist."));

        await hub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        var tooLong = Assert.ThrowsAsync<HubException>(() => hub.InvokeAsync("Say", new string('a', 500)));
        Assert.That(tooLong!.Message, Does.Contain("at most 120 characters"));
    }

    [Test]
    public async Task CatsSeeEachOthersEmotes_AndTooFastOrUnknownOnesAreRefused()
    {
        var (_, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var tomSees = Record(tomHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");

        await felixHub.InvokeAsync("Emote", "wave");
        var wave = await tomSees.Next<EmoteView>("CatEmoted", e => e.PlayerId == felix.Id);
        Assert.That(wave.Emote, Is.EqualTo("wave"));

        // The grains' clock stands still until the test moves it: this meow is at the same instant as the wave.
        var tooFast = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync("Emote", "meow"));
        Assert.That(tooFast!.Message, Does.Contain("one emote a second"));

        var unknown = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync("Emote", "dance"));
        Assert.That(unknown!.Message, Does.Contain("That isn't an emote."));
        Assert.That(await tomSees.Saw<EmoteView>("CatEmoted", e => e.PlayerId == felix.Id, TimeSpan.FromSeconds(1)), Is.False);

        // A second later (on the grains' clock), the meow goes through.
        Game.GrainClock.Advance(RoomState.EmoteCooldown);
        await felixHub.InvokeAsync("Emote", "meow");
        Assert.That((await tomSees.Next<EmoteView>("CatEmoted", e => e.PlayerId == felix.Id)).Emote, Is.EqualTo("meow"));
    }

    [Test]
    public async Task ACatThatArrivesLater_SeesASleepingCatAsleep_UntilItWalks()
    {
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = felixHub;
        await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        await felixHub.InvokeAsync("Emote", "sleep");

        var (_, tomHub) = await Game.ConnectGuestAsync();
        await using var __ = tomHub;
        var tomRoom = await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        Assert.That(tomRoom.Cats.Single(c => c.PlayerId == felix.Id).Pose, Is.EqualTo("sleep"));

        await felixHub.InvokeAsync("Move", 1.0, 1.0);
        var (_, garfieldHub) = await Game.ConnectGuestAsync();
        await using var ___ = garfieldHub;
        var garfieldRoom = await garfieldHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        Assert.That(garfieldRoom.Cats.Single(c => c.PlayerId == felix.Id).Pose, Is.Null);
    }

    [Test]
    public async Task GetWorlds_CountsPlayersInEachWorld()
    {
        var (_, hub) = await Game.ConnectGuestAsync();
        await using var _ = hub;
        await hub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        var worlds = await hub.InvokeAsync<List<WorldSummary>>("GetWorlds");

        Assert.That(worlds.Select(w => w.Id), Is.EqualTo(WorldIds));
        Assert.That(worlds.Single(w => w.Id == "siamese").Population, Is.EqualTo(1));
    }

    [Test]
    public async Task ANewTabTakesTheCatOverAndTheOldTabIsToldAndRefused()
    {
        var (tom, cookie) = await Game.SignInGuestAsync();
        var (_, felixHub) = await Game.ConnectGuestAsync();
        await using var felix = felixHub;
        await using var oldTab = await Game.ConnectAsync(cookie);
        await using var newTab = await Game.ConnectAsync(cookie);
        var replaced = new TaskCompletionSource();
        oldTab.On("SessionReplaced", () => replaced.TrySetResult());
        await felix.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        var felixSees = Record(felix);

        await oldTab.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");
        await oldTab.InvokeAsync("Move", 3.0, 0.0);
        var room = await newTab.InvokeAsync<RoomSnapshot>("EnterWorld", "tabby");

        // One cat, where the old tab left it, and the old tab is told.
        await replaced.Task.WaitAsync(Wait);
        var toms = room.Cats.Where(c => c.PlayerId == tom.Id).ToList();
        Assert.That(toms, Has.Count.EqualTo(1));
        Assert.That((toms[0].X, toms[0].Z), Is.EqualTo((3.0, 0.0)));

        // The old tab can't play any more; the new one can.
        var refused = Assert.ThrowsAsync<HubException>(() => oldTab.InvokeAsync("Move", 1.0, 1.0));
        Assert.That(refused!.Message, Does.Contain(GameHub.ReplacedMessage));
        await newTab.InvokeAsync("Move", 2.0, 2.0);
        Assert.That(await felixSees.Next<(Guid, double, double)>("CatMoved", m => m.Item1 == tom.Id && m.Item2 == 2.0),
            Is.EqualTo((tom.Id, 2.0, 2.0)));

        // The old tab closing doesn't take the new tab's cat with it.
        await oldTab.StopAsync();
        Assert.That(await felixSees.Saw<Guid>("CatLeft", id => id == tom.Id, TimeSpan.FromSeconds(2)), Is.False);
    }

    [Test]
    public async Task ACatGoesThroughADoorItStandsOn_AndArrivesByTheMatchingDoor_ButNotThroughOneAcrossTheRoom()
    {
        var (_, tomHub) = await Game.ConnectGuestAsync();
        var (felix, felixHub) = await Game.ConnectGuestAsync();
        await using var _ = tomHub;
        await using var __ = felixHub;
        var tomSees = Record(tomHub);
        await tomHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        var square = await felixHub.InvokeAsync<RoomSnapshot>("EnterWorld", "calico");
        var door = square.Doors.Single(d => d.ToRoomId == "cafe");
        Assert.That(door.ToRoomName, Is.EqualTo("Cat Café"));

        // From the spawn point, across the square: refused, and Felix is still there, walking.
        var refused = Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("GoThroughDoor", door.Id));
        Assert.That(refused!.Message, Does.Contain("Walk up to the door to go through it."));
        await felixHub.InvokeAsync("Move", door.X, door.Z);
        Assert.That(await tomSees.Next<(Guid, double, double)>("CatMoved", m => m.Item1 == felix.Id),
            Is.EqualTo((felix.Id, door.X, door.Z)));

        // On the door: through it, into the café, just inside its door back to the square.
        var cafe = await felixHub.InvokeAsync<RoomSnapshot>("GoThroughDoor", door.Id);

        Assert.That(cafe.RoomId, Is.EqualTo("cafe"));
        Assert.That(await tomSees.Next<Guid>("CatLeft", id => id == felix.Id), Is.EqualTo(felix.Id));
        var back = RoomCatalog.Find("cafe")!.Doors.Single(d => d.ToRoom == "town-square");
        var felixInCafe = cafe.Cats.Single(c => c.PlayerId == felix.Id);
        Assert.That((felixInCafe.X, felixInCafe.Z), Is.EqualTo((back.Arrival.X, back.Arrival.Z)));
        Assert.That(cafe.Doors.Select(d => d.Id), Does.Contain(back.Id));

        // Arriving isn't standing on the door: going straight back is refused until Felix walks onto it.
        Assert.ThrowsAsync<HubException>(() => felixHub.InvokeAsync<RoomSnapshot>("GoThroughDoor", back.Id));
        await felixHub.InvokeAsync("Move", back.Spot.X, back.Spot.Z);
        var again = await felixHub.InvokeAsync<RoomSnapshot>("GoThroughDoor", back.Id);

        Assert.That(again.RoomId, Is.EqualTo("town-square"));
        var cafeDoor = RoomCatalog.Find("town-square")!.GetDoor(door.Id);
        var felixInSquare = again.Cats.Single(c => c.PlayerId == felix.Id);
        Assert.That((felixInSquare.X, felixInSquare.Z), Is.EqualTo((cafeDoor.Arrival.X, cafeDoor.Arrival.Z)));
        Assert.That(await tomSees.Next<CatView>("CatJoined", c => c.PlayerId == felix.Id), Is.Not.Null);
    }

    private static Recorder Record(HubConnection hub) => new(hub);
}

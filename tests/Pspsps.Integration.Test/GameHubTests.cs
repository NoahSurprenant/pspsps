using System.Threading.Channels;
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

    private static Recorder Record(HubConnection hub) => new(hub);

    /// <summary>Everything the server sends one connection, read in order.</summary>
    private sealed class Recorder
    {
        private readonly Channel<(string Method, object Payload)> _events = Channel.CreateUnbounded<(string, object)>();

        public Recorder(HubConnection hub)
        {
            hub.On<CatView>("CatJoined", c => _events.Writer.TryWrite(("CatJoined", c)));
            hub.On<Guid>("CatLeft", id => _events.Writer.TryWrite(("CatLeft", id)));
            hub.On<Guid, double, double>("CatMoved", (id, x, z) => _events.Writer.TryWrite(("CatMoved", (id, x, z))));
            hub.On<ChatView>("ChatSaid", c => _events.Writer.TryWrite(("ChatSaid", c)));
            hub.On<EmoteView>("CatEmoted", e => _events.Writer.TryWrite(("CatEmoted", e)));
        }

        /// <summary>Whether a matching event arrives within <paramref name="within"/>.</summary>
        public async Task<bool> Saw<T>(string method, Func<T, bool> match, TimeSpan within)
        {
            using var timeout = new CancellationTokenSource(within);
            try
            {
                while (true)
                {
                    var (name, payload) = await _events.Reader.ReadAsync(timeout.Token);
                    if (name == method && payload is T value && match(value))
                    {
                        return true;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        public async Task<T> Next<T>(string method, Func<T, bool> match)
        {
            using var timeout = new CancellationTokenSource(Wait);
            while (true)
            {
                var (name, payload) = await _events.Reader.ReadAsync(timeout.Token);
                if (name == method && payload is T value && match(value))
                {
                    return value;
                }
            }
        }
    }
}

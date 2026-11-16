using System.Threading.Channels;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Pspsps.Server.Game;

namespace Pspsps.Integration.Test;

/// <summary>Two guests in the same room, end to end: cookies, hub, world and room grains, broadcasts.</summary>
public sealed class GameHubTests
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);
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
    public async Task GetWorlds_CountsPlayersInEachWorld()
    {
        var (_, hub) = await Game.ConnectGuestAsync();
        await using var _ = hub;
        await hub.InvokeAsync<RoomSnapshot>("EnterWorld", "siamese");

        var worlds = await hub.InvokeAsync<List<WorldSummary>>("GetWorlds");

        Assert.That(worlds.Select(w => w.Id), Is.EqualTo(new[] { "tabby", "calico", "siamese" }));
        Assert.That(worlds.Single(w => w.Id == "siamese").Population, Is.EqualTo(1));
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

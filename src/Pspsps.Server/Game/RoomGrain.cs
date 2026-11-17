using Orleans.Runtime;
using Pspsps.Domain;
using Pspsps.Domain.Fishing;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Game;

/// <summary>
/// Holds a room's <see cref="RoomState"/> in memory and broadcasts every change. Calls run one at a time. Kept alive while
/// cats are in it (idle cats are still there), deactivated by the last one leaving. Takes out cats whose session stopped
/// renewing its lease (<see cref="SessionLease"/>).
/// </summary>
[KeepAlive]
internal sealed class RoomGrain(
    IRoomBroadcaster broadcaster,
    ChatFilters chatFilters,
    [FromKeyedServices(TimeProviderNames.Grains)] TimeProvider time,
    Random random,
    GameMetrics metrics) : Grain, IRoomGrain
{
    private string _key = "";
    private RoomState _state = null!;

    // Counted in pspsps.rooms.active once a cat joins, not on activation: a renewal can wake an empty room for a moment.
    private bool _counted;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _key = this.GetPrimaryKeyString();
        var (_, roomId) = RoomKey.Parse(_key);
        var room = RoomCatalog.Find(roomId) ?? throw new InvalidOperationException($"No room '{roomId}'.");
        _state = new RoomState(room);
        this.RegisterGrainTimer(
            static (grain, _) => grain.RemoveExpired(),
            this,
            new GrainTimerCreationOptions { DueTime = SessionLease.RenewEvery, Period = SessionLease.RenewEvery });
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_counted)
        {
            metrics.RoomDeactivated();
        }

        return Task.CompletedTask;
    }

    public async Task<RoomSnapshot> Join(PlayerCard player, string session)
    {
        var fur = Enum.Parse<FurColor>(player.FurColor);
        var cat = Rule(() => _state.Join(player.PlayerId, player.DisplayName, fur, session, time.GetUtcNow()));
        if (!_counted)
        {
            _counted = true;
            metrics.RoomActivated();
        }

        await broadcaster.CatJoined(_key, CatView.From(cat));

        var (worldId, _) = RoomKey.Parse(_key);
        return new RoomSnapshot(
            worldId,
            _state.Room.Id,
            _state.Room.Name,
            _state.Room.Width,
            _state.Room.Depth,
            [.. _state.Cats.Select(CatView.From)],
            [.. _state.RecentChat.Select(ChatView.From)],
            [.. RoomCatalog.All.Where(r => r.Id != _state.Room.Id).Select(r => new RoomLink(r.Id, r.Name))],
            _state.Room.Pond is { } pond ? new PondView(pond.Center.X, pond.Center.Z, pond.Radius) : null);
    }

    public async Task Leave(Guid playerId, string session)
    {
        if (_state.Leave(playerId, session))
        {
            await broadcaster.CatLeft(_key, playerId);
        }

        if (_state.Cats.Count == 0)
        {
            DeactivateOnIdle();
        }
    }

    public Task<IReadOnlyList<string>> Renew(IReadOnlyList<PlayerSession> sessions)
    {
        var now = time.GetUtcNow();
        var lost = sessions
            .Where(s => _state.Renew(s.PlayerId, s.Session, now) == CatRenewal.Gone)
            .Select(s => s.Session)
            .ToList();

        // A renewal can be what activated this room (it came back empty after its pod died): the tabs are told and stop
        // renewing here, so an empty room goes again rather than waiting for its timer.
        if (_state.Cats.Count == 0)
        {
            DeactivateOnIdle();
        }

        return Task.FromResult<IReadOnlyList<string>>(lost);
    }

    public async Task Move(Guid playerId, double x, double z)
    {
        var position = Rule(() => _state.Move(playerId, new Position(x, z)));
        await broadcaster.CatMoved(_key, playerId, position.X, position.Z);
    }

    public async Task Say(Guid playerId, string text)
    {
        var line = Rule(() => _state.Say(playerId, text, chatFilters.Current, time.GetUtcNow()));
        await broadcaster.ChatSaid(_key, ChatView.From(line));
    }

    public async Task Emote(Guid playerId, string emote)
    {
        var done = Rule(() => _state.Emote(playerId, emote, time.GetUtcNow()));
        await broadcaster.CatEmoted(_key, EmoteView.From(done));
    }

    public Task Cast(Guid playerId, string session)
    {
        var now = time.GetUtcNow();
        var line = Rule(() => _state.Cast(playerId, now, random.NextDouble()));
        // One shot, at the bite. The grain timer runs on the grain clock and inside the grain, like every call here.
        IGrainTimer? bite = null;
        bite = this.RegisterGrainTimer(
            async ct =>
            {
                bite?.Dispose();
                // Only if this line is still out: the cat may have reeled early, walked off or left meanwhile.
                if (ReferenceEquals(_state.LineOf(playerId), line))
                {
                    await broadcaster.FishBit(session);
                }
            },
            new GrainTimerCreationOptions { DueTime = line.BiteAt - now, Period = Timeout.InfiniteTimeSpan });
        return Task.CompletedTask;
    }

    public async Task<ReelView> Reel(Guid playerId)
    {
        var result = Rule(() => _state.Reel(playerId, time.GetUtcNow(), random.NextDouble()));
        if (result.Fish is not { } fish)
        {
            var outcome = result.Outcome == ReelOutcome.TooEarly ? "too-early" : "too-late";
            return new ReelView(outcome, null, null, 0, 0);
        }

        await broadcaster.CatFished(_key, new CatchView(playerId, fish.Id, fish.Name, fish.Coins));
        return new ReelView("caught", fish.Id, fish.Name, fish.Coins, 0);
    }

    // Cats whose pod died without closing their connections: nothing renews their leases.
    private async Task RemoveExpired()
    {
        var expired = _state.RemoveExpired(time.GetUtcNow());
        if (expired.Count > 0)
        {
            metrics.LeasesExpired("room", expired.Count);
        }

        await Task.WhenAll(expired.Select(playerId => broadcaster.CatLeft(_key, playerId)));

        if (_state.Cats.Count == 0)
        {
            DeactivateOnIdle();
        }
    }

    // DomainException stays in this pod; callers get a GameRuleException, which Orleans knows how to copy.
    private static T Rule<T>(Func<T> change)
    {
        try
        {
            return change();
        }
        catch (DomainException e)
        {
            throw new GameRuleException(e.Message);
        }
    }
}

using Orleans.Runtime;
using Pspsps.Application.Homes;
using Pspsps.Domain;
using Pspsps.Domain.Fishing;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Game;

/// <summary>
/// Holds a room's <see cref="RoomState"/> in memory and broadcasts every change. Calls run one at a time. Kept alive while
/// cats are in it (idle cats are still there), deactivated by the last one leaving. Takes out cats whose session stopped
/// renewing its lease (<see cref="SessionLease"/>). A world's room comes from the room catalogue; a player's home
/// (<see cref="RoomKey.HomeOwner"/>) is read from the database, its furniture and all, when it activates.
/// </summary>
[KeepAlive]
internal sealed class RoomGrain(
    IRoomBroadcaster broadcaster,
    ChatFilters chatFilters,
    [FromKeyedServices(TimeProviderNames.Grains)] TimeProvider time,
    Random random,
    GameMetrics metrics,
    IServiceScopeFactory scopes) : Grain, IRoomGrain
{
    private string _key = "";
    private RoomState _state = null!;

    // The room's doors and props as the browser sees them: catalogue data, the same for every join.
    private IReadOnlyList<DoorView> _doors = [];
    private IReadOnlyList<PropView> _props = [];

    // A home's owner, furniture and who may visit; null for a world's room.
    private HomeView? _home;

    // Counted in pspsps.rooms.active once a cat joins, not on activation: a renewal can wake an empty room for a moment.
    private bool _counted;

    // Each cat's pending fishing timer (the bite, then the getaway): one at a time.
    private readonly Dictionary<Guid, IGrainTimer> _fishingTimers = [];

    // How long after the bite window closes the getaway timer fires, so it never beats a reel at the window's edge.
    private static readonly TimeSpan GetAwayGrace = TimeSpan.FromMilliseconds(50);

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _key = this.GetPrimaryKeyString();
        RoomDefinition room;
        if (RoomKey.HomeOwner(_key) is { } owner)
        {
            await using var scope = scopes.CreateAsyncScope();
            var plan = await scope.ServiceProvider.GetRequiredService<IHomeService>().GetAsync(owner, cancellationToken);
            room = Home.RoomOf(plan.OwnerName, plan.Furniture);
            _home = HomeView.From(plan);
        }
        else
        {
            var (_, roomId) = RoomKey.Parse(_key);
            room = RoomCatalog.Find(roomId) ?? throw new InvalidOperationException($"No room '{roomId}'.");
        }

        _state = new RoomState(room);
        _doors = [.. room.Doors.Select(DoorView.From)];
        // A home's furniture as the room's props, the same way as every change to it after (Furnish).
        _props = _home?.Props() ?? [.. room.Props.Select(PropView.From)];
        this.RegisterGrainTimer(
            static (grain, _) => grain.RemoveExpired(),
            this,
            new GrainTimerCreationOptions { DueTime = SessionLease.RenewEvery, Period = SessionLease.RenewEvery });
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        if (_counted)
        {
            metrics.RoomDeactivated();
        }

        return Task.CompletedTask;
    }

    public async Task<RoomSnapshot> Join(PlayerCard player, string session, string? throughDoor)
    {
        var fur = Enum.Parse<FurColor>(player.FurColor);
        var cat = Rule(() => _state.Join(
            player.PlayerId, player.DisplayName, fur, session, time.GetUtcNow(), throughDoor, player.Hat, player.Collar,
            player.OutfitVersion));
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
            // A home is called after its owner as they are called now (Furnish brings the name), not when it opened.
            _home is null ? _state.Room.Name : Home.NameOf(_home.OwnerName),
            _state.Room.Width,
            _state.Room.Depth,
            [.. _state.Cats.Select(CatView.From)],
            [.. _state.RecentChat.Select(ChatView.From)],
            // A home is in no world: its door is the way out.
            _home is null ? [.. RoomCatalog.All.Where(r => r.Id != _state.Room.Id).Select(r => new RoomLink(r.Id, r.Name))] : [],
            _state.Room.Pond is { } pond ? new PondView(pond.Center.X, pond.Center.Z, pond.Radius) : null,
            _doors,
            _props,
            _home);
    }

    public async Task Leave(Guid playerId, string session)
    {
        if (_state.Leave(playerId, session))
        {
            await broadcaster.CatLeft(_key, playerId);
        }

        GoIfEmpty();
    }

    public async Task<DoorTarget> LeaveThrough(Guid playerId, string session, string doorId)
    {
        var door = Rule(() => _state.LeaveThrough(playerId, session, doorId));
        await broadcaster.CatLeft(_key, playerId);
        GoIfEmpty();
        return new DoorTarget(door.ToRoom, door.ToDoor);
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

    public Task<bool> Has(Guid playerId, string session)
    {
        // Asking can be what activated this room (it came back empty after its pod died): an empty room goes again.
        if (_state.Cats.Count == 0)
        {
            DeactivateOnIdle();
        }

        return Task.FromResult(_state.Has(playerId, session));
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

    public async Task Dress(OutfitView outfit)
    {
        ArgumentNullException.ThrowIfNull(outfit);
        var fur = Enum.Parse<FurColor>(outfit.FurColor);
        if (_state.Dress(outfit.PlayerId, fur, outfit.Hat, outfit.Collar, outfit.Version) is { } cat)
        {
            await broadcaster.CatDressed(_key, OutfitView.From(cat));
        }
        else
        {
            // Not here, or an older outfit than it has. Asking can be what activated this room (the cat's tab just left
            // it): an empty room goes again.
            GoIfEmpty();
        }
    }

    public Task Cast(Guid playerId)
    {
        var now = time.GetUtcNow();
        var line = Rule(() => _state.Cast(playerId, now, random.NextDouble()));
        // One shot, at the bite. The grain timer runs on the grain clock and inside the grain, like every call here; it
        // can fire late (a busy room), and the reel window runs from when it does.
        Schedule(playerId, line.BiteAt - now, async () =>
        {
            // Nobody to tell when the line is gone (reeled early, walked off, left, or taken over by another tab).
            if (_state.Bite(playerId, line, time.GetUtcNow()) is { } controlling)
            {
                await broadcaster.FishBit(controlling);
                // Then, a little after the window closes (a timer can fire early by a tick), a bite not reeled in
                // gets away.
                Schedule(playerId, FishingLine.BiteWindow + GetAwayGrace, async () =>
                {
                    if (_state.LetGetAway(playerId, line, time.GetUtcNow()) is { } told)
                    {
                        await broadcaster.FishGotAway(told);
                    }
                });
            }
        });
        return Task.CompletedTask;
    }

    public Task<ReelView> Reel(Guid playerId)
    {
        var result = Rule(() => _state.Reel(playerId, time.GetUtcNow(), random.NextDouble()));
        Unschedule(playerId);
        return Task.FromResult(ReelView.From(result));
    }

    public async Task Furnish(HomeView home)
    {
        ArgumentNullException.ThrowIfNull(home);
        // A home only, and not an older change than it has (two changes told out of order).
        if (_home is not null && home.OwnerId == _home.OwnerId && home.Version >= _home.Version)
        {
            _home = home;
            _props = home.Props();
            await broadcaster.HomeChanged(_key, home);
        }

        // Telling a home nobody is in can be what activated it: it goes again.
        GoIfEmpty();
    }

    public Task<IReadOnlyList<Guid>> CatsHere()
    {
        IReadOnlyList<Guid> here = [.. _state.Cats.Select(cat => cat.PlayerId)];
        GoIfEmpty();
        return Task.FromResult(here);
    }

    public async Task SendOut(IReadOnlyList<Guid> playerIds, string message)
    {
        ArgumentNullException.ThrowIfNull(playerIds);
        foreach (var playerId in playerIds)
        {
            if (_state.SendOut(playerId) is { } session)
            {
                Unschedule(playerId);
                await broadcaster.CatLeft(_key, playerId);
                await broadcaster.SentOut(_key, session, message);
            }
        }

        GoIfEmpty();
    }

    // At most one pending fishing timer per cat: a new one replaces (disposes) the cat's last.
    private void Schedule(Guid playerId, TimeSpan due, Func<Task> callback)
    {
        Unschedule(playerId);
        IGrainTimer? timer = null;
        timer = this.RegisterGrainTimer(
            async ct =>
            {
                if (_fishingTimers.TryGetValue(playerId, out var current) && ReferenceEquals(current, timer))
                {
                    _fishingTimers.Remove(playerId);
                }

                timer?.Dispose();
                await callback();
            },
            new GrainTimerCreationOptions { DueTime = due, Period = Timeout.InfiniteTimeSpan });
        _fishingTimers[playerId] = timer;
    }

    private void Unschedule(Guid playerId)
    {
        if (_fishingTimers.Remove(playerId, out var timer))
        {
            timer.Dispose();
        }
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

    // The last cat out: the room goes, and forgets everything.
    private void GoIfEmpty()
    {
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

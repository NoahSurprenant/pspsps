using Pspsps.Domain;
using Pspsps.Domain.Players;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

/// <summary>
/// Holds a room's <see cref="RoomState"/> in memory and broadcasts every change. Calls run one at a time. Kept alive while
/// cats are in it (idle cats are still there), deactivated by the last one leaving.
/// </summary>
[KeepAlive]
internal sealed class RoomGrain(IRoomBroadcaster broadcaster, TimeProvider time) : Grain, IRoomGrain
{
    private string _key = "";
    private RoomState _state = null!;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _key = this.GetPrimaryKeyString();
        var (_, roomId) = RoomKey.Parse(_key);
        var room = RoomCatalog.Find(roomId) ?? throw new InvalidOperationException($"No room '{roomId}'.");
        _state = new RoomState(room);
        return Task.CompletedTask;
    }

    public async Task<RoomSnapshot> Join(PlayerCard player)
    {
        var fur = Enum.Parse<FurColor>(player.FurColor);
        var cat = Rule(() => _state.Join(player.PlayerId, player.DisplayName, fur));
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
            [.. RoomCatalog.All.Where(r => r.Id != _state.Room.Id).Select(r => new RoomLink(r.Id, r.Name))]);
    }

    public async Task Leave(Guid playerId)
    {
        if (_state.Leave(playerId))
        {
            await broadcaster.CatLeft(_key, playerId);
        }

        if (_state.Cats.Count == 0)
        {
            DeactivateOnIdle();
        }
    }

    public async Task Move(Guid playerId, double x, double z)
    {
        var position = Rule(() => _state.Move(playerId, new Position(x, z)));
        await broadcaster.CatMoved(_key, playerId, position.X, position.Z);
    }

    public async Task Say(Guid playerId, string text)
    {
        var line = Rule(() => _state.Say(playerId, text, time.GetUtcNow()));
        await broadcaster.ChatSaid(_key, ChatView.From(line));
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

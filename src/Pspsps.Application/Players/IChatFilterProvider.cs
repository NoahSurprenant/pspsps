using Pspsps.Domain.Rooms;

namespace Pspsps.Application.Players;

/// <summary>
/// The chat filter rooms' chat is checked against now (<c>Pspsps.Server.Game.ChatFilters</c>), so a rename
/// (<see cref="Domain.Players.Player.Rename"/>, <see cref="Domain.Players.Player.RenameByAdmin"/>) is refused a name the
/// same lists and look-alike handling would mask or refuse.
/// </summary>
public interface IChatFilterProvider
{
    ChatFilter Current { get; }
}

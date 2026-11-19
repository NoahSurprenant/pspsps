using Orleans.CodeGeneration;

namespace Pspsps.Server.Game;

/// <summary>One room of one world, keyed by <see cref="RoomKey"/>. One activation in the whole cluster.</summary>
/// <remarks>
/// Versioned: a change to it is add-only and bumps the version (docs/architecture.md, "Changing what pods say to each
/// other"), which Pspsps.Server.Test's WireContractTests check against wire-contracts.txt.
/// </remarks>
[Version(1)]
public interface IRoomGrain : IGrainWithStringKey
{
    /// <summary>
    /// Puts the player's cat in the room, controlled by <paramref name="session"/> (the tab's connection): at the
    /// spawn point, or just inside the door <paramref name="throughDoor"/> when it came in through one.
    /// </summary>
    Task<RoomSnapshot> Join(PlayerCard player, string session, string? throughDoor);

    /// <summary>Takes the cat out, unless another session (a newer tab) controls it now.</summary>
    Task Leave(Guid playerId, string session);

    /// <summary>
    /// Takes the cat out through the door <paramref name="doorId"/>, if <paramref name="session"/> controls it and it
    /// stands on that door; returns where the door leads. Refused (a <see cref="GameRuleException"/>, the cat stays)
    /// when it isn't on the door.
    /// </summary>
    Task<DoorTarget> LeaveThrough(Guid playerId, string session, string doorId);

    /// <summary>
    /// Renews the leases of the cats these sessions control. The room takes out cats whose lease runs out
    /// (<see cref="Pspsps.Domain.SessionLease"/>), as when the pod holding their connections died. Returns the sessions
    /// whose cat isn't here any more (its lease ran out, or this room came back empty after its pod died); a cat another
    /// session took over isn't lost.
    /// </summary>
    Task<IReadOnlyList<string>> Renew(IReadOnlyList<PlayerSession> sessions);

    /// <summary>Whether the player's cat is here, played from <paramref name="session"/>: where friends find it.</summary>
    Task<bool> Has(Guid playerId, string session);

    Task Move(Guid playerId, double x, double z);

    Task Say(Guid playerId, string text);

    Task Emote(Guid playerId, string emote);

    /// <summary>Casts the cat's line; at the bite the room tells the tab controlling the cat then (FishBit), and only it.</summary>
    Task Cast(Guid playerId);

    /// <summary>
    /// The player changed what their cat wears: if the cat is here, it wears that from now on, and the room is shown it
    /// (CatDressed). Nothing when it isn't, or already wears a newer outfit (OutfitView.Version).
    /// </summary>
    Task Dress(OutfitView outfit);

    /// <summary>Reels the line in. Nothing is shown to the room here: the hub pays a catch first, then shows it.</summary>
    Task<ReelView> Reel(Guid playerId);

    /// <summary>
    /// A home's owner changed it (saved by then): the room draws its furniture as <paramref name="home"/> has it from now
    /// on, and is shown it (HomeChanged). Nothing for a world's room.
    /// </summary>
    Task Furnish(HomeView home);

    /// <summary>The players whose cats are in the room now.</summary>
    Task<IReadOnlyList<Guid>> CatsHere();

    /// <summary>
    /// The room's recent chat, oldest first (<see cref="Pspsps.Domain.Rooms.RoomState.ChatHistory"/> lines at most), as the room has
    /// it: what a report keeps of what was said.
    /// </summary>
    Task<IReadOnlyList<ChatView>> RecentChat();

    /// <summary>
    /// Takes these players' cats out of the room (a home they may no longer stay in), whichever tab plays them; each
    /// tab is told <paramref name="message"/> (SentOut), and the rest of the room sees them go.
    /// </summary>
    Task SendOut(IReadOnlyList<Guid> playerIds, string message);
}

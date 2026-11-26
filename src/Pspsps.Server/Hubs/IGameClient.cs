using Pspsps.Server.Game;

namespace Pspsps.Server.Hubs;

/// <summary>What the server calls on browsers (client/src/net.ts handles each).</summary>
public interface IGameClient
{
    Task CatJoined(CatView cat);

    Task CatLeft(Guid playerId);

    Task CatMoved(Guid playerId, double x, double z);

    Task BallMoved(BallView ball);

    Task ChatSaid(ChatView line);

    /// <summary>
    /// The room no longer has this tab's cat (its lease ran out, or the room came back empty after its pod died): enter the
    /// world again, then go back to that room.
    /// </summary>
    Task RoomLost();

    /// <summary>
    /// The world no longer counts this tab and is full (it came back empty after its pod died, and filled up before this
    /// tab's renewal arrived): pick a world again. <paramref name="message"/> says why.
    /// </summary>
    Task WorldLost(string message);

    Task CatEmoted(EmoteView emote);

    /// <summary>A cat in the room put on or took off a hat or collar, or changed its fur.</summary>
    Task CatDressed(OutfitView outfit);

    /// <summary>A cat in the room was renamed: its label above it, and anywhere else this tab shows it, is shown it now.</summary>
    Task CatRenamed(RenameView rename);

    /// <summary>This room is a home whose owner (not necessarily this tab's cat) was renamed: shown as their new name's home.</summary>
    Task HomeRenamed(string ownerName);

    /// <summary>
    /// This player (any of their tabs) renamed their own cat: their own card and the catalogue, wherever they show the
    /// name, are brought up to date.
    /// </summary>
    Task Renamed(string displayName);

    /// <summary>Something bit this tab's line: reel in within <see cref="Pspsps.Domain.Fishing.FishingLine.BiteWindow"/>.</summary>
    Task FishBit();

    /// <summary>The bite on this tab's line wasn't reeled in time: it got away, and the line is out of the water.</summary>
    Task FishGotAway();

    /// <summary>A cat in the room caught something.</summary>
    Task CatFished(CatchView caught);

    /// <summary>The player opened the game in another tab, which is the one playing now.</summary>
    Task SessionReplaced();

    /// <summary>
    /// Another player asked this one to be friends, answered their request, took theirs back or ended the friendship: the
    /// friends list and a player card showing that player are out of date. Sent to every tab of the player.
    /// </summary>
    Task FriendsChanged(Guid playerId);

    /// <summary>The owner of the home this tab's cat is in changed its furniture or who may visit.</summary>
    Task HomeChanged(HomeView home);

    /// <summary>
    /// This tab's cat was sent out of <paramref name="ownerId"/>'s home (its owner no longer lets it in): go back to Town
    /// Square, unless the tab has gone elsewhere meanwhile. <paramref name="message"/> says why.
    /// </summary>
    Task SentOut(Guid ownerId, string message);

    /// <summary>
    /// An admin banned the player: <paramref name="ban"/> says why and until when. The tab shows it and stops; the server
    /// closes the connection shortly after, and refuses every call and connection until the ban ends.
    /// </summary>
    Task Banned(SanctionView ban);

    /// <summary>An admin muted the player (<paramref name="mute"/>: why, until when), or lifted it (null): chat is off until then.</summary>
    Task Muted(SanctionView? mute);

    /// <summary>An admin warned the player: the tab fetches its warnings (GetWarnings) and shows each until it's closed.</summary>
    Task Warned();

    /// <summary>
    /// The player muted or unmuted someone (from any of their tabs): <paramref name="mutes"/> is everyone they have muted
    /// now, whose chat the tab hides. Sent to every tab of the player.
    /// </summary>
    Task MutesChanged(IReadOnlyList<MutedView> mutes);
}

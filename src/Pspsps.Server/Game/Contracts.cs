using System.Diagnostics.CodeAnalysis;
using Pspsps.Application.Homes;
using Pspsps.Application.Moderation;
using Pspsps.Application.Players;
using Pspsps.Application.Rooms;
using Pspsps.Application.Safety;
using Pspsps.Application.Shop;
using Pspsps.Domain.Fishing;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Moderation;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;
using Pspsps.Domain.Worlds;

namespace Pspsps.Server.Game;

// What grains take and return, and what the hub sends to browsers. Orleans copies these between pods.

/// <summary>Who a player is, as a room needs to know them: their name, and what their cat wears (shop item IDs).</summary>
[GenerateSerializer, Immutable]
public sealed record PlayerCard(
    [property: Id(0)] Guid PlayerId,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string FurColor,
    [property: Id(3)] string? Hat = null,
    [property: Id(4)] string? Collar = null,
    [property: Id(5)] int OutfitVersion = 0);

/// <summary>
/// What a cat wears: its fur, and the hat and collar it has on (shop item IDs), if any, as of Version (the player's
/// OutfitVersion: a room keeps the newer of two).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record OutfitView(
    [property: Id(0)] Guid PlayerId,
    [property: Id(1)] string FurColor,
    [property: Id(2)] string? Hat,
    [property: Id(3)] string? Collar,
    [property: Id(4)] int Version = 0)
{
    public static OutfitView From(CatInRoom cat)
    {
        ArgumentNullException.ThrowIfNull(cat);
        return new(cat.PlayerId, cat.FurColor.ToString(), cat.Hat, cat.Collar, cat.OutfitVersion);
    }

    public static OutfitView From(Wardrobe wardrobe)
    {
        ArgumentNullException.ThrowIfNull(wardrobe);
        return new(wardrobe.PlayerId, wardrobe.FurColor.ToString(), wardrobe.Hat, wardrobe.Collar, wardrobe.OutfitVersion);
    }
}

[GenerateSerializer, Immutable]
public sealed record CatView(
    [property: Id(0)] Guid PlayerId,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string FurColor,
    [property: Id(3)] double X,
    [property: Id(4)] double Z,
    [property: Id(5)] string? Pose,
    [property: Id(6)] string? Hat,
    [property: Id(7)] string? Collar)
{
    public static CatView From(CatInRoom cat) =>
        new(cat.PlayerId, cat.DisplayName, cat.FurColor.ToString(), cat.Position.X, cat.Position.Z, Emotes.NameOf(cat.Pose),
            cat.Hat, cat.Collar);
}

[GenerateSerializer, Immutable]
public sealed record ChatView([property: Id(0)] Guid PlayerId, [property: Id(1)] string Text, [property: Id(2)] DateTimeOffset At)
{
    public static ChatView From(ChatLine line) => new(line.PlayerId, line.Text, line.At);
}

/// <summary>A cat was renamed: its new name, shown above it and on its card from now on.</summary>
[GenerateSerializer, Immutable]
public sealed record RenameView([property: Id(0)] Guid PlayerId, [property: Id(1)] string DisplayName)
{
    public static RenameView From(CatInRoom cat) => new(cat.PlayerId, cat.DisplayName);
}

/// <summary>A cat did an emote (one of Emotes.All: "sit", "meow", "wave", "sleep").</summary>
[GenerateSerializer, Immutable]
public sealed record EmoteView([property: Id(0)] Guid PlayerId, [property: Id(1)] string Emote, [property: Id(2)] DateTimeOffset At)
{
    public static EmoteView From(CatEmote emote) => new(emote.PlayerId, emote.Emote, emote.At);
}

/// <summary>
/// A room as the browser draws it when the cat comes in. <see cref="Notice"/> says why the cat is here when it isn't where
/// it was going (that room was full, say, and it landed in the next place on its way out).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record RoomSnapshot(
    [property: Id(0)] string WorldId,
    [property: Id(1)] string RoomId,
    [property: Id(2)] string RoomName,
    [property: Id(3)] double Width,
    [property: Id(4)] double Depth,
    [property: Id(5)] IReadOnlyList<CatView> Cats,
    [property: Id(6)] IReadOnlyList<ChatView> Chat,
    [property: Id(7)] IReadOnlyList<RoomLink> Rooms,
    [property: Id(8)] PondView? Pond,
    [property: Id(9)] IReadOnlyList<DoorView> Doors,
    [property: Id(10)] IReadOnlyList<PropView> Props,
    [property: Id(11)] HomeView? Home = null,
    [property: Id(12)] string? Notice = null,
    [property: Id(13)] WorldMapView? WorldMap = null,
    [property: Id(14)] string? Look = null,
    [property: Id(15)] IReadOnlyList<ChunkView>? Chunks = null);

/// <summary>
/// One outdoor room of the open world, for drawing it in place: its middle in world metres (X, Z), its floor, and its
/// pond, doors and props in metres from that middle.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record ChunkView(
    [property: Id(0)] string RoomId,
    [property: Id(1)] string Name,
    [property: Id(2)] double X,
    [property: Id(3)] double Z,
    [property: Id(4)] double Width,
    [property: Id(5)] double Depth,
    [property: Id(6)] PondView? Pond,
    [property: Id(7)] IReadOnlyList<DoorView> Doors,
    [property: Id(8)] IReadOnlyList<PropView> Props,
    [property: Id(9)] string? Look,
    [property: Id(10)] IReadOnlyList<PathView>? Paths = null,
    [property: Id(11)] IReadOnlyList<TerraceView>? Terraces = null);

/// <summary>A raised stretch of ground: its middle (X, Z) in metres from the room's middle, its size and its height.</summary>
[GenerateSerializer, Immutable]
public sealed record TerraceView(
    [property: Id(0)] double X,
    [property: Id(1)] double Z,
    [property: Id(2)] double Width,
    [property: Id(3)] double Depth,
    [property: Id(4)] double Height,
    [property: Id(5)] string? Colour = null);

/// <summary>A narrow strip of floor out of a room's edge: its middle (X, Z) in metres from the room's middle, and its size.</summary>
[GenerateSerializer, Immutable]
public sealed record PathView(
    [property: Id(0)] double X,
    [property: Id(1)] double Z,
    [property: Id(2)] double Width,
    [property: Id(3)] double Depth);

/// <summary>
/// A player's home, as the browser draws it and its owner decorates it: whose it is, who may visit ("Friends", "Anyone"
/// or "Nobody"), the furniture in it, and its version (a room or a browser told of two changes keeps the newer).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record HomeView(
    [property: Id(0)] Guid OwnerId,
    [property: Id(1)] string OwnerName,
    [property: Id(2)] string Visitors,
    [property: Id(3)] IReadOnlyList<FurnitureView> Furniture,
    [property: Id(4)] int Version = 0)
{
    public static HomeView From(HomePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return new(
            plan.OwnerId, plan.OwnerName, plan.Visitors.ToString(), [.. plan.Furniture.Select(FurnitureView.From)], plan.Version);
    }

    /// <summary>The furniture as the room's decoration.</summary>
    public IReadOnlyList<PropView> Props() =>
    [
        .. Furniture.Select(piece =>
            PropView.From(Home.PropOf(piece.ItemId, new Position(piece.X, piece.Z), piece.QuarterTurns))),
    ];
}

/// <summary>
/// A piece of furniture in a home: its ID (to turn it or put it away), the shop item it is (which is also the kind of
/// prop it's drawn as) and its name, where it stands, the circle it takes up, and which way it faces in quarter turns.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record FurnitureView(
    [property: Id(0)] Guid Id,
    [property: Id(1)] string ItemId,
    [property: Id(2)] string Name,
    [property: Id(3)] double X,
    [property: Id(4)] double Z,
    [property: Id(5)] double Radius,
    [property: Id(6)] int QuarterTurns)
{
    public static FurnitureView From(PlacedFurniture piece)
    {
        ArgumentNullException.ThrowIfNull(piece);
        var item = ShopCatalog.Find(piece.ItemId);
        return new(piece.Id, piece.ItemId, item?.Name ?? piece.ItemId, piece.X, piece.Z, item?.Radius ?? 0, piece.QuarterTurns);
    }
}

/// <summary>Where a room's pond is, for drawing it and walking to it.</summary>
[GenerateSerializer, Immutable]
public sealed record PondView([property: Id(0)] double X, [property: Id(1)] double Z, [property: Id(2)] double Radius);

/// <summary>
/// A piece of the room's decoration, for drawing it: what it is (Kind), where it stands (X, Z), the circle round there it
/// is drawn inside (Radius), and which way it faces (Facing, radians: 0 faces the front of the room). Cats walk through it.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record PropView(
    [property: Id(0)] string Kind,
    [property: Id(1)] double X,
    [property: Id(2)] double Z,
    [property: Id(3)] double Radius,
    [property: Id(4)] double Facing)
{
    public static PropView From(Prop prop)
    {
        ArgumentNullException.ThrowIfNull(prop);
        return new PropView(prop.Kind, prop.Spot.X, prop.Spot.Z, prop.Radius, prop.Facing);
    }
}

/// <summary>
/// A door out of the room: a round spot on the floor (X, Z, Radius) to walk onto, and the room it leads to, for drawing
/// it and walking through it.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record DoorView(
    [property: Id(0)] string Id,
    [property: Id(1)] double X,
    [property: Id(2)] double Z,
    [property: Id(3)] double Radius,
    [property: Id(4)] string ToRoomId,
    [property: Id(5)] string ToRoomName,
    [property: Id(6)] string? Edge = null)
{
    public static DoorView From(Door door, RoomDefinition to)
    {
        ArgumentNullException.ThrowIfNull(door);
        ArgumentNullException.ThrowIfNull(to);
        return new DoorView(door.Id, door.Spot.X, door.Spot.Z, door.Radius, to.Id, to.Name, door.Edge?.ToString().ToLowerInvariant());
    }

    public static DoorView From(Door door, IRoomLibrary rooms)
    {
        ArgumentNullException.ThrowIfNull(door);
        var to = rooms.Find(door.ToRoom) ?? throw new InvalidOperationException($"No room '{door.ToRoom}'.");
        return new DoorView(door.Id, door.Spot.X, door.Spot.Z, door.Radius, to.Id, to.Name, door.Edge?.ToString().ToLowerInvariant());
    }
}

/// <summary>
/// Where a door a cat went through leads: a room, and the door in it the cat comes in by (none: its spawn point, as from
/// a home).
/// </summary>
[GenerateSerializer, Immutable]
public sealed record DoorTarget([property: Id(0)] string RoomId, [property: Id(1)] string? DoorId);

/// <summary>
/// How a reel went: "caught" with the fish and the coins it paid, "too-early" or "too-late" with nothing. TotalCoins is
/// the player's coins after a catch (filled in by the hub, which pays it), and null when nothing was caught.
/// </summary>
[GenerateSerializer, Immutable]
public sealed record ReelView(
    [property: Id(0)] string Outcome,
    [property: Id(1)] string? FishId,
    [property: Id(2)] string? FishName,
    [property: Id(3)] int Coins,
    [property: Id(4)] int? TotalCoins)
{
    public const string Caught = "caught";

    public static ReelView From(ReelResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var outcome = result.Outcome switch
        {
            ReelOutcome.Caught => Caught,
            ReelOutcome.TooEarly => "too-early",
            ReelOutcome.TooLate => "too-late",
            _ => throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Not a reel outcome."),
        };
        return new ReelView(outcome, result.Fish?.Id, result.Fish?.Name, result.Coins, null);
    }
}

/// <summary>A catch, shown to everyone in the room.</summary>
[GenerateSerializer, Immutable]
public sealed record CatchView(
    [property: Id(0)] Guid PlayerId, [property: Id(1)] string FishId, [property: Id(2)] string FishName, [property: Id(3)] int Coins);

/// <summary>A room the player can go to from here.</summary>
[GenerateSerializer, Immutable]
public sealed record RoomLink([property: Id(0)] string Id, [property: Id(1)] string Name);

public sealed record WorldSummary(string Id, string Name, int Population, int Capacity);

/// <summary>Raised by grains for anything the player did wrong; the hub shows the message.</summary>
[GenerateSerializer]
public sealed class GameRuleException(string message) : Exception(message);

/// <summary>One browser tab's session (its connection ID) of a player, as the pod holding it renews its lease.</summary>
[GenerateSerializer, Immutable]
public sealed record PlayerSession([property: Id(0)] Guid PlayerId, [property: Id(1)] string Session);

/// <summary>Where a player's cat is: a room of a world.</summary>
[GenerateSerializer, Immutable]
public sealed record PlayerPlace([property: Id(0)] string WorldId, [property: Id(1)] string RoomId);

/// <summary>
/// A player card: another cat, where the player looking stands with them (FriendState: "None", "RequestSent",
/// "RequestReceived" or "Friends"), the names of the hat and collar it wears, if any, whether its home would let the
/// player looking in now, and the hat and collar themselves (shop item IDs), for the card's picture of the cat. An admin
/// looking also gets the cat's standing (<see cref="CardModerationView"/>); anyone else, null.
/// </summary>
public sealed record PlayerCardView(
    Guid PlayerId, string DisplayName, string FurColor, string FriendState, string? HatName = null, string? CollarName = null,
    bool CanVisitHome = false, string? Hat = null, string? Collar = null, CardModerationView? Moderation = null);

/// <summary>Something in the shop (Kind: "Hat", "Collar" or "Fur"; a fur's colour in FurColor).</summary>
public sealed record ShopItemView(string Id, string Name, string Kind, int Price, string? FurColor)
{
    public static ShopItemView From(ShopItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new(item.Id, item.Name, item.Kind.ToString(), item.Price, item.Fur?.ToString());
    }
}

/// <summary>
/// The shop as one player sees it: everything in it, in the catalogue's order, the player's coins, what they have (item
/// IDs) and how many of each, what their cat wears, and how many of one piece of furniture a player can have.
/// </summary>
public sealed record ShopView(
    IReadOnlyList<ShopItemView> Items, int Coins, IReadOnlyList<string> Owned, string FurColor, string? Hat, string? Collar,
    IReadOnlyDictionary<string, int> Counts, int MaxOfEachFurniture)
{
    public static ShopView From(Wardrobe wardrobe)
    {
        ArgumentNullException.ThrowIfNull(wardrobe);
        return new(
            [.. ShopCatalog.All.Select(ShopItemView.From)],
            wardrobe.Coins,
            wardrobe.Owned,
            wardrobe.FurColor.ToString(),
            wardrobe.Hat,
            wardrobe.Collar,
            wardrobe.Counts,
            ShopCatalog.MaxOfEachFurniture);
    }
}

/// <summary>
/// One of the player's friends or friend requests (as <see cref="PlayerCardView"/>), and for a friend in a room, where:
/// null when they're offline (or between rooms).
/// </summary>
public sealed record FriendView(
    Guid PlayerId, string DisplayName, string FurColor, string FriendState, FriendPlaceView? Place, bool CanVisitHome = false);

/// <summary>
/// Where a friend is, with the names to show. In a home, which is in no world, the world is empty and HomeOwnerId says
/// whose it is.
/// </summary>
public sealed record FriendPlaceView(
    string WorldId, string WorldName, string RoomId, string RoomName, Guid? HomeOwnerId = null);

/// <summary>
/// A ban or mute as the browser shows it: why, and until when (null: for good). Who decided it isn't shown to the player.
/// </summary>
public sealed record SanctionView(string Reason, DateTimeOffset? Until)
{
    [return: NotNullIfNotNull(nameof(sanction))]
    public static SanctionView? From(Sanction? sanction) => sanction is null ? null : new(sanction.Reason, sanction.Until);
}

/// <summary>
/// The moderation part of a player card, for an admin only: whether the cat is an admin too (admins aren't moderated),
/// the ban and mute it is under now, and the networks it played from lately (null: none seen, or network bans off).
/// </summary>
public sealed record CardModerationView(
    bool IsAdmin, SanctionView? Ban, SanctionView? Mute, IReadOnlyList<NetworkView>? Networks = null)
{
    /// <summary>The networks as an admin sees them; null for none.</summary>
    public static IReadOnlyList<NetworkView>? NetworksOf(IReadOnlyList<RecentAddress> recent)
    {
        ArgumentNullException.ThrowIfNull(recent);
        return recent.Count == 0 ? null : [.. recent.Select(NetworkView.From)];
    }
}

/// <summary>
/// A network a cat played from lately, for an admin: Key (its keyed hash, which the admin bans it by) and Label (the hash's
/// first 8 hex digits: never the address), how many players were on it in the last 7 days (more than one: a shared
/// network, a school or a phone network maybe), when this cat last was, its ban in force, and whether that ban is this
/// cat's (BannedWithIt: lifted with it) or another cat's on the same network.
/// </summary>
public sealed record NetworkView(
    string Key, string Label, int Players, DateTimeOffset LastSeen, SanctionView? Ban, bool BannedWithIt = false)
{
    public static NetworkView From(RecentAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return new(address.AddressHash, address.Label, address.Players, address.LastSeen, SanctionView.From(address.Ban),
            address.BannedWithIt);
    }
}

/// <summary>A sign-up refused because the network is banned: what the player is told, and the ban (why, until when).</summary>
public sealed record BannedView(string Message, SanctionView Ban)
{
    public static BannedView From(Sanction ban)
    {
        ArgumentNullException.ThrowIfNull(ban);
        return new(ban.SaysBanned(), SanctionView.From(ban));
    }
}

/// <summary>
/// The signed-in player (/api/me): who they are, whether they are an admin, and the ban and mute they're under now (the
/// browser shows a banned player why, and a muted one why their chat is off).
/// </summary>
public sealed record MeView(
    Guid Id, string DisplayName, string FurColor, int Coins, bool IsGuest, string? Hat, string? Collar, int OutfitVersion,
    bool IsAdmin, SanctionView? Ban, SanctionView? Mute)
{
    public static MeView From(PlayerProfile player, bool isAdmin, Standing standing)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(standing);
        return new(player.Id, player.DisplayName, player.FurColor.ToString(), player.Coins, player.IsGuest, player.Hat,
            player.Collar, player.OutfitVersion, isAdmin, SanctionView.From(standing.Ban), SanctionView.From(standing.Mute));
    }
}

/// <summary>A ban or mute in force, for the admin page: why, since and until when, and which admin decided it.</summary>
public sealed record AdminSanctionView(string Reason, DateTimeOffset Since, DateTimeOffset? Until, Guid ById, string ByName);

/// <summary>
/// A player banned or muted now, on the admin page, with how many networks are banned with them now (Lift offers them).
/// </summary>
public sealed record SanctionedPlayerView(
    Guid PlayerId, string DisplayName, AdminSanctionView? Ban, AdminSanctionView? Mute, int NetworkBans = 0)
{
    public static SanctionedPlayerView From(SanctionedPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new(player.PlayerId, player.DisplayName, Of(player.Ban, player.BannedByName), Of(player.Mute, player.MutedByName),
            player.NetworkBans);
    }

    private static AdminSanctionView? Of(Sanction? sanction, string? byName) =>
        sanction is null ? null : new(sanction.Reason, sanction.Since, sanction.Until, sanction.By, byName ?? "");
}

/// <summary>
/// A line of the audit trail on the admin page. Kind: "Warned", "Muted", "Banned", "MuteLifted", "BanLifted", "Renamed",
/// "AddressBanned" or "AddressBanLifted" (those two with Address, the network's label: never the address).
/// </summary>
public sealed record ModerationLogView(
    Guid PlayerId, string PlayerName, string Kind, Guid ById, string ByName, DateTimeOffset At, string? Reason,
    DateTimeOffset? Until, string? Address = null)
{
    public static ModerationLogView From(ModerationLogLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new(line.PlayerId, line.PlayerName, line.Kind.ToString(), line.ByPlayerId, line.ByName, line.At, line.Reason,
            line.Until, line.Address);
    }
}

/// <summary>
/// A network banned now, on the admin page: Key (its keyed hash, to lift it by), Label (the hash's first 8 hex digits:
/// never the address), the banned cat it was banned with, how many players were on it in the last 7 days, why, since and
/// until when, and which admin banned it.
/// </summary>
public sealed record NetworkBanView(
    string Key, string Label, Guid PlayerId, string PlayerName, int Players, string Reason, DateTimeOffset Since,
    DateTimeOffset Until, string ByName)
{
    public static NetworkBanView From(AddressBanLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return new(line.AddressHash, line.Label, line.PlayerId, line.PlayerName, line.Players, line.Ban.Reason,
            line.Ban.Since, line.Ban.Until ?? line.Ban.Since, line.ByName);
    }
}

/// <summary>
/// The admin page: the open reports (oldest first), who is banned or muted now, what admins did lately (newest first),
/// and the networks banned now (newest first).
/// </summary>
public sealed record AdminOverviewView(
    IReadOnlyList<OpenReportView> Reports, IReadOnlyList<SanctionedPlayerView> Sanctioned, IReadOnlyList<ModerationLogView> Recent,
    IReadOnlyList<NetworkBanView> NetworkBans)
{
    public static AdminOverviewView From(ModerationOverview overview, IRoomLibrary rooms)
    {
        ArgumentNullException.ThrowIfNull(overview);
        return new(
            [.. overview.Reports.Select(report => OpenReportView.From(report, rooms))],
            [.. overview.Sanctioned.Select(SanctionedPlayerView.From)],
            [.. overview.Recent.Select(ModerationLogView.From)],
            [.. overview.AddressBans.Select(NetworkBanView.From)]);
    }
}

/// <summary>A line of a report's chat, as the room had it: who said it (their name then), what, and when.</summary>
public sealed record ReportLineView(Guid PlayerId, string DisplayName, string Text, DateTimeOffset At);

/// <summary>
/// An open report on the admin page: who reported whom, why (Reason: a ReportReason's name) and the note, where (Place,
/// to read: "Town Square, Tabby", or "A home"), when, and the room's last chat lines, oldest first.
/// </summary>
public sealed record OpenReportView(
    Guid Id, Guid ReporterId, string ReporterName, Guid ReportedId, string ReportedName, string Reason, string? Note,
    string Place, DateTimeOffset FiledAt, IReadOnlyList<ReportLineView> Context)
{
    public static OpenReportView From(OpenReport report, IRoomLibrary rooms)
    {
        ArgumentNullException.ThrowIfNull(report);
        return new(
            report.Id, report.ReporterId, report.ReporterName, report.ReportedId, report.ReportedName, report.Reason.ToString(),
            report.Note, PlaceOf(report.WorldId, report.Room, rooms), report.FiledAt,
            [.. report.Context.Select(line => new ReportLineView(line.PlayerId, line.DisplayName, line.Text, line.At))]);
    }

    /// <summary>Where a report was filed, to read: the room and the world, or a home (which is in no world).</summary>
    public static string PlaceOf(string worldId, string room, IRoomLibrary rooms)
    {
        ArgumentNullException.ThrowIfNull(room);
        var roomId = room;
        if (room.Contains('/', StringComparison.Ordinal))
        {
            if (RoomKey.HomeOwner(room) is not null)
            {
                return "A home";
            }

            roomId = RoomKey.Parse(room).RoomId;
        }

        var roomName = rooms.OfWorld(worldId).Find(roomId)?.Name ?? roomId;
        return WorldCatalog.Find(worldId) is { } world ? $"{roomName}, {world.Name}" : roomName;
    }
}

/// <summary>
/// An admin's action on a player, from the admin page (as the hub's Moderate takes it); with a lift, LiftNetworks lifts the
/// networks banned with them too (as the hub's LiftNetworkBans).
/// </summary>
public sealed record ModerateRequestView(string? Action, string? Reason, string? Length, bool LiftNetworks = false);

/// <summary>A warning from the admins the player hasn't seen yet: its ID (to say it was seen) and its words.</summary>
public sealed record WarningView(Guid Id, string Text);

/// <summary>A player this one has muted (their chat is hidden from this player's browser), for the list of mutes.</summary>
public sealed record MutedView(Guid PlayerId, string DisplayName, string FurColor)
{
    public static MutedView From(MutedPlayer muted)
    {
        ArgumentNullException.ThrowIfNull(muted);
        return new(muted.PlayerId, muted.DisplayName, muted.FurColor.ToString());
    }
}

/// <summary>A report sent: whether the player has the cat they reported muted now, and their mutes after.</summary>
public sealed record ReportReceipt(bool Muted, IReadOnlyList<MutedView> Mutes);

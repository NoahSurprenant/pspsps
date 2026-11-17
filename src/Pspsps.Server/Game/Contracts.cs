using Pspsps.Application.Homes;
using Pspsps.Application.Shop;
using Pspsps.Domain.Fishing;
using Pspsps.Domain.Homes;
using Pspsps.Domain.Rooms;
using Pspsps.Domain.Shop;

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

/// <summary>A cat did an emote (one of Emotes.All: "sit", "meow", "wave", "sleep").</summary>
[GenerateSerializer, Immutable]
public sealed record EmoteView([property: Id(0)] Guid PlayerId, [property: Id(1)] string Emote, [property: Id(2)] DateTimeOffset At)
{
    public static EmoteView From(CatEmote emote) => new(emote.PlayerId, emote.Emote, emote.At);
}

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
    [property: Id(11)] HomeView? Home = null);

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
    [property: Id(5)] string ToRoomName)
{
    public static DoorView From(Door door)
    {
        ArgumentNullException.ThrowIfNull(door);
        var to = RoomCatalog.Find(door.ToRoom) ?? throw new InvalidOperationException($"No room '{door.ToRoom}'.");
        return new DoorView(door.Id, door.Spot.X, door.Spot.Z, door.Radius, to.Id, to.Name);
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
/// player looking in now, and the hat and collar themselves (shop item IDs), for the card's picture of the cat.
/// </summary>
public sealed record PlayerCardView(
    Guid PlayerId, string DisplayName, string FurColor, string FriendState, string? HatName = null, string? CollarName = null,
    bool CanVisitHome = false, string? Hat = null, string? Collar = null);

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

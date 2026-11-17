using Pspsps.Domain.Fishing;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

// What grains take and return, and what the hub sends to browsers. Orleans copies these between pods.

/// <summary>Who a player is, as a room needs to know them.</summary>
[GenerateSerializer, Immutable]
public sealed record PlayerCard([property: Id(0)] Guid PlayerId, [property: Id(1)] string DisplayName, [property: Id(2)] string FurColor);

[GenerateSerializer, Immutable]
public sealed record CatView(
    [property: Id(0)] Guid PlayerId,
    [property: Id(1)] string DisplayName,
    [property: Id(2)] string FurColor,
    [property: Id(3)] double X,
    [property: Id(4)] double Z,
    [property: Id(5)] string? Pose)
{
    public static CatView From(CatInRoom cat) =>
        new(cat.PlayerId, cat.DisplayName, cat.FurColor.ToString(), cat.Position.X, cat.Position.Z, Emotes.NameOf(cat.Pose));
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
    [property: Id(9)] IReadOnlyList<DoorView> Doors);

/// <summary>Where a room's pond is, for drawing it and walking to it.</summary>
[GenerateSerializer, Immutable]
public sealed record PondView([property: Id(0)] double X, [property: Id(1)] double Z, [property: Id(2)] double Radius);

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

/// <summary>Where a door a cat went through leads: a room, and the door in it the cat comes in by.</summary>
[GenerateSerializer, Immutable]
public sealed record DoorTarget([property: Id(0)] string RoomId, [property: Id(1)] string DoorId);

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
/// A player card: another cat, and where the player looking stands with them (FriendState: "None", "RequestSent",
/// "RequestReceived" or "Friends"). What it wears comes with the shop.
/// </summary>
public sealed record PlayerCardView(Guid PlayerId, string DisplayName, string FurColor, string FriendState);

/// <summary>
/// One of the player's friends or friend requests (as <see cref="PlayerCardView"/>), and for a friend in a room, where:
/// null when they're offline (or between rooms).
/// </summary>
public sealed record FriendView(
    Guid PlayerId, string DisplayName, string FurColor, string FriendState, FriendPlaceView? Place);

/// <summary>Where a friend is, with the names to show.</summary>
public sealed record FriendPlaceView(string WorldId, string WorldName, string RoomId, string RoomName);

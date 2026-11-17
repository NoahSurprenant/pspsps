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
    [property: Id(4)] double Z)
{
    public static CatView From(CatInRoom cat) =>
        new(cat.PlayerId, cat.DisplayName, cat.FurColor.ToString(), cat.Position.X, cat.Position.Z);
}

[GenerateSerializer, Immutable]
public sealed record ChatView([property: Id(0)] Guid PlayerId, [property: Id(1)] string Text, [property: Id(2)] DateTimeOffset At)
{
    public static ChatView From(ChatLine line) => new(line.PlayerId, line.Text, line.At);
}

/// <summary>A cat did an emote (one of RoomState.Emotes: "sit", "meow", "wave", "sleep").</summary>
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
    [property: Id(7)] IReadOnlyList<RoomLink> Rooms);

/// <summary>A room the player can go to from here.</summary>
[GenerateSerializer, Immutable]
public sealed record RoomLink([property: Id(0)] string Id, [property: Id(1)] string Name);

public sealed record WorldSummary(string Id, string Name, int Population, int Capacity);

/// <summary>Raised by grains for anything the player did wrong; the hub shows the message.</summary>
[GenerateSerializer]
public sealed class GameRuleException(string message) : Exception(message);

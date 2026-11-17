namespace Pspsps.Domain.Friends;

/// <summary>A player's friendships by kind: friends, requests they sent, and requests waiting for them.</summary>
public readonly record struct FriendCounts(int Friends, int Sent, int Received);

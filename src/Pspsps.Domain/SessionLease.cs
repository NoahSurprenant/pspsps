namespace Pspsps.Domain;

/// <summary>
/// How long a session (a browser tab's connection) keeps its cat in a room and its place in a world without being
/// renewed. The pod holding the connection renews it every <see cref="RenewEvery"/>; when that pod dies without closing
/// its connections, nothing renews them and the room and the world drop them once the lease runs out.
/// </summary>
public static class SessionLease
{
    /// <summary>How often a live session is renewed.</summary>
    public static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(30);

    /// <summary>How long a lease lasts: three renewals, so one late or lost renewal doesn't drop a live cat.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(90);

    /// <summary>Whether a lease last renewed at <paramref name="renewedAt"/> has run out by <paramref name="now"/>.</summary>
    public static bool HasExpired(DateTimeOffset renewedAt, DateTimeOffset now) => now - renewedAt >= Duration;
}

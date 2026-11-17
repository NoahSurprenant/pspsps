namespace Pspsps.Domain;

/// <summary>
/// A session's (a browser tab's connection's) hold on its cat in a room or its place in a world, and when it was last
/// renewed. The pod holding the connection renews it every <see cref="RenewEvery"/>; when that pod dies without closing
/// its connections, nothing renews them, and the room and the world drop them once their leases run out.
/// </summary>
public readonly record struct SessionLease(string Session, DateTimeOffset RenewedAt)
{
    /// <summary>How often a live session is renewed.</summary>
    public static readonly TimeSpan RenewEvery = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long a lease lasts. Renewed every 30 s and lasting 100 s, a lease survives two missed renewals, with 10 s to
    /// spare for the third to arrive.
    /// </summary>
    public static readonly TimeSpan Duration = TimeSpan.FromSeconds(100);

    public static SessionLease Start(string session, DateTimeOffset now) => new(session, now);

    public SessionLease Renew(DateTimeOffset now) => this with { RenewedAt = now };

    /// <summary>Whether the lease has run out by <paramref name="now"/>: it is strictly older than <see cref="Duration"/>.</summary>
    public bool HasExpired(DateTimeOffset now) => now - RenewedAt > Duration;
}

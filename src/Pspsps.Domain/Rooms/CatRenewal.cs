namespace Pspsps.Domain.Rooms;

/// <summary>What renewing a session's lease on its cat in a room found.</summary>
public enum CatRenewal
{
    /// <summary>The session controls the cat; its lease starts again.</summary>
    Renewed,

    /// <summary>The cat is here, but another session (the player's newer tab) controls it now.</summary>
    TakenOver,

    /// <summary>The cat isn't here: its lease ran out, or the room came back empty after its pod died.</summary>
    Gone,
}

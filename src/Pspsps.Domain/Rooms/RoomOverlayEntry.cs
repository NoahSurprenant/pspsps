namespace Pspsps.Domain.Rooms;

/// <summary>A room's <see cref="RoomOverlay"/> as it is kept in the database.</summary>
public sealed class RoomOverlayEntry
{
    private RoomOverlayEntry()
    {
        RoomId = string.Empty;
        Id = string.Empty;
        Name = string.Empty;
    }

    public string RoomId { get; private set; }

    public string Id { get; private set; }

    public string Name { get; private set; }

    public DateTimeOffset Starts { get; private set; }

    public DateTimeOffset Ends { get; private set; }

    public bool Yearly { get; private set; }

    public string? Look { get; private set; }

    public IReadOnlyList<Prop> Adds { get; private set; } = [];

    public IReadOnlyList<Prop> Removes { get; private set; } = [];

    public static RoomOverlayEntry Of(RoomOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var entry = new RoomOverlayEntry();
        entry.Replace(overlay);
        return entry;
    }

    /// <summary>Changes what the overlay is, keeping its room and ID.</summary>
    public void Replace(RoomOverlay overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        if (RoomId.Length > 0 && (overlay.RoomId != RoomId || overlay.Id != Id))
        {
            throw new DomainException("An overlay stays the overlay of its room, with its ID.");
        }

        RoomId = overlay.RoomId;
        Id = overlay.Id;
        Name = overlay.Name;
        Starts = overlay.Window.Starts;
        Ends = overlay.Window.Ends;
        Yearly = overlay.Window.Yearly;
        Look = overlay.Look;
        Adds = overlay.Adds;
        Removes = overlay.Removes;
    }

    public RoomOverlay ToOverlay() =>
        new(Id, RoomId, Name, new OverlayWindow(Starts, Ends, Yearly)) { Adds = Adds, Removes = Removes, Look = Look };
}

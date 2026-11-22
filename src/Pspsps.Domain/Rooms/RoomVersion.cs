namespace Pspsps.Domain.Rooms;

public enum RoomVersionState
{
    /// <summary>Being worked on: players never see it.</summary>
    Draft,

    /// <summary>The room as players have it; a room has at most one.</summary>
    Published,

    /// <summary>Once published, since replaced; kept so the room can go back to it.</summary>
    Archived,
}

/// <summary>
/// One version of a room's layout. A room is what its published version says; to change it, a draft is made (from the
/// published version, or from nothing for a new room), edited, checked against every other room (<see cref="RoomBook"/>)
/// and published, which archives the version it replaces. Going back is publishing a copy of an archived version.
/// </summary>
public sealed class RoomVersion
{
    private RoomVersion()
    {
        RoomId = string.Empty;
        Layout = null!;
    }

    public Guid Id { get; private set; }

    public string RoomId { get; private set; }

    /// <summary>1 for a room's first version, and one more for each after it.</summary>
    public int Number { get; private set; }

    public RoomVersionState State { get; private set; }

    /// <summary>1 for a new draft, and one more with each edit: an admin saving over a draft someone else has since changed is refused.</summary>
    public int Revision { get; private set; }

    public RoomLayout Layout { get; private set; }

    /// <summary>
    /// The look the room has once this version is published (how it is painted and what is round it), or null for a version that
    /// leaves the room's look as it is. It is drafted, scheduled, published and rolled back with the layout.
    /// </summary>
    public RoomLook? Look { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PublishedAt { get; private set; }

    /// <summary>When a draft is to go live by itself; null for one that waits to be published by hand. Saving the draft again takes it off.</summary>
    public DateTimeOffset? GoLiveAt { get; private set; }

    /// <summary>The admin who scheduled it, who the publishing is put down to.</summary>
    public Guid? ScheduledBy { get; private set; }

    public static RoomVersion Draft(string roomId, int number, RoomLayout layout, Guid? createdBy, DateTimeOffset now, RoomLook? look = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(roomId);
        ArgumentNullException.ThrowIfNull(layout);
        if (number < 1)
        {
            throw new DomainException("A room's versions are numbered from 1.");
        }

        return new RoomVersion
        {
            Id = Guid.NewGuid(),
            RoomId = roomId,
            Number = number,
            State = RoomVersionState.Draft,
            Revision = 1,
            Layout = layout,
            Look = look,
            CreatedBy = createdBy,
            CreatedAt = now,
        };
    }

    /// <summary>Publishes a version straight away: for the rooms a new database starts with.</summary>
    public static RoomVersion Published(string roomId, RoomLayout layout, DateTimeOffset now)
    {
        var version = Draft(roomId, 1, layout, null, now);
        version.Publish(now);
        return version;
    }

    /// <summary>Keeps the layout, and the look when one is given (none given leaves the draft's look as it was).</summary>
    public void Edit(RoomLayout layout, RoomLook? look = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (State != RoomVersionState.Draft)
        {
            throw new DomainException("Only a draft can be edited: make a new draft from this version.");
        }

        Layout = layout;
        Look = look ?? Look;
        Revision++;
        GoLiveAt = null;
        ScheduledBy = null;
    }

    public void Schedule(DateTimeOffset at, Guid adminId, DateTimeOffset now)
    {
        if (State != RoomVersionState.Draft)
        {
            throw new DomainException("Only a draft can be scheduled.");
        }

        if (at <= now)
        {
            throw new DomainException("A draft goes live in the future: pick a later time, or publish it now.");
        }

        GoLiveAt = at;
        ScheduledBy = adminId;
    }

    public void Unschedule()
    {
        GoLiveAt = null;
        ScheduledBy = null;
    }

    public void Publish(DateTimeOffset now)
    {
        if (State != RoomVersionState.Draft)
        {
            throw new DomainException("Only a draft can be published: make a new draft from this version.");
        }

        State = RoomVersionState.Published;
        PublishedAt = now;
        Unschedule();
    }

    public void Archive()
    {
        if (State != RoomVersionState.Published)
        {
            throw new DomainException("Only the published version can be replaced.");
        }

        State = RoomVersionState.Archived;
    }

    /// <summary>A new draft of the room with this version's layout, to edit or to go back to.</summary>
    public RoomVersion DraftFrom(int number, Guid? createdBy, DateTimeOffset now) => Draft(RoomId, number, Layout, createdBy, now, Look);
}

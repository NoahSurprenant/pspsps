namespace Pspsps.Domain.Rooms;

/// <summary>One thing an admin did to the rooms: what, to which room or look, when and who; kept so changes can be traced.</summary>
public sealed class RoomAuditEntry
{
    private RoomAuditEntry()
    {
        Action = string.Empty;
        Target = string.Empty;
        Detail = string.Empty;
    }

    public Guid Id { get; private set; }

    public DateTimeOffset At { get; private set; }

    /// <summary>The admin; null once their account is gone.</summary>
    public Guid? AdminId { get; private set; }

    public string Action { get; private set; }

    /// <summary>The room, look or overlay it was done to.</summary>
    public string Target { get; private set; }

    public string Detail { get; private set; }

    /// <summary>The room as this change left its draft or version, so the history can show it and put it back; null for changes that are not to one room.</summary>
    public RoomContent? Content { get; private set; }

    public static RoomAuditEntry Of(Guid? adminId, string action, string target, string detail, DateTimeOffset at, RoomContent? content = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(detail);
        return new RoomAuditEntry { Id = Guid.NewGuid(), At = at, AdminId = adminId, Action = action, Target = target, Detail = detail, Content = content };
    }
}

/// <summary>A room's layout with the look that goes with it, as one change left them.</summary>
public sealed record RoomContent(RoomLayout Layout, RoomLook? Look);


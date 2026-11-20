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

    public static RoomAuditEntry Of(Guid adminId, string action, string target, string detail, DateTimeOffset at)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        ArgumentNullException.ThrowIfNull(detail);
        return new RoomAuditEntry { Id = Guid.NewGuid(), At = at, AdminId = adminId, Action = action, Target = target, Detail = detail };
    }
}

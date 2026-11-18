namespace Pspsps.Application.Moderation;

/// <summary>The admins: player IDs from the server's configuration, the only way to become one.</summary>
public interface IAdmins
{
    /// <summary>The admins' player IDs as configured now.</summary>
    IReadOnlySet<Guid> Current { get; }
}

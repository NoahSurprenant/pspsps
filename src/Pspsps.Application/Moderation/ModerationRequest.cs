using Pspsps.Domain;

namespace Pspsps.Application.Moderation;

/// <summary>What an admin can do to a player.</summary>
public enum ModerationAction
{
    Warn,
    Mute,
    Ban,
    Lift,
}

/// <summary>
/// An admin's action on a player, as asked: warn (the reason is the warning), mute for an hour, a day or a week, ban
/// for a day, a week or for good, or lift whatever ban and mute there are.
/// </summary>
public sealed record ModerationRequest(ModerationAction Action, string? Reason, TimeSpan? Length)
{
    /// <summary>How long a mute can be, by name.</summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan> MuteLengths = new Dictionary<string, TimeSpan>
    {
        ["1h"] = TimeSpan.FromHours(1),
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7),
    };

    /// <summary>How long a ban can be, by name ("permanent": for good).</summary>
    public static readonly IReadOnlyDictionary<string, TimeSpan?> BanLengths = new Dictionary<string, TimeSpan?>
    {
        ["24h"] = TimeSpan.FromHours(24),
        ["7d"] = TimeSpan.FromDays(7),
        ["permanent"] = null,
    };

    /// <summary>
    /// The request from what the browser sends: <paramref name="action"/> ("warn", "mute", "ban" or "lift"), the reason,
    /// and for a mute or a ban its length (one of <see cref="MuteLengths"/> or <see cref="BanLengths"/>).
    /// </summary>
    public static ModerationRequest Parse(string? action, string? reason, string? length)
    {
        // Letters only: Enum.TryParse also takes numbers ("7") and lists ("Mute, Ban").
        if (string.IsNullOrEmpty(action) || !action.All(char.IsAsciiLetter)
            || !Enum.TryParse<ModerationAction>(action, ignoreCase: true, out var what))
        {
            throw new DomainException("That isn't something an admin can do.");
        }

        return what switch
        {
            ModerationAction.Mute => new(what, reason, MuteLengths.TryGetValue(length ?? "", out var mute)
                ? mute
                : throw new DomainException("A mute lasts an hour, a day or a week.")),
            ModerationAction.Ban => BanLengths.TryGetValue(length ?? "", out var ban)
                ? new(what, reason, ban)
                : throw new DomainException("A ban lasts a day, a week or for good."),
            _ => new(what, reason, null),
        };
    }
}

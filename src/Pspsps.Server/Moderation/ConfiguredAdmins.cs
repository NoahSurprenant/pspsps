using System.Collections.Frozen;
using Microsoft.Extensions.Options;
using Pspsps.Application.Moderation;

namespace Pspsps.Server.Moderation;

/// <summary>The <c>Moderation</c> configuration section.</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";

    /// <summary>
    /// The admins, by player ID (the ID a player's own card shows under Details). The only way to become an admin. Text,
    /// not Guid: one that isn't an ID is logged and skipped, rather than failing the whole list.
    /// </summary>
    public string[] AdminPlayerIds { get; set; } = [];
}

/// <summary>The admins as configured now (<see cref="ModerationOptions"/>), read again whenever the configuration changes.</summary>
internal sealed partial class ConfiguredAdmins(IOptionsMonitor<ModerationOptions> options, ILogger<ConfiguredAdmins> logger)
    : IAdmins
{
    // The options last read, and their admins: parsed once per change.
    private Parsed? _parsed;

    public IReadOnlySet<Guid> Current
    {
        get
        {
            var now = options.CurrentValue;
            if (_parsed is { } parsed && ReferenceEquals(parsed.Options, now))
            {
                return parsed.Admins;
            }

            var admins = Parse(now.AdminPlayerIds, invalid => LogNotAnId(logger, invalid));
            _parsed = new Parsed(now, admins);
            return admins;
        }
    }

    /// <summary>The player IDs in <paramref name="ids"/>; anything else is passed to <paramref name="invalid"/>, and skipped.</summary>
    public static IReadOnlySet<Guid> Parse(IEnumerable<string?>? ids, Action<string> invalid)
    {
        ArgumentNullException.ThrowIfNull(invalid);
        var admins = new HashSet<Guid>();
        foreach (var id in ids ?? [])
        {
            if (Guid.TryParse(id?.Trim(), out var playerId) && playerId != Guid.Empty)
            {
                admins.Add(playerId);
            }
            else
            {
                invalid(id ?? "");
            }
        }

        return admins.ToFrozenSet();
    }

    private sealed record Parsed(ModerationOptions Options, IReadOnlySet<Guid> Admins);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Moderation:AdminPlayerIds has '{Value}', which isn't a player ID; skipped")]
    private static partial void LogNotAnId(ILogger logger, string value);
}

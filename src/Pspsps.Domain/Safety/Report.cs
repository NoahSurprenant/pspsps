namespace Pspsps.Domain.Safety;

/// <summary>
/// One player's report of another, for the moderation tools to review: why (<see cref="Reason"/>, and an optional
/// <see cref="Note"/>), where the reporter was (<see cref="WorldId"/>, <see cref="Room"/>), when, the reported player's
/// name then, and the room's last chat lines (<see cref="Context"/>) as the server had them: never what the reporter's
/// browser says was said. An aggregate of its own; it starts <see cref="ReportStatus.Open"/>.
/// </summary>
public sealed class Report
{
    /// <summary>The longest note a report can have.</summary>
    public const int MaxNoteLength = 300;

    /// <summary>How many of the room's last chat lines a report keeps.</summary>
    public const int MaxContextLines = 20;

    /// <summary>How many reports a player can file in <see cref="Window"/>.</summary>
    public const int MaxPerWindow = 10;

    /// <summary>The time the limits look back over: reports per player, and one open report per pair.</summary>
    public static TimeSpan Window { get; } = TimeSpan.FromHours(1);

    private readonly List<ReportedLine> _context = [];

    private Report()
    {
        ReportedName = null!;
        WorldId = null!;
        Room = null!;
    }

    private Report(
        Guid reporterId, Guid reportedId, string reportedName, ReportReason reason, string? note, string worldId, string room,
        DateTimeOffset now)
    {
        Id = Guid.CreateVersion7(now);
        ReporterId = reporterId;
        ReportedId = reportedId;
        ReportedName = reportedName;
        Reason = reason;
        Note = note;
        WorldId = worldId;
        Room = room;
        FiledAt = now;
        Status = ReportStatus.Open;
    }

    public Guid Id { get; private set; }

    /// <summary>Who reported.</summary>
    public Guid ReporterId { get; private set; }

    /// <summary>Who was reported.</summary>
    public Guid ReportedId { get; private set; }

    /// <summary>The reported player's name when they were reported (names can change).</summary>
    public string ReportedName { get; private set; }

    public ReportReason Reason { get; private set; }

    /// <summary>What the reporter added, if anything (at most <see cref="MaxNoteLength"/> characters).</summary>
    public string? Note { get; private set; }

    /// <summary>The world the reporter was in.</summary>
    public string WorldId { get; private set; }

    /// <summary>The room the reporter was in: its key ("tabby/town-square", or "homes/&lt;owner&gt;" for a home).</summary>
    public string Room { get; private set; }

    public DateTimeOffset FiledAt { get; private set; }

    public ReportStatus Status { get; private set; }

    /// <summary>The room's last chat lines when the report was filed, oldest first.</summary>
    public IReadOnlyList<ReportedLine> Context => _context.AsReadOnly();

    /// <summary>
    /// <paramref name="reporterId"/> reports <paramref name="reportedId"/> (called <paramref name="reportedName"/>) from
    /// the room <paramref name="room"/> of <paramref name="worldId"/>, with that room's recent chat (<paramref
    /// name="chat"/>, oldest first: the last <see cref="MaxContextLines"/> are kept). <paramref name="past"/> are the
    /// reporter's reports in the last <see cref="Window"/> (older ones are ignored): at most <see cref="MaxPerWindow"/>,
    /// and one still open about the same player. Not yourself, a reason from the list, and a note of at most
    /// <see cref="MaxNoteLength"/> characters (blank is none). Reports at the same moment can pass a limit by one, which
    /// is harmless. The room's chat is kept only when it can be about the reported cat: it is in that room
    /// (<paramref name="reportedIsHere"/>), or said one of its lines; a cat reported from elsewhere (its card opened from the
    /// friends list) is reported with no lines, rather than with what strangers said.
    /// </summary>
    public static Report File(
        Guid reporterId,
        Guid reportedId,
        string reportedName,
        ReportReason reason,
        string? note,
        string worldId,
        string room,
        IEnumerable<ChatContextLine> chat,
        bool reportedIsHere,
        IEnumerable<PastReport> past,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(past);
        if (reporterId == Guid.Empty || reportedId == Guid.Empty || string.IsNullOrWhiteSpace(reportedName))
        {
            throw new DomainException("That cat doesn't exist.");
        }

        if (reporterId == reportedId)
        {
            throw new DomainException("You can't report yourself.");
        }

        if (!Enum.IsDefined(reason))
        {
            throw new DomainException("Pick a reason.");
        }

        if (string.IsNullOrWhiteSpace(worldId) || string.IsNullOrWhiteSpace(room))
        {
            throw new DomainException("You can only report a cat from a room.");
        }

        var trimmed = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmed is { Length: > MaxNoteLength })
        {
            throw new DomainException($"A note is at most {MaxNoteLength} characters.");
        }

        var recent = past.Where(r => r.FiledAt > now - Window).ToList();
        if (recent.Any(r => r.ReportedId == reportedId && r.Status == ReportStatus.Open))
        {
            throw new DomainException("You've reported that cat already. Thanks: it will be looked at.");
        }

        if (recent.Count >= MaxPerWindow)
        {
            throw new DomainException("You've sent a lot of reports lately. Try again later.");
        }

        var report = new Report(reporterId, reportedId, reportedName, reason, trimmed, worldId, room, now);
        var lines = chat.ToList();
        if (!reportedIsHere && !lines.Any(l => l.PlayerId == reportedId))
        {
            lines.Clear();
        }

        var position = 0;
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - MaxContextLines)))
        {
            report._context.Add(new ReportedLine(position++, line.PlayerId, line.DisplayName, line.Text, line.At));
        }

        return report;
    }
}

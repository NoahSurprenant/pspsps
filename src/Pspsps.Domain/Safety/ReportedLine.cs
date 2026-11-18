namespace Pspsps.Domain.Safety;

/// <summary>
/// One line of a room's chat, as the room had it when a report was filed: who said it (their ID, and their name then),
/// what (after the chat filter), and when. Its <see cref="Position"/> keeps the lines in the order they were said. Part
/// of the <see cref="Report"/> aggregate, and never changed.
/// </summary>
public sealed class ReportedLine
{
    private ReportedLine()
    {
        DisplayName = null!;
        Text = null!;
    }

    internal ReportedLine(int position, Guid playerId, string displayName, string text, DateTimeOffset at)
    {
        Position = position;
        PlayerId = playerId;
        DisplayName = displayName;
        Text = text;
        At = at;
    }

    /// <summary>The line's place in the report's context: 0 for the oldest.</summary>
    public int Position { get; private set; }

    public Guid PlayerId { get; private set; }

    /// <summary>The speaker's name when the report was filed (names can change).</summary>
    public string DisplayName { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset At { get; private set; }
}

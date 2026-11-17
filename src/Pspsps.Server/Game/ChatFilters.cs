using Microsoft.Extensions.Options;
using Pspsps.Domain.Rooms;

namespace Pspsps.Server.Game;

/// <summary>The <c>ChatFilter</c> configuration section: word lists, each masking or refusing its words.</summary>
public sealed class ChatFilterOptions
{
    public const string SectionName = "ChatFilter";

    public ChatWordListOptions[] Lists { get; set; } = [];
}

public sealed class ChatWordListOptions
{
    public ChatFilterAction Action { get; set; } = ChatFilterAction.Mask;
    public string[] Words { get; set; } = [];
}

/// <summary>
/// The chat filter the rooms use now, built from configuration and rebuilt when it changes, so a new word list applies
/// to the next line said, without a restart.
/// </summary>
public sealed class ChatFilters(IOptionsMonitor<ChatFilterOptions> options)
{
    private Built? _built;

    public ChatFilter Current
    {
        get
        {
            // The monitor hands back the same options object until the configuration changes.
            var current = options.CurrentValue;
            var built = _built;
            if (built is not null && ReferenceEquals(built.From, current))
            {
                return built.Filter;
            }

            var filter = ChatFilter.Create(
                (current.Lists ?? []).Select(list => new ChatWordList(list.Action, list.Words ?? [])));
            _built = new Built(current, filter);
            return filter;
        }
    }

    private sealed record Built(ChatFilterOptions From, ChatFilter Filter);
}

public static class ChatFilterSetup
{
    /// <summary>
    /// The word lists' own file, relative to the content root. A file of its own so that a mounted ConfigMap (the
    /// chart's <c>chatFilter</c> values) replaces the shipped lists whole rather than merging into them.
    /// </summary>
    public static readonly string FilePath = Path.Combine("chat-filter", "chat-filter.json");

    /// <summary>Reads the word lists from <see cref="FilePath"/>, rereading it when it changes, and adds <see cref="ChatFilters"/>.</summary>
    public static IHostApplicationBuilder AddChatFilter(this IHostApplicationBuilder builder)
    {
        builder.Configuration.AddJsonFile(FilePath, optional: true, reloadOnChange: true);
        builder.Services.Configure<ChatFilterOptions>(builder.Configuration.GetSection(ChatFilterOptions.SectionName));
        builder.Services.AddSingleton<ChatFilters>();
        return builder;
    }
}

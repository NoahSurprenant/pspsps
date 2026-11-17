using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Primitives;
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
/// The word lists' file: where it is, and the last error reading it. Its configuration source reports errors here
/// instead of throwing, so a broken file stops neither the server nor a reload.
/// </summary>
public sealed class ChatFilterFile(string path)
{
    private string? _error;

    public string FullPath { get; } = path;

    public bool Exists => File.Exists(FullPath);

    internal void Failed(Exception error) => Volatile.Write(ref _error, error.Message);

    /// <summary>The error from reading the file since the last call, if any.</summary>
    internal string? TakeError() => Interlocked.Exchange(ref _error, null);
}

/// <summary>
/// The chat filter the rooms use now, built from configuration and rebuilt when it changes, so a new word list applies
/// to the next line said, without a restart. Never throws: when the lists can't be read (a value that doesn't bind, a
/// broken or missing file) it logs a warning and keeps the last good filter.
/// </summary>
public sealed partial class ChatFilters(IConfiguration configuration, ChatFilterFile file, ILogger<ChatFilters> logger)
{
    private readonly Lock _rebuilding = new();
    private Built? _built;

    public ChatFilter Current
    {
        get
        {
            var built = Volatile.Read(ref _built);
            if (built is not null && !built.Change.HasChanged)
            {
                return built.Filter;
            }

            lock (_rebuilding)
            {
                built = _built;
                if (built is null || built.Change.HasChanged)
                {
                    built = Build(built?.Filter);
                    Volatile.Write(ref _built, built);
                }

                return built.Filter;
            }
        }
    }

    private Built Build(ChatFilter? lastGood)
    {
        // The token first: a change while reading makes the next call read again.
        var change = configuration.GetReloadToken();
        var problem = file.TakeError() ?? (file.Exists ? null : "the file is missing");
        ChatFilter? read = null;
        try
        {
            var options = configuration.GetSection(ChatFilterOptions.SectionName).Get<ChatFilterOptions>() ?? new();
            read = ChatFilter.Create(
                (options.Lists ?? []).Select(list => new ChatWordList(list.Action, list.Words ?? [])));
        }
        catch (InvalidOperationException e)
        {
            // A value the options can't take, such as an action that doesn't exist.
            problem = e.Message;
        }

        if (problem is null && read is not null)
        {
            return new Built(change, read);
        }

        LogUnreadable(logger, file.FullPath, problem ?? "", lastGood is not null);
        return new Built(change, lastGood ?? read ?? ChatFilter.None);
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The chat filter's word lists ({Path}) couldn't be read: {Problem} Kept the last good filter: {Kept}.")]
    private static partial void LogUnreadable(ILogger logger, string path, string problem, bool kept);

    private sealed record Built(IChangeToken Change, ChatFilter Filter);
}

public static class ChatFilterSetup
{
    /// <summary>
    /// The word lists' own file, relative to the content root. A file of its own so that a mounted ConfigMap (the
    /// chart's <c>chatFilter</c> values) replaces the shipped lists whole rather than merging into them.
    /// </summary>
    public static readonly string FilePath = Path.Combine("chat-filter", "chat-filter.json");

    /// <summary>
    /// Reads the word lists from <see cref="FilePath"/>, rereading it when it changes, and adds <see cref="ChatFilters"/>.
    /// The file goes before the environment variables and command line, which still override it.
    /// </summary>
    public static IHostApplicationBuilder AddChatFilter(this IHostApplicationBuilder builder)
    {
        var file = new ChatFilterFile(Path.Combine(builder.Environment.ContentRootPath, FilePath));
        var source = new JsonConfigurationSource
        {
            Path = FilePath,
            Optional = true,
            ReloadOnChange = true,
            OnLoadException = context =>
            {
                file.Failed(context.Exception);
                context.Ignore = true;
            },
        };

        var sources = builder.Configuration.Sources;
        sources.Insert(OverridesStart(sources), source);
        builder.Services.AddSingleton(file);
        builder.Services.AddSingleton<ChatFilters>();
        return builder;
    }

    // Where the sources that override files start: the first environment variables or command line after the last file
    // (the host's own DOTNET_ and ASPNETCORE_ variables come before appsettings.json).
    private static int OverridesStart(IList<IConfigurationSource> sources)
    {
        var lastFile = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is FileConfigurationSource)
            {
                lastFile = i;
            }
        }

        for (var i = lastFile + 1; i < sources.Count; i++)
        {
            if (sources[i] is EnvironmentVariablesConfigurationSource or CommandLineConfigurationSource)
            {
                return i;
            }
        }

        return sources.Count;
    }
}

using System.Text.Json;
using Microsoft.Extensions.Configuration.CommandLine;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Configuration.Json;
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
    /// <summary>
    /// <c>Mask</c> or <c>Refuse</c>. Text, not the enum: the binder drops a list whose enum it can't convert without a
    /// word, which would quietly turn the list off. <see cref="ChatFilters"/> checks it.
    /// </summary>
    public string Action { get; set; } = nameof(ChatFilterAction.Mask);
    public string[] Words { get; set; } = [];
}

/// <summary>The word lists' file, as it is on disk now.</summary>
public sealed class ChatFilterFile(string path)
{
    private static readonly JsonDocumentOptions Json = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public string FullPath { get; } = path;

    /// <summary>What is wrong with the file (missing, unreadable, not JSON), or null when nothing is.</summary>
    public string? Problem()
    {
        try
        {
            if (!File.Exists(FullPath))
            {
                return "the file is missing.";
            }

            JsonDocument.Parse(File.ReadAllText(FullPath), Json).Dispose();
            return null;
        }
        catch (JsonException e)
        {
            return $"the file isn't valid JSON: {e.Message}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"the file can't be read: {e.Message}";
        }
    }
}

/// <summary>
/// The chat filter the rooms use now. Built when the server starts and rebuilt on every configuration change, so a new
/// word list applies to the next line said, without a restart. When the lists can't be used (an action that doesn't
/// exist, a missing or broken file) it logs a warning and keeps the filter it had; the next good load replaces it.
/// </summary>
public sealed partial class ChatFilters : IDisposable
{
    private readonly ChatFilterFile _file;
    private readonly ILogger<ChatFilters> _logger;
    private readonly Lock _rebuilding = new();
    private readonly IDisposable? _subscription;
    private ChatFilter? _current;

    public ChatFilters(IOptionsMonitor<ChatFilterOptions> options, ChatFilterFile file, ILogger<ChatFilters> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _file = file;
        _logger = logger;
        Rebuild(options.CurrentValue);
        _subscription = options.OnChange(Rebuild);
    }

    public ChatFilter Current => Volatile.Read(ref _current) ?? ChatFilter.None;

    public void Dispose() => _subscription?.Dispose();

    // Runs on the thread that reloaded the configuration: never throws, whatever the configuration holds.
    private void Rebuild(ChatFilterOptions options)
    {
        lock (_rebuilding)
        {
            var problem = _file.Problem();
            ChatFilter? read = null;
            if (Validate(options, out var lists) is { } invalid)
            {
                problem = invalid;
            }
            else
            {
                read = ChatFilter.Create(lists);
                foreach (var entry in read.Rejected)
                {
                    LogRejected(_logger, entry);
                }
            }

            if (problem is null)
            {
                Volatile.Write(ref _current, read);
                return;
            }

            // Nothing to keep yet (at startup): use whatever could be read, so environment variables still apply.
            LogUnusable(_logger, _file.FullPath, problem, _current is not null);
            Volatile.Write(ref _current, _current ?? read ?? ChatFilter.None);
        }
    }

    // The lists with their actions, or what is wrong with them.
    private static string? Validate(ChatFilterOptions options, out List<ChatWordList> lists)
    {
        lists = [];
        foreach (var list in options.Lists ?? [])
        {
            // By name only: Enum.TryParse would also take a number.
            var action = Enum.GetNames<ChatFilterAction>()
                .FirstOrDefault(known => string.Equals(known, list.Action?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (action is null)
            {
                return $"a list's Action is '{list.Action}'; it is {nameof(ChatFilterAction.Mask)} or {nameof(ChatFilterAction.Refuse)}.";
            }

            lists.Add(new ChatWordList(Enum.Parse<ChatFilterAction>(action), list.Words ?? []));
        }

        return null;
    }

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The chat filter's word lists ({Path}) can't be used: {Problem} Kept the filter it had: {Kept}.")]
    private static partial void LogUnusable(ILogger logger, string path, string problem, bool kept);

    [LoggerMessage(Level = LogLevel.Warning,
        Message = "The chat filter left out '{Entry}': one word per entry, letters only (and digits).")]
    private static partial void LogRejected(ILogger logger, string entry);
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
        var source = new JsonConfigurationSource
        {
            Path = FilePath,
            Optional = true,
            ReloadOnChange = true,
            // A broken file must not stop the server or throw out of a reload; ChatFilters checks the file itself.
            OnLoadException = context => context.Ignore = true,
        };

        var sources = builder.Configuration.Sources;
        sources.Insert(OverridesStart(sources), source);
        builder.Services.Configure<ChatFilterOptions>(builder.Configuration.GetSection(ChatFilterOptions.SectionName));
        builder.Services.AddSingleton(new ChatFilterFile(Path.Combine(builder.Environment.ContentRootPath, FilePath)));
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

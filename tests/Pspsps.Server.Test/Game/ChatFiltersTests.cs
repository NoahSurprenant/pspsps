using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pspsps.Server.Game;

namespace Pspsps.Server.Test.Game;

/// <summary>The chat filter as the server builds it: from chat-filter/chat-filter.json under a scratch content root.</summary>
public sealed class ChatFiltersTests
{
    private const string Darn = """{ "ChatFilter": { "Lists": [ { "Action": "Mask", "Words": [ "darn" ] } ] } }""";
    private const string Heck = """{ "ChatFilter": { "Lists": [ { "Action": "Mask", "Words": [ "heck" ] } ] } }""";

    private string _root = null!;
    private WebApplication? _app;
    private LogRecorder _logs = null!;

    [SetUp]
    public void MakeContentRoot()
    {
        _root = Directory.CreateTempSubdirectory("chat-filter-test-").FullName;
        _logs = new LogRecorder();
    }

    [TearDown]
    public async Task CleanUp()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            _app = null;
        }

        _logs.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Test]
    public void Current_FollowsTheFile_WithoutARestart()
    {
        WriteFile(Darn);
        var filters = Start();
        Assert.That(filters.Current.Apply("oh darn"), Is.EqualTo("oh ****"));
        var first = filters.Current;
        Assert.That(filters.Current, Is.SameAs(first), "built once per configuration");

        WriteFile(Heck);
        Reload();

        Assert.That(filters.Current.Apply("oh darn, heck"), Is.EqualTo("oh darn, ****"));
        Assert.That(_logs.Warnings, Is.Empty);
    }

    [Test]
    public void Current_AfterAnEditThatCannotBeRead_KeepsTheLastGoodFilterAndWarns()
    {
        WriteFile(Darn);
        var filters = Start();
        Assert.That(filters.Current.Apply("darn"), Is.EqualTo("****"));

        WriteFile("""{ "ChatFilter": { "Lists": [ { "Action": "Block", "Words": [ "heck" ] } ] } }""");
        Reload();

        Assert.That(() => filters.Current, Throws.Nothing);
        Assert.That(filters.Current.Apply("darn heck"), Is.EqualTo("**** heck"));
        Assert.That(_logs.Warnings, Is.Not.Empty);
    }

    [Test]
    public void Current_AfterTheFileBreaks_KeepsTheLastGoodFilterAndWarns()
    {
        WriteFile(Darn);
        var filters = Start();
        Assert.That(filters.Current.Apply("darn"), Is.EqualTo("****"));

        WriteFile("""{ "ChatFilter": { "Lists": [ """);
        Assert.That(Reload, Throws.Nothing);

        Assert.That(filters.Current.Apply("darn"), Is.EqualTo("****"));
        Assert.That(_logs.Warnings, Is.Not.Empty);
        Assert.That(_logs.Warnings, Has.All.Contain("chat-filter.json"));
    }

    [Test]
    public void Current_AfterTheFileGoesMissing_KeepsTheLastGoodFilterAndWarns()
    {
        WriteFile(Darn);
        var filters = Start();
        Assert.That(filters.Current.Apply("darn"), Is.EqualTo("****"));

        File.Delete(FilePath);
        Reload();

        Assert.That(filters.Current.Apply("darn"), Is.EqualTo("****"));
        Assert.That(_logs.Warnings, Is.Not.Empty);
        Assert.That(_logs.Warnings, Has.All.Contain("chat-filter.json"));
    }

    [Test]
    public void Current_WithNoFile_LetsEverythingThroughAndWarns()
    {
        var filters = Start();

        Assert.That(filters.Current.Apply("darn heck"), Is.EqualTo("darn heck"));
        Assert.That(_logs.Warnings, Has.Count.EqualTo(1));
        Assert.That(_logs.Warnings[0], Does.Contain("chat-filter.json"));
    }

    [Test]
    public void AddChatFilter_EnvironmentVariablesStillOverrideTheFile()
    {
        WriteFile(Darn);
        const string Variable = "ChatFilter__Lists__0__Words__0";
        Environment.SetEnvironmentVariable(Variable, "heck");
        try
        {
            var filters = Start();

            Assert.That(filters.Current.Apply("darn heck"), Is.EqualTo("darn ****"));
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, null);
        }
    }

    private string FilePath => Path.Combine(_root, "chat-filter", "chat-filter.json");

    // Whole or not at all, as a ConfigMap changes: the file watcher must never catch it half written.
    private void WriteFile(string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var draft = Path.Combine(_root, "draft.json");
        File.WriteAllText(draft, json);
        File.Move(draft, FilePath, overwrite: true);
    }

    private ChatFilters Start()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = _root,
            EnvironmentName = "Testing",
        });
        builder.Logging.ClearProviders().AddProvider(_logs);
        builder.AddChatFilter();
        _app = builder.Build();
        return _app.Services.GetRequiredService<ChatFilters>();
    }

    private void Reload() => ((IConfigurationRoot)_app!.Configuration).Reload();

    /// <summary>Every warning (and worse) logged, as text.</summary>
    private sealed class LogRecorder : ILoggerProvider, ILogger
    {
        private readonly List<string> _warnings = [];

        public IReadOnlyList<string> Warnings
        {
            get
            {
                lock (_warnings)
                {
                    return [.. _warnings];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => this;

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel))
            {
                lock (_warnings)
                {
                    _warnings.Add(formatter(state, exception));
                }
            }
        }

        public void Dispose()
        {
        }
    }
}

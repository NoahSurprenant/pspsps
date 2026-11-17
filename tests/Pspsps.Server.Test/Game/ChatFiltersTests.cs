using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pspsps.Domain;
using Pspsps.Server.Game;

namespace Pspsps.Server.Test.Game;

public sealed class ChatFiltersTests
{
    [Test]
    public void Current_FollowsTheConfiguration_WithoutARestart()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ChatFilter:Lists:0:Action"] = "Mask",
                ["ChatFilter:Lists:0:Words:0"] = "darn",
                ["ChatFilter:Lists:1:Action"] = "Refuse",
                ["ChatFilter:Lists:1:Words:0"] = "woof",
            })
            .Build();
        using var services = new ServiceCollection()
            .Configure<ChatFilterOptions>(configuration.GetSection(ChatFilterOptions.SectionName))
            .AddSingleton<ChatFilters>()
            .BuildServiceProvider();
        var filters = services.GetRequiredService<ChatFilters>();

        Assert.That(filters.Current.Apply("oh darn"), Is.EqualTo("oh ****"));
        Assert.Throws<DomainException>(() => filters.Current.Apply("woof"));
        var first = filters.Current;
        Assert.That(filters.Current, Is.SameAs(first), "built once per configuration");

        configuration["ChatFilter:Lists:0:Words:0"] = "heck";
        configuration.Reload();

        Assert.That(filters.Current.Apply("oh darn, heck"), Is.EqualTo("oh darn, ****"));
    }

    [Test]
    public void Current_WithNoLists_LetsEverythingThrough()
    {
        using var services = new ServiceCollection()
            .Configure<ChatFilterOptions>(new ConfigurationBuilder().Build())
            .AddSingleton<ChatFilters>()
            .BuildServiceProvider();

        Assert.That(services.GetRequiredService<ChatFilters>().Current.Apply("darn woof"), Is.EqualTo("darn woof"));
    }
}

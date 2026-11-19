using Microsoft.Extensions.Configuration;
using Orleans.Configuration;
using Pspsps.Server.Game;

namespace Pspsps.Server.Test.Game;

public sealed class OrleansSetupTests
{
    private const string Redis = "redis:6379";

    [TestCase(null, null, null, ClusteringMode.Localhost)]
    [TestCase(null, Redis, null, ClusteringMode.Redis)]
    [TestCase(null, Redis, "pspsps-7d9f-abcde", ClusteringMode.Kubernetes)]
    [TestCase("Redis", Redis, "pspsps-7d9f-abcde", ClusteringMode.Redis)]
    [TestCase("redis", Redis, null, ClusteringMode.Redis)]
    [TestCase("Kubernetes", Redis, null, ClusteringMode.Kubernetes)]
    [TestCase("Localhost", Redis, "pspsps-7d9f-abcde", ClusteringMode.Localhost)]
    [TestCase("Localhost", null, null, ClusteringMode.Localhost)]
    [TestCase("  ", null, null, ClusteringMode.Localhost)]
    public void TheClustering_IsTheOneAskedFor_ElseWhatTheSettingsImply(
        string? asked, string? redis, string? podName, ClusteringMode expected)
    {
        Assert.That(OrleansSetup.ModeFrom(Settings(asked, redis, podName)), Is.EqualTo(expected));
    }

    [TestCase("Redis")]
    [TestCase("Kubernetes")]
    public void RedisOrKubernetes_WithoutRedis_IsRefused(string asked)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => OrleansSetup.ModeFrom(Settings(asked, null, null)));
        Assert.That(refused!.Message, Does.Contain("ConnectionStrings:Redis"));
    }

    [TestCase("Consul")]
    [TestCase("1")]
    public void AnUnknownClustering_IsRefused(string asked)
    {
        var refused = Assert.Throws<InvalidOperationException>(() => OrleansSetup.ModeFrom(Settings(asked, Redis, null)));
        Assert.That(refused!.Message, Does.Contain("Localhost, Redis or Kubernetes"));
    }

    [Test]
    public void TheSetting_IsOutsideOrleansOwnSection()
    {
        // Orleans reads "Orleans:Clustering" itself, as the name of a clustering provider to load: a pod set to
        // "Orleans:Clustering=Redis" stopped at startup ("Could not find Clustering provider named 'Default'").
        Assert.That(OrleansSetup.ClusteringKey, Does.Not.StartWith("Orleans:"));
    }

    [Test]
    public void GrainVersions_MixAsTheOrleansDocsAdviseForARollingUpgrade()
    {
        var options = new GrainVersioningOptions();

        OrleansSetup.ConfigureVersioning(options);

        Assert.Multiple(() =>
        {
            Assert.That(options.DefaultCompatibilityStrategy, Is.EqualTo("BackwardCompatible"));
            Assert.That(options.DefaultVersionSelectorStrategy, Is.EqualTo("AllCompatibleVersions"));
        });
    }

    private static IConfiguration Settings(string? asked, string? redis, string? podName) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [OrleansSetup.ClusteringKey] = asked,
            ["ConnectionStrings:Redis"] = redis,
            ["POD_NAME"] = podName,
        }).Build();
}

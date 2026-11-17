using Pspsps.Server.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Pspsps.Server.Test.Telemetry;

[TestFixture]
public class BackgroundClientSpanSamplerTests
{
    [TestCase(ActivityKind.Client, SamplingDecision.Drop)]
    [TestCase(ActivityKind.Server, SamplingDecision.RecordAndSample)]
    [TestCase(ActivityKind.Internal, SamplingDecision.RecordAndSample)]
    [TestCase(ActivityKind.Consumer, SamplingDecision.RecordAndSample)]
    public void Root(ActivityKind kind, SamplingDecision expected)
    {
        var parameters = new SamplingParameters(default, ActivityTraceId.CreateRandom(), "span", kind);

        Assert.That(new BackgroundClientSpanSampler().ShouldSample(parameters).Decision, Is.EqualTo(expected));
    }

    /// <summary>
    /// As the app wires it: a database call with no parent (the key ring refresh) is dropped, the
    /// same call inside a hub call or request is kept, and so is the hub call itself.
    /// </summary>
    [Test]
    public void ParentBased_KeepsClientSpansInsideATrace()
    {
        const string sourceName = "Pspsps.Server.Test.Sampler";
        var exported = new List<Activity>();
        using var source = new ActivitySource(sourceName);
        using var provider = Sdk.CreateTracerProviderBuilder()
            .SetSampler(new ParentBasedSampler(new BackgroundClientSpanSampler()))
            .AddSource(sourceName)
            .AddInMemoryExporter(exported)
            .Build();

        using (source.StartActivity("poll", ActivityKind.Client))
        {
        }
        using (source.StartActivity("GameHub/Move", ActivityKind.Internal))
        {
            using (source.StartActivity("select", ActivityKind.Client))
            {
            }
        }

        Assert.That(exported.Select(a => a.DisplayName), Is.EquivalentTo(new[] { "select", "GameHub/Move" }));
    }

    [TestCase(ActivityKind.Server)]
    [TestCase(ActivityKind.Internal)]
    public void Roots_AreKeptAtTheRatio(ActivityKind kind)
    {
        var parameters = new SamplingParameters(default, ActivityTraceId.CreateRandom(), "GameHub/Move", kind);

        Assert.Multiple(() =>
        {
            Assert.That(new BackgroundClientSpanSampler(0).ShouldSample(parameters).Decision, Is.EqualTo(SamplingDecision.Drop));
            Assert.That(new BackgroundClientSpanSampler(1).ShouldSample(parameters).Decision, Is.EqualTo(SamplingDecision.RecordAndSample));
        });
    }

    [Test]
    public void ClientRoots_AreDroppedWhateverTheRatio()
    {
        var parameters = new SamplingParameters(default, ActivityTraceId.CreateRandom(), "select", ActivityKind.Client);

        Assert.That(new BackgroundClientSpanSampler(1).ShouldSample(parameters).Decision, Is.EqualTo(SamplingDecision.Drop));
    }
}

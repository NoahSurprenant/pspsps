using System.Collections.Immutable;
using System.Net;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using NSubstitute;
using Orleans.Runtime;
using Pspsps.Server.Telemetry;

namespace Pspsps.Server.Test.Telemetry;

public sealed class SiloHealthCheckTests
{
    private static readonly SiloAddress Here = SiloAddress.New(IPAddress.Loopback, 11111, 1);

    [TestCase(SiloStatus.Active, HealthStatus.Healthy)]
    [TestCase(SiloStatus.Joining, HealthStatus.Unhealthy)]
    [TestCase(SiloStatus.ShuttingDown, HealthStatus.Unhealthy)]
    [TestCase(SiloStatus.Dead, HealthStatus.Unhealthy)]
    public async Task APod_IsReady_OnlyWhileItsSiloIsActiveInTheCluster(SiloStatus status, HealthStatus expected)
    {
        var result = await Check(status).CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(expected));
    }

    [Test]
    public async Task ASiloTheMembershipDoesntKnowYet_IsntReady()
    {
        var membership = Substitute.For<IClusterMembershipService>();
        membership.CurrentSnapshot.Returns(new ClusterMembershipSnapshot(
            ImmutableDictionary<SiloAddress, ClusterMember>.Empty, new MembershipVersion(1)));
        var silo = Substitute.For<ILocalSiloDetails>();
        silo.SiloAddress.Returns(Here);

        var result = await new SiloHealthCheck(membership, silo).CheckHealthAsync(new HealthCheckContext());

        Assert.That(result.Status, Is.EqualTo(HealthStatus.Unhealthy));
    }

    private static SiloHealthCheck Check(SiloStatus status)
    {
        var membership = Substitute.For<IClusterMembershipService>();
        membership.CurrentSnapshot.Returns(new ClusterMembershipSnapshot(
            ImmutableDictionary<SiloAddress, ClusterMember>.Empty.Add(Here, new ClusterMember(Here, status, "pod-a")),
            new MembershipVersion(1)));
        var silo = Substitute.For<ILocalSiloDetails>();
        silo.SiloAddress.Returns(Here);
        return new SiloHealthCheck(membership, silo);
    }
}

using Microsoft.Extensions.Diagnostics.HealthChecks;
using Orleans.Runtime;

namespace Pspsps.Server.Telemetry;

/// <summary>
/// Ready only while this pod's Orleans silo is an active member of the cluster, as the membership table has it: not yet
/// while it joins, no more once it is shutting down or the others declared it dead. A pod that isn't in the cluster can't
/// reach the grains, so it takes no players.
/// </summary>
public sealed class SiloHealthCheck(IClusterMembershipService membership, ILocalSiloDetails silo) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var status = membership.CurrentSnapshot.GetSiloStatus(silo.SiloAddress);
        return Task.FromResult(status == SiloStatus.Active
            ? HealthCheckResult.Healthy()
            : HealthCheckResult.Unhealthy($"The silo is {status}, not active in the cluster."));
    }
}

public static class SiloHealthCheckExtensions
{
    /// <summary>The silo's membership as a readiness check ("orleans"): only for a server with an Orleans silo.</summary>
    public static IHealthChecksBuilder AddSiloHealthCheck(this IHealthChecksBuilder checks) =>
        checks.AddCheck<SiloHealthCheck>("orleans", HealthStatus.Unhealthy, [HealthCheckExtensions.ReadyTag]);
}

using Orleans.Configuration;
using Orleans.Versions.Compatibility;
using Orleans.Versions.Selector;

namespace Pspsps.Server.Game;

/// <summary>How the server's silo finds the others (<c>Cluster:Mode</c>).</summary>
public enum ClusteringMode
{
    /// <summary>A single local silo, alone: development, the tests, and the chart's one pod.</summary>
    Localhost,

    /// <summary>Silos joined through Redis's membership table, anywhere (the two-pod run in CI: plain containers).</summary>
    Redis,

    /// <summary>
    /// Redis, plus Orleans' Kubernetes hosting: the silo's name and address from its pod, its IDs from the pod's
    /// orleans/* labels, and silos whose pod is gone declared dead. Needs the pod's service account to read the pods.
    /// </summary>
    Kubernetes,
}

/// <summary>The server's Orleans silo: how it clusters, and how grain interface versions mix during a rolling update.</summary>
public static class OrleansSetup
{
    /// <summary>Localhost, Redis or Kubernetes. Unset: Localhost without Redis, else Kubernetes in a pod (POD_NAME), else Redis.</summary>
    public const string ClusteringKey = "Cluster:Mode";

    /// <summary>The service and cluster IDs outside Kubernetes (where the pod's orleans/* labels give them).</summary>
    public const string ServiceIdKey = "Orleans:ServiceId";

    public const string ClusterIdKey = "Orleans:ClusterId";

    public const string DefaultId = "pspsps";

    /// <summary>
    /// The clustering asked for (<see cref="ClusteringKey"/>), or the one the settings imply. Refuses a name it doesn't
    /// know, and Redis or Kubernetes without <c>ConnectionStrings:Redis</c>.
    /// </summary>
    public static ClusteringMode ModeFrom(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var redis = configuration.GetConnectionString("Redis");
        var asked = configuration[ClusteringKey];
        if (string.IsNullOrWhiteSpace(asked))
        {
            if (string.IsNullOrEmpty(redis))
            {
                return ClusteringMode.Localhost;
            }

            return string.IsNullOrEmpty(configuration["POD_NAME"]) ? ClusteringMode.Redis : ClusteringMode.Kubernetes;
        }

        var name = Enum.GetNames<ClusteringMode>().FirstOrDefault(n => n.Equals(asked.Trim(), StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"{ClusteringKey} is '{asked}': set Localhost, Redis or Kubernetes.");
        var mode = Enum.Parse<ClusteringMode>(name);
        if (mode != ClusteringMode.Localhost && string.IsNullOrEmpty(redis))
        {
            throw new InvalidOperationException($"{ClusteringKey} is {mode}, which needs ConnectionStrings:Redis.");
        }

        return mode;
    }

    /// <summary>
    /// The rolling upgrade settings of Orleans' grain versioning (Orleans docs, "Deploy new versions of grains"): a call
    /// made by an older interface version may run on an activation of a newer one (backward compatible), and a new
    /// activation goes to any silo with a compatible version. A grain interface's [Version] goes up with every change to
    /// it, and changes are add-only (docs/architecture.md, "Changing what pods say to each other").
    /// </summary>
    public static void ConfigureVersioning(GrainVersioningOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        // Already GrainVersioningOptions' own defaults: set here on purpose, so an Orleans upgrade that ever changes
        // its default doesn't quietly change how a rolling update of this server mixes grain interface versions.
        options.DefaultCompatibilityStrategy = nameof(BackwardCompatible);
        options.DefaultVersionSelectorStrategy = nameof(AllCompatibleVersions);
    }

    /// <summary>The silo for <paramref name="mode"/>.</summary>
    public static void Configure(ISiloBuilder silo, ClusteringMode mode, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(silo);
        ArgumentNullException.ThrowIfNull(configuration);
        // A hub call's trace follows it into the grains it calls, on whichever pod they live.
        silo.AddActivityPropagation();
        silo.Configure<GrainVersioningOptions>(ConfigureVersioning);
        if (mode == ClusteringMode.Localhost)
        {
            silo.UseLocalhostClustering();
            return;
        }

        // Kubernetes hosting, added after, takes the IDs from the pod's labels instead.
        silo.Configure<ClusterOptions>(options =>
        {
            options.ServiceId = configuration[ServiceIdKey] is { Length: > 0 } serviceId ? serviceId : DefaultId;
            options.ClusterId = configuration[ClusterIdKey] is { Length: > 0 } clusterId ? clusterId : DefaultId;
        });
        silo.UseRedisClustering(configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException($"Orleans clustering {mode} needs ConnectionStrings:Redis."));
        if (mode == ClusteringMode.Kubernetes)
        {
            silo.UseKubernetesHosting();
        }
    }
}

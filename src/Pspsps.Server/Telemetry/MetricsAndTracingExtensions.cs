using Npgsql;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Pspsps.Server.Telemetry;

public static class MetricsAndTracingExtensions
{
    /// <summary>Meters beyond the instrumentation packages' own.</summary>
    public static readonly string[] AdditionalMeters =
    [
        "Microsoft.EntityFrameworkCore",
        // dotnet.health_check.*, from AddTelemetryHealthCheckPublisher.
        "Microsoft.Extensions.Diagnostics.HealthChecks",
        "Microsoft.AspNetCore.Authentication",
        "Microsoft.AspNetCore.Authorization",
        // Grains, messaging, membership and the rest of the actor runtime.
        "Microsoft.Orleans",
        // pspsps.*: players in worlds, active rooms, chat, hub calls.
        GameMetrics.MeterName,
    ];

    /// <summary>
    /// Metrics and traces, pushed over OTLP to the collector when an endpoint is set; nothing is
    /// scraped from the app. Health probes are left out of the traces.
    /// </summary>
    public static IServiceCollection AddMetricsAndTracing(this IServiceCollection services, TelemetryOptions telemetry, IConfiguration configuration)
    {
        var healthPaths = HealthCheckPaths.FromConfiguration(configuration);
        services.AddOpenTelemetry()
            .ConfigureResource(resource => telemetry.ConfigureResource(resource))
            .WithMetrics(metrics =>
            {
                metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddProcessInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddMeter(AdditionalMeters);

                if (telemetry.SignalEndpoint("metrics") is { } endpoint)
                {
                    metrics.AddOtlpExporter((exporter, reader) =>
                    {
                        ConfigureExporter(exporter, endpoint, telemetry.Protocol);
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds = (int)telemetry.MetricExportInterval.TotalMilliseconds;
                    });
                }
            })
            .WithTracing(tracing =>
            {
                tracing
                    .SetSampler(new ParentBasedSampler(new BackgroundClientSpanSampler(RootSampleRatio(configuration))))
                    .AddAspNetCoreInstrumentation(options => options.Filter = context => !healthPaths.IsHealthPath(context.Request.Path))
                    // Discord and Google sign-in, and the Kubernetes API (cluster mode).
                    .AddHttpClientInstrumentation()
                    .AddNpgsql()
                    .AddEntityFrameworkCoreInstrumentation()
                    // Grain calls (silo.AddActivityPropagation): a hub call's trace follows it into the grains, on any pod.
                    .AddSource(OrleansActivitySources)
                    // One span per hub method call, with the grain calls it makes inside.
                    .AddSource("Microsoft.AspNetCore.SignalR.Server");

                if (telemetry.SignalEndpoint("traces") is { } endpoint)
                {
                    tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, telemetry.Protocol));
                }

                if (configuration.GetValue<bool>("OTLP_CONSOLE_EXPORTER_ENABLED"))
                {
                    tracing.AddConsoleExporter();
                }
            });

        return services;
    }

    /// <summary>
    /// The share of root spans kept (hub calls, requests that came without a trace context): the standard
    /// <c>OTEL_TRACES_SAMPLER_ARG</c>, 0 to 1, default 0.1.
    /// </summary>
    public static double RootSampleRatio(IConfiguration configuration) =>
        double.TryParse(configuration["OTEL_TRACES_SAMPLER_ARG"], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var ratio) && ratio is >= 0 and <= 1
            ? ratio
            : DefaultRootSampleRatio;

    public const double DefaultRootSampleRatio = 0.1;

    /// <summary>Orleans' tracing sources.</summary>
    public static readonly string[] OrleansActivitySources = ["Microsoft.Orleans.Application", "Microsoft.Orleans.Runtime"];

    public static void ConfigureExporter(OtlpExporterOptions exporter, Uri endpoint, OtlpWireProtocol protocol)
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = protocol == OtlpWireProtocol.Grpc ? OtlpExportProtocol.Grpc : OtlpExportProtocol.HttpProtobuf;
    }
}

using OpenTelemetry.Trace;
using System.Diagnostics;

namespace Pspsps.Server.Telemetry;

/// <summary>
/// The root sampler. Drops client spans that start a trace: database and HTTP calls made outside any request or hub
/// call (the key ring refresh, the health check publisher), each of which would otherwise be its own one-span trace.
/// Other roots are kept at <c>rootRatio</c>: every hub call is a root (SignalR gives them no parent), so keeping them
/// all would make a trace of every step a cat takes. Calls with a parent follow it.
/// </summary>
/// <remarks>Used as the root sampler of a <see cref="ParentBasedSampler"/>, so it only sees roots.</remarks>
public sealed class BackgroundClientSpanSampler(double rootRatio = 1.0) : Sampler
{
    private readonly TraceIdRatioBasedSampler _roots = new(Math.Clamp(rootRatio, 0, 1));

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.Kind == ActivityKind.Client
            ? new SamplingResult(SamplingDecision.Drop)
            : _roots.ShouldSample(samplingParameters);
}

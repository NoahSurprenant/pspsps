using System.Diagnostics.Metrics;

namespace Pspsps.Server.Telemetry.Browser;

/// <summary>
/// The browsers' numbers as the game's metrics: the Web Vitals (pspsps.browser.lcp, .inp, .fcp and .ttfb in seconds,
/// pspsps.browser.cls unitless), the frame rate in a room (pspsps.browser.fps, a median over ten seconds) and long tasks
/// (pspsps.browser.long_task.duration, seconds). Over the whole game, with no label a browser sets: anyone may send a
/// batch, and each new label value would be a new series.
/// </summary>
public sealed class BrowserMetrics
{
    public const string MeterName = "Pspsps.Browser";

    private static readonly double[] SecondsBuckets = [0.05, 0.1, 0.2, 0.3, 0.5, 0.75, 1, 1.5, 2, 2.5, 3, 4, 5, 7.5, 10];
    private static readonly double[] ShiftBuckets = [0.01, 0.025, 0.05, 0.1, 0.15, 0.25, 0.5, 1];
    private static readonly double[] FpsBuckets = [5, 10, 15, 20, 24, 30, 40, 50, 55, 60, 75, 90, 120, 144];
    private static readonly double[] LongTaskBuckets = [0.05, 0.075, 0.1, 0.15, 0.2, 0.3, 0.5, 1, 2, 5];

    // Each number's histogram, and what its value is divided by to be in the histogram's unit (ms to s).
    private readonly Dictionary<string, (Histogram<double> Histogram, double Divisor)> _histograms;

    public BrowserMetrics(IMeterFactory meters)
    {
        ArgumentNullException.ThrowIfNull(meters);
        var meter = meters.Create(MeterName);

        Histogram<double> Create(string name, string? unit, string description, double[] buckets) =>
            meter.CreateHistogram(name, unit, description, advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = buckets });

        const double MsPerSecond = 1000;
        _histograms = new(StringComparer.Ordinal)
        {
            ["LCP"] = (Create("pspsps.browser.lcp", "s", "Largest Contentful Paint", SecondsBuckets), MsPerSecond),
            ["INP"] = (Create("pspsps.browser.inp", "s", "Interaction to Next Paint", SecondsBuckets), MsPerSecond),
            ["FCP"] = (Create("pspsps.browser.fcp", "s", "First Contentful Paint", SecondsBuckets), MsPerSecond),
            ["TTFB"] = (Create("pspsps.browser.ttfb", "s", "Time to First Byte", SecondsBuckets), MsPerSecond),
            ["CLS"] = (Create("pspsps.browser.cls", null, "Cumulative Layout Shift", ShiftBuckets), 1),
            ["FPS"] = (Create("pspsps.browser.fps", "{frame}/s", "Frames per second in a room: the median over ten seconds", FpsBuckets), 1),
            ["LONG_TASK"] = (Create("pspsps.browser.long_task.duration", "s", "Long tasks: the main thread busy 50 ms or more", LongTaskBuckets), MsPerSecond),
        };
    }

    public void Record(IEnumerable<ClientVital> vitals)
    {
        ArgumentNullException.ThrowIfNull(vitals);
        foreach (var vital in vitals)
        {
            if (_histograms.TryGetValue(vital.Name, out var instrument))
                instrument.Histogram.Record(vital.Value / instrument.Divisor);
        }
    }
}

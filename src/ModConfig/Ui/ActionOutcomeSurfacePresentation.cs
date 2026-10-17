using System;
using System.Collections.Generic;
using System.Globalization;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

namespace OrbModConfig;

internal enum ActionOutcomeServiceColor
{
    Leaf = 0,
    Amber = 1,
    Sky = 2,
    Violet = 3,
    Cyan = 4,
    Orange = 5,
    Rose = 6,
    Teal = 7,
}

internal static class ActionOutcomeTimelineServicePolicy
{
    private const string MentorServiceId = "orbmentor.mastery-sharing";

    internal static bool Includes(ServiceId service, ServiceShape shape) =>
        shape == ServiceShape.Ordinary &&
        !string.Equals(service.Value, MentorServiceId, StringComparison.Ordinal) &&
        service != StrategistServicePolicies.ServiceId;
}

internal readonly struct ActionOutcomeStackPresentation
{
    internal ActionOutcomeStackPresentation(
        ServiceId service,
        ActionOutcomeServiceColor color,
        long committed)
    {
        Service = service;
        Color = color;
        Committed = committed;
    }

    internal ServiceId Service { get; }
    internal ActionOutcomeServiceColor Color { get; }
    internal long Committed { get; }
}

internal readonly struct ActionOutcomeBucketPresentation
{
    internal ActionOutcomeBucketPresentation(
        long minuteKey,
        ActionOutcomeStackPresentation[] stacks,
        ActionOutcomeServiceDetailPresentation[] details,
        long committed,
        bool hasFault)
    {
        MinuteKey = minuteKey;
        Stacks = stacks ?? throw new ArgumentNullException(nameof(stacks));
        Details = details ?? throw new ArgumentNullException(nameof(details));
        Committed = committed;
        HasFault = hasFault;
    }

    internal long MinuteKey { get; }
    internal ActionOutcomeStackPresentation[] Stacks { get; }
    internal ActionOutcomeServiceDetailPresentation[] Details { get; }
    internal long Committed { get; }
    internal bool HasFault { get; }
}

internal readonly struct ActionOutcomeServiceDetailPresentation
{
    internal ActionOutcomeServiceDetailPresentation(
        ServiceId service,
        ActionOutcomeServiceColor color,
        string summary)
    {
        Service = service;
        Color = color;
        Summary = summary ?? throw new ArgumentNullException(nameof(summary));
    }

    internal ServiceId Service { get; }
    internal ActionOutcomeServiceColor Color { get; }
    internal string Summary { get; }
}

internal readonly struct ActionOutcomeLegendPresentation
{
    internal ActionOutcomeLegendPresentation(
        ServiceId service,
        string displayName,
        ActionOutcomeServiceColor color)
    {
        Service = service;
        DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
        Color = color;
    }

    internal ServiceId Service { get; }
    internal string DisplayName { get; }
    internal ActionOutcomeServiceColor Color { get; }
}

internal readonly struct ActionOutcomeSurfacePresentation
{
    internal const string Title = "Automation activity · last 30 minutes";
    internal const string QuietWindow = "No automation activity in the last 30 minutes";
    internal const string AxisLabel = "Completed actions / minute";
    internal const string EmptyMinute = "No automation outcomes in this minute";
    internal const string EmptyTiming = "Recent processing · waiting for activity";

    internal ActionOutcomeSurfacePresentation(
        ActionOutcomeBucketPresentation[] buckets,
        ActionOutcomeLegendPresentation[] legend,
        bool showsTimeline,
        long maximumCommitted,
        string timingSummary)
    {
        Buckets = buckets ?? throw new ArgumentNullException(nameof(buckets));
        Legend = legend ?? throw new ArgumentNullException(nameof(legend));
        ShowsTimeline = showsTimeline;
        MaximumCommitted = maximumCommitted;
        TimingSummary = timingSummary ?? throw new ArgumentNullException(nameof(timingSummary));
    }

    internal ActionOutcomeBucketPresentation[] Buckets { get; }
    internal ActionOutcomeLegendPresentation[] Legend { get; }
    internal bool ShowsTimeline { get; }
    internal string QuietMessage => ShowsTimeline ? string.Empty : QuietWindow;
    internal long MaximumCommitted { get; }
    internal string TimingSummary { get; }

    internal static ActionOutcomeSurfacePresentation Build(
        ReadOnlySpan<ServiceActionTimelineCellSnapshot> cells,
        int serviceCount,
        int bucketCount,
        ReadOnlySpan<ServiceCyclePumpTimingSample> timings)
    {
        if (serviceCount < 0) throw new ArgumentOutOfRangeException(nameof(serviceCount));
        if (bucketCount < 0) throw new ArgumentOutOfRangeException(nameof(bucketCount));
        if (cells.Length == 0)
        {
            return new ActionOutcomeSurfacePresentation(
                Array.Empty<ActionOutcomeBucketPresentation>(),
                Array.Empty<ActionOutcomeLegendPresentation>(),
                showsTimeline: false,
                maximumCommitted: 0,
                Timing(timings));
        }
        if (cells.Length != checked(serviceCount * bucketCount))
            throw new ArgumentException("The timeline must contain every service cell for every bucket.", nameof(cells));

        ValidateShape(cells, serviceCount, bucketCount);
        var totals = new long[serviceCount];
        var buckets = new ActionOutcomeBucketPresentation[bucketCount];
        long maximum = 0;
        var hasFault = false;
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var offset = checked(bucket * serviceCount);
            var stacks = new List<ActionOutcomeStackPresentation>(serviceCount);
            var details = new List<ActionOutcomeServiceDetailPresentation>(serviceCount);
            long total = 0;
            var bucketFault = false;
            for (var serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
            {
                var cell = cells[offset + serviceIndex];
                if (!ActionOutcomeTimelineServicePolicy.Includes(cell.Service, cell.Shape)) continue;
                var displayName = AutomataServiceCycleTraceRoster.DisplayName(cell.Service);
                if (string.IsNullOrEmpty(displayName)) displayName = cell.Service.Value;
                if (cell.Committed > 0)
                {
                    total = SaturatingAdd(total, cell.Committed);
                    totals[serviceIndex] = SaturatingAdd(totals[serviceIndex], cell.Committed);
                    stacks.Add(new ActionOutcomeStackPresentation(
                        cell.Service,
                        ColorFor(cell.Service),
                        cell.Committed));
                }
                if (cell.Committed > 0 || cell.Rejected > 0 || cell.Skipped > 0 || cell.FaultedCount > 0)
                {
                    details.Add(new ActionOutcomeServiceDetailPresentation(
                        cell.Service,
                        ColorFor(cell.Service),
                        DetailSummary(displayName, in cell)));
                }
                bucketFault |= cell.Faulted;
            }
            stacks.Sort(CompareStacks);
            details.Sort(CompareDetails);
            if (total > maximum) maximum = total;
            hasFault |= bucketFault;
            buckets[bucket] = new ActionOutcomeBucketPresentation(
                cells[offset].MinuteKey,
                stacks.ToArray(),
                details.ToArray(),
                total,
                bucketFault);
        }

        var legend = new List<ActionOutcomeLegendPresentation>(serviceCount);
        for (var serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
        {
            if (totals[serviceIndex] <= 0) continue;
            var service = cells[serviceIndex].Service;
            if (!ActionOutcomeTimelineServicePolicy.Includes(
                    service,
                    cells[serviceIndex].Shape)) continue;
            var displayName = AutomataServiceCycleTraceRoster.DisplayName(service);
            if (string.IsNullOrEmpty(displayName)) displayName = service.Value;
            legend.Add(new ActionOutcomeLegendPresentation(
                service,
                displayName,
                ColorFor(service)));
        }
        legend.Sort(CompareLegend);
        return new ActionOutcomeSurfacePresentation(
            buckets,
            legend.ToArray(),
            showsTimeline: maximum > 0 || hasFault,
            maximum,
            Timing(timings));
    }

    internal static ActionOutcomeServiceColor ColorFor(ServiceId service) => service.Value switch
    {
        "orbautomata.auto-harvest" => ActionOutcomeServiceColor.Leaf,
        "orbautomata.auto-buy" => ActionOutcomeServiceColor.Amber,
        "orbautomata.spell-level" => ActionOutcomeServiceColor.Sky,
        "orbautomata.auto-cast" => ActionOutcomeServiceColor.Violet,
        "orbautomata.auto-concept" => ActionOutcomeServiceColor.Cyan,
        "orbautomata.auto-items" => ActionOutcomeServiceColor.Orange,
        "orbautomata.auto-scribe" => ActionOutcomeServiceColor.Rose,
        "orbmentor.mastery-sharing" => ActionOutcomeServiceColor.Teal,
        _ => (ActionOutcomeServiceColor)(StableHash(service.Value) % 8u),
    };

    private static void ValidateShape(
        ReadOnlySpan<ServiceActionTimelineCellSnapshot> cells,
        int serviceCount,
        int bucketCount)
    {
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var offset = checked(bucket * serviceCount);
            var minute = cells[offset].MinuteKey;
            for (var serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
            {
                var current = cells[offset + serviceIndex];
                if (current.MinuteKey != minute)
                    throw new ArgumentException("Every cell in a bucket must share its minute key.", nameof(cells));
                if (bucket == 0) continue;
                var prior = cells[serviceIndex];
                if (current.Service != prior.Service || current.Shape != prior.Shape)
                    throw new ArgumentException("Timeline service order must remain stable across buckets.", nameof(cells));
            }
        }
    }

    private static int CompareStacks(
        ActionOutcomeStackPresentation left,
        ActionOutcomeStackPresentation right)
    {
        var color = left.Color.CompareTo(right.Color);
        return color != 0
            ? color
            : string.Compare(left.Service.Value, right.Service.Value, StringComparison.Ordinal);
    }

    private static int CompareLegend(
        ActionOutcomeLegendPresentation left,
        ActionOutcomeLegendPresentation right)
    {
        var color = left.Color.CompareTo(right.Color);
        return color != 0
            ? color
            : string.Compare(left.Service.Value, right.Service.Value, StringComparison.Ordinal);
    }

    private static int CompareDetails(
        ActionOutcomeServiceDetailPresentation left,
        ActionOutcomeServiceDetailPresentation right)
    {
        var color = left.Color.CompareTo(right.Color);
        return color != 0
            ? color
            : string.Compare(left.Service.Value, right.Service.Value, StringComparison.Ordinal);
    }

    private static string DetailSummary(
        string displayName,
        in ServiceActionTimelineCellSnapshot cell)
    {
        var outcomes = new List<string>(4);
#if SERVICE_CYCLE_PROFILE
        if (cell.Committed > 0) outcomes.Add($"committed {cell.Committed}");
        if (cell.Rejected > 0) outcomes.Add($"rejected {cell.Rejected}");
        if (cell.Skipped > 0) outcomes.Add($"skipped {cell.Skipped}");
        if (cell.FaultedCount > 0) outcomes.Add($"faulted {cell.FaultedCount}");
#else
        if (cell.Committed > 0) outcomes.Add($"{cell.Committed} completed");
        if (cell.Rejected > 0) outcomes.Add($"{cell.Rejected} not applied");
        if (cell.Skipped > 0) outcomes.Add($"{cell.Skipped} skipped");
        if (cell.FaultedCount > 0) outcomes.Add($"{cell.FaultedCount} failed");
#endif
        return displayName + " · " + string.Join(" · ", outcomes);
    }

    private static uint StableHash(string value)
    {
        const uint offset = 2166136261;
        const uint prime = 16777619;
        var hash = offset;
        for (var index = 0; index < value.Length; index++)
            hash = unchecked((hash ^ value[index]) * prime);
        return hash;
    }

    private static string Timing(ReadOnlySpan<ServiceCyclePumpTimingSample> timings)
    {
        if (timings.Length == 0) return EmptyTiming;
        long total = 0;
        long worst = 0;
        for (var index = 0; index < timings.Length; index++)
        {
            var ticks = timings[index].TotalDuration.Ticks;
            total = SaturatingAdd(total, ticks);
            if (ticks > worst) worst = ticks;
        }
        return string.Format(
            CultureInfo.InvariantCulture,
            "Recent processing · average {0:F3} ms · worst {1:F3} ms",
            total / (double)timings.Length / TimeSpan.TicksPerMillisecond,
            worst / (double)TimeSpan.TicksPerMillisecond);
    }

    private static long SaturatingAdd(long left, long right) =>
        right > long.MaxValue - left ? long.MaxValue : left + right;
}

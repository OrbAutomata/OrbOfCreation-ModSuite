using System;
using System.Globalization;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// Says out loud what a service's retry gate is doing: one line when a target's episode is announced,
/// and one when that episode ends. A quiet entry is announced only once it stops being quiet.
/// </summary>
internal sealed class ServiceRetryEpisodeNarrator
{
    private readonly string _feature;
    private readonly Action<string> _warn;
    private readonly Func<ServiceRetryTarget, string> _describe;

    /// <param name="describe">
    /// Names a target in a sentence; a service whose targets take several action kinds says which.
    /// The entity's identity alone when omitted.
    /// </param>
    internal ServiceRetryEpisodeNarrator(
        string feature,
        Action<string> warn,
        Func<ServiceRetryTarget, string>? describe = null)
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("The feature name is required.", nameof(feature));
        _feature = feature;
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
        _describe = describe ?? (static target => EntityIdentityFormatter.Format(target.Id));
    }

    internal ServiceRetryGateSnapshot Current { get; private set; } = ServiceRetryGateSnapshot.Empty;
    internal MonotonicTimestamp ObservedAt { get; private set; }

    /// <summary>The announced target still backing off when the gate was last observed.</summary>
    internal bool TryGetBackingOff(out ServiceRetryEntry entry) =>
        Current.TryGetLatestAnnounced(ObservedAt, out entry);

    /// <summary>
    /// Narrates the change from the last gate seen and moves the clock to <paramref name="now"/>;
    /// answers whether the gate changed or the announced target backing off did.
    /// </summary>
    internal bool Observe(ServiceRetryGateSnapshot gate, MonotonicTimestamp now)
    {
        if (gate is null) throw new ArgumentNullException(nameof(gate));
        var wasBackingOff = TryGetBackingOff(out var before);
        var gateChanged = !ReferenceEquals(gate, Current);
        if (gateChanged) Narrate(gate);
        ObservedAt = now;
        var backingOff = TryGetBackingOff(out var after);
        return gateChanged || backingOff != wasBackingOff || backingOff && after.Target != before.Target;
    }

    private void Narrate(ServiceRetryGateSnapshot gate)
    {
        for (var index = 0; index < Current.Count; index++)
        {
            var previous = Current[index];
            if (!previous.IsQuiet && !gate.TryFind(previous.Target, out _))
                _warn(
                    $"{_feature} stops backing off {_describe(previous.Target)} after " +
                    $"{Attempts(previous.ConsecutiveFailures)}.");
        }
        for (var index = 0; index < gate.Count; index++)
        {
            var entry = gate[index];
            if (entry.IsQuiet ||
                Current.TryFind(entry.Target, out var previous) && !previous.IsQuiet)
                continue;
            _warn(
                $"{_feature} backs off {_describe(entry.Target)} after " +
                $"{Attempts(entry.ConsecutiveFailures)} and tries again in " +
                $"{entry.Backoff.ToTimeSpan().TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s: " +
                entry.Reason);
        }
        Current = gate;
    }

    /// <summary>A new lifecycle starts with no target backing off, and nothing to announce about the old one.</summary>
    internal void InvalidateLifecycle() => Current = ServiceRetryGateSnapshot.Empty;

    private static string Attempts(int failures) =>
        failures == 1 ? "1 failed attempt" : $"{failures} failed attempts in a row";
}

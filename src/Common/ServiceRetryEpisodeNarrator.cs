using System;
using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>
/// Says out loud what a service's retry gate is doing: one line each time a target fails and backs
/// off, and one when it works again.
/// </summary>
internal sealed class ServiceRetryEpisodeNarrator
{
    private readonly string _feature;
    private readonly Action<string> _warn;

    internal ServiceRetryEpisodeNarrator(string feature, Action<string> warn)
    {
        if (string.IsNullOrWhiteSpace(feature)) throw new ArgumentException("The feature name is required.", nameof(feature));
        _feature = feature;
        _warn = warn ?? throw new ArgumentNullException(nameof(warn));
    }

    internal ServiceRetryGateSnapshot Current { get; private set; } = ServiceRetryGateSnapshot.Empty;

    /// <summary>Narrates the change from the last gate seen; answers whether anything changed.</summary>
    internal bool Observe(ServiceRetryGateSnapshot gate)
    {
        if (gate is null) throw new ArgumentNullException(nameof(gate));
        if (ReferenceEquals(gate, Current)) return false;
        for (var index = 0; index < Current.Count; index++)
        {
            var previous = Current[index];
            if (!gate.TryFind(previous.Target, out _))
                _warn(
                    $"{_feature} resumed {EntityIdentityFormatter.Format(previous.Target.Id)} after " +
                    $"{Attempts(previous.ConsecutiveFailures)}.");
        }
        for (var index = 0; index < gate.Count; index++)
        {
            var entry = gate[index];
            if (Current.TryFind(entry.Target, out var previous) &&
                previous.ConsecutiveFailures == entry.ConsecutiveFailures &&
                previous.FailedAt == entry.FailedAt)
                continue;
            _warn(
                $"{_feature} backs off {EntityIdentityFormatter.Format(entry.Target.Id)} after " +
                $"{Attempts(entry.ConsecutiveFailures)} and tries again in " +
                $"{entry.Backoff.ToTimeSpan().TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture)} s: " +
                entry.Reason);
        }
        Current = gate;
        return true;
    }

    /// <summary>A new lifecycle starts with no target backing off, and nothing to announce about the old one.</summary>
    internal void InvalidateLifecycle() => Current = ServiceRetryGateSnapshot.Empty;

    private static string Attempts(int failures) =>
        failures == 1 ? "1 failed attempt" : $"{failures} failed attempts in a row";
}

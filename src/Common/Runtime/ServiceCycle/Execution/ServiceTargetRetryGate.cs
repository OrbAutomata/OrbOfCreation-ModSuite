using System;
using System.Collections.Generic;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

/// <summary>
/// The per-target backoff of one service runner. A failed or surprising action backs its own target
/// off with doubling waits; a verified commit of that target ends the episode, and the runner (with
/// the gate) is rebuilt for every lifecycle. Nothing here latches: a target only ever waits.
/// </summary>
internal sealed class ServiceTargetRetryGate
{
    private readonly ServiceFaultRecoveryPolicy _policy;

    internal ServiceTargetRetryGate(ServiceFaultRecoveryPolicy policy)
    {
        if (!policy.IsValid) throw new ArgumentException("A valid fault recovery policy is required.", nameof(policy));
        _policy = policy;
    }

    internal ServiceRetryGateSnapshot Snapshot { get; private set; } = ServiceRetryGateSnapshot.Empty;

    internal bool Allows(MonotonicDuration? initialBackoff, MonotonicDuration? maximumBackoff) =>
        _policy.Allows(initialBackoff, maximumBackoff);

    /// <summary>
    /// A quiet first failure stays quiet while the target keeps failing on the world it first failed
    /// on; the first failure from a fresher world is announced.
    /// </summary>
    internal ServiceRetryEntry Record(
        ServiceRetryTarget target,
        ServiceActionResultCode code,
        string reason,
        MonotonicTimestamp failedAt,
        bool awaitsObservation = false,
        MonotonicDuration? maximumBackoff = null,
        bool quietFirstFailure = false,
        WorldGeneration plannedFrom = default,
        MonotonicDuration? initialBackoff = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A backing-off target names why it failed.", nameof(reason));
        var current = Snapshot;
        var index = current.IndexOf(target);
        var failures = 1;
        var quiet = quietFirstFailure;
        if (index >= 0)
        {
            var previous = current[index];
            quiet &= previous.IsQuiet && previous.PlannedFrom == plannedFrom;
            failures = previous.ConsecutiveFailures == int.MaxValue
                ? int.MaxValue
                : previous.ConsecutiveFailures + 1;
            awaitsObservation |= previous.AwaitsObservation;
        }
        var backoff = _policy.BackoffAfter(
            failures,
            initialBackoff ?? _policy.InitialBackoff,
            maximumBackoff ?? _policy.MaximumBackoff);
        var entry = new ServiceRetryEntry(
            target,
            code,
            reason,
            failures,
            failedAt,
            failedAt.AddSaturated(backoff),
            awaitsObservation,
            quiet,
            plannedFrom);
        var entries = new List<ServiceRetryEntry>(current.Count + 1);
        for (var existing = 0; existing < current.Count; existing++)
            if (existing != index)
                entries.Add(current[existing]);
        entries.Add(entry);
        Snapshot = new ServiceRetryGateSnapshot(entries.ToArray());
        return entry;
    }

    /// <summary>A verified commit of the target ends an episode its own action failure started.</summary>
    internal void ClearOnCommit(ServiceRetryTarget target)
    {
        var index = Snapshot.IndexOf(target);
        if (index >= 0 && !Snapshot[index].AwaitsObservation) Remove(index);
    }

    /// <summary>The evaluator saw the target work again, or stopped considering it; either ends any episode.</summary>
    internal void Clear(ServiceRetryTarget target)
    {
        var index = Snapshot.IndexOf(target);
        if (index >= 0) Remove(index);
    }

    internal void Apply(ServiceRetryReport[]? reports, MonotonicTimestamp observedAt)
    {
        if (reports is null) return;
        foreach (var report in reports)
        {
            if (report.EndsEpisode)
                Clear(report.Target);
            else
                Record(
                    report.Target,
                    report.Code,
                    report.Reason,
                    observedAt,
                    awaitsObservation: true,
                    report.MaximumBackoff);
        }
    }

    /// <summary>A wait never outlasts the next target that becomes due, so the service wakes for it.</summary>
    internal MonotonicTimestamp WakeNoLaterThanNextRetry(MonotonicTimestamp due, MonotonicTimestamp now) =>
        Snapshot.TryGetNextDueAfter(now, out var retryDue) && retryDue < due ? retryDue : due;

    private void Remove(int index)
    {
        var current = Snapshot;
        if (current.Count == 1)
        {
            Snapshot = ServiceRetryGateSnapshot.Empty;
            return;
        }
        var entries = new ServiceRetryEntry[current.Count - 1];
        for (int from = 0, to = 0; from < current.Count; from++)
            if (from != index)
                entries[to++] = current[from];
        Snapshot = new ServiceRetryGateSnapshot(entries);
    }
}

/// <summary>What one evaluation saw happen to a target after its action had already committed.</summary>
internal readonly struct ServiceRetryReport
{
    private ServiceRetryReport(
        ServiceRetryTarget target,
        bool endsEpisode,
        ServiceActionResultCode code,
        string reason,
        MonotonicDuration? maximumBackoff)
    {
        Target = target;
        EndsEpisode = endsEpisode;
        Code = code;
        Reason = reason;
        MaximumBackoff = maximumBackoff;
    }

    internal ServiceRetryTarget Target { get; }
    internal bool EndsEpisode { get; }
    internal ServiceActionResultCode Code { get; }
    internal string Reason { get; }
    internal MonotonicDuration? MaximumBackoff { get; }

    internal static ServiceRetryReport Failed(
        ServiceRetryTarget target,
        ServiceActionResultCode code,
        string reason,
        MonotonicDuration? maximumBackoff) =>
        new(target, false, code, reason, maximumBackoff);

    internal static ServiceRetryReport End(ServiceRetryTarget target) =>
        new(target, true, default, string.Empty, null);
}

/// <summary>The worker's reports for the evaluation in progress, reset before each one.</summary>
internal sealed class ServiceRetryReportBuffer
{
    private readonly List<ServiceRetryReport> _reports = new();
    private readonly ServiceFaultRecoveryPolicy _policy;

    internal ServiceRetryReportBuffer(ServiceFaultRecoveryPolicy policy)
    {
        if (!policy.IsValid) throw new ArgumentException("A valid fault recovery policy is required.", nameof(policy));
        _policy = policy;
    }

    internal void Reset() => _reports.Clear();

    internal void Add(in ServiceRetryReport report)
    {
        if (!_policy.Allows(null, report.MaximumBackoff))
            throw new ArgumentOutOfRangeException(
                nameof(report),
                "A retry cap cannot be shorter than the service's initial fault backoff.");
        _reports.Add(report);
    }

    internal ServiceRetryReport[]? ToArrayOrNull() => _reports.Count == 0 ? null : _reports.ToArray();
}

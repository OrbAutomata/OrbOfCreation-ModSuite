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

    internal ServiceRetryEntry Record(
        ServiceRetryTarget target,
        string reason,
        MonotonicTimestamp failedAt,
        bool awaitsObservation = false,
        MonotonicDuration? maximumBackoff = null)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("A backing-off target names why it failed.", nameof(reason));
        var current = Snapshot;
        var index = current.IndexOf(target);
        var failures = 1;
        if (index >= 0)
        {
            var previous = current[index];
            failures = previous.ConsecutiveFailures == int.MaxValue
                ? int.MaxValue
                : previous.ConsecutiveFailures + 1;
            awaitsObservation |= previous.AwaitsObservation;
        }
        var backoff = _policy.BackoffAfter(failures, maximumBackoff ?? _policy.MaximumBackoff);
        var entry = new ServiceRetryEntry(
            target,
            reason,
            failures,
            failedAt,
            failedAt.AddSaturated(backoff),
            awaitsObservation);
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

    /// <summary>The evaluator saw the target work again, which ends any episode.</summary>
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
            if (report.IsVerified)
                Clear(report.Target);
            else
                Record(report.Target, report.Reason, observedAt, awaitsObservation: true, report.MaximumBackoff);
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
        bool isVerified,
        string reason,
        MonotonicDuration? maximumBackoff)
    {
        Target = target;
        IsVerified = isVerified;
        Reason = reason;
        MaximumBackoff = maximumBackoff;
    }

    internal ServiceRetryTarget Target { get; }
    internal bool IsVerified { get; }
    internal string Reason { get; }
    internal MonotonicDuration? MaximumBackoff { get; }

    internal static ServiceRetryReport Failed(
        ServiceRetryTarget target,
        string reason,
        MonotonicDuration? maximumBackoff) =>
        new(target, false, reason, maximumBackoff);

    internal static ServiceRetryReport Verified(ServiceRetryTarget target) =>
        new(target, true, string.Empty, null);
}

/// <summary>The worker's reports for the evaluation in progress, reset before each one.</summary>
internal sealed class ServiceRetryReportBuffer
{
    private readonly List<ServiceRetryReport> _reports = new();

    internal void Reset() => _reports.Clear();

    internal void Add(in ServiceRetryReport report) => _reports.Add(report);

    internal ServiceRetryReport[]? ToArrayOrNull() => _reports.Count == 0 ? null : _reports.ToArray();
}

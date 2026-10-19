using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal static class ServiceWakeSchedule
{
    internal static WakePolicy Resolve(WakePolicy requested, WakePolicy registrationDefault)
    {
        if (!requested.IsValid) throw new InvalidOperationException("The evaluator returned an invalid wake policy.");
        var resolved = requested.Kind == WakePolicyKind.Default ? registrationDefault : requested;
        if (!resolved.IsValid || resolved.Kind == WakePolicyKind.Default)
            throw new InvalidOperationException("The response wake policy did not resolve to a concrete policy.");
        return resolved;
    }

    internal static MonotonicTimestamp AtResponse(
        WakePolicy resolved,
        MonotonicTimestamp responsePublishedAt,
        bool zeroActions)
    {
        return resolved.Kind switch
        {
            WakePolicyKind.Immediate => responsePublishedAt,
            WakePolicyKind.AfterDecision => responsePublishedAt.AddSaturated(resolved.Delay),
            WakePolicyKind.AfterBatch when zeroActions => responsePublishedAt.AddSaturated(resolved.Delay),
            WakePolicyKind.AfterBatch => default,
            WakePolicyKind.At => resolved.DueTime,
            WakePolicyKind.OnPublication => new MonotonicTimestamp(long.MaxValue),
            _ => throw new InvalidOperationException("The wake policy is not concrete."),
        };
    }

    internal static MonotonicTimestamp AtBatchTerminal(
        WakePolicy resolved,
        MonotonicTimestamp responsePublishedAt,
        MonotonicTimestamp terminalAt)
    {
        return resolved.Kind switch
        {
            WakePolicyKind.Immediate => responsePublishedAt,
            WakePolicyKind.AfterDecision => responsePublishedAt.AddSaturated(resolved.Delay),
            WakePolicyKind.AfterBatch => terminalAt.AddSaturated(resolved.Delay),
            WakePolicyKind.At => resolved.DueTime,
            WakePolicyKind.OnPublication => new MonotonicTimestamp(long.MaxValue),
            _ => throw new InvalidOperationException("The wake policy is not concrete."),
        };
    }

    internal static MonotonicTimestamp FromRetryPolicy(WakePolicy policy, MonotonicTimestamp anchor)
    {
        return policy.Kind switch
        {
            WakePolicyKind.AfterDecision => anchor.AddSaturated(policy.Delay),
            WakePolicyKind.At => policy.DueTime,
            WakePolicyKind.OnPublication => new MonotonicTimestamp(long.MaxValue),
            _ => throw new InvalidOperationException("A retry policy must be AfterDecision, At, or OnPublication."),
        };
    }
}

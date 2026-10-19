using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal readonly struct ServiceFaultRecord
{
    internal ServiceFaultRecord(ServiceFault fault, MonotonicTimestamp retryDue)
    {
        Fault = fault;
        RetryDue = retryDue;
    }

    internal ServiceFault Fault { get; }
    internal MonotonicTimestamp RetryDue { get; }
}

internal sealed class ServiceFaultTracker
{
    private readonly ServiceFaultRecoveryPolicy _policy;
    private int _consecutiveFailures;
    private ServiceFault _latestFault;

    internal ServiceFaultTracker(ServiceFaultRecoveryPolicy policy)
    {
        if (!policy.IsValid) throw new ArgumentException("A valid fault recovery policy is required.", nameof(policy));
        _policy = policy;
    }

    internal int ConsecutiveFailures => _consecutiveFailures;

    internal ServiceFaultRecord Record(ServiceFaultCategory category, MonotonicTimestamp observedAt)
        => Record(category, CommonActionResultCodes.AdapterFault, observedAt);

    internal ServiceFaultRecord Record(
        ServiceFaultCategory category,
        ServiceActionResultCode code,
        MonotonicTimestamp observedAt)
    {
        if (_consecutiveFailures != int.MaxValue) _consecutiveFailures++;
        var retryDue = observedAt.AddSaturated(_policy.BackoffAfter(_consecutiveFailures));
        _latestFault = new ServiceFault(category, code, _consecutiveFailures, observedAt);
        return new ServiceFaultRecord(_latestFault, retryDue);
    }

    internal ServiceFaultRecoveryFact Recover(MonotonicTimestamp recoveredAt)
    {
        if (!_latestFault.IsValid) return default;
        var recovery = new ServiceFaultRecoveryFact(_latestFault, recoveredAt);
        Reset();
        return recovery;
    }

    internal ServiceFaultRecoveryFact PendingRecovery(MonotonicTimestamp recoveredAt) =>
        _latestFault.IsValid
            ? new ServiceFaultRecoveryFact(_latestFault, recoveredAt)
            : default;

    internal void Reset()
    {
        _consecutiveFailures = 0;
        _latestFault = default;
    }
}

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
    private readonly RepeatedFailure _loggedFailure = new();
    private string _failureReason = string.Empty;

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
        MonotonicTimestamp observedAt,
        string reason = "")
    {
        if (_consecutiveFailures != int.MaxValue) _consecutiveFailures++;
        var retryDue = observedAt.AddSaturated(_policy.BackoffAfter(_consecutiveFailures));
        _latestFault = new ServiceFault(category, code, _consecutiveFailures, observedAt, reason);
        return new ServiceFaultRecord(_latestFault, retryDue);
    }

    internal ServiceFaultRecord RecordException(
        ServiceFaultCategory category,
        MonotonicTimestamp observedAt,
        Exception exception,
        string service)
    {
        var reason = DescribeException(category, exception, service);
        return Record(category, CommonActionResultCodes.AdapterFault, observedAt, reason);
    }

    internal string DescribeException(ServiceFaultCategory category, Exception exception, string service)
    {
        if (!_loggedFailure.IsRepeat(exception, subject: category.ToString()))
            _failureReason = GameActionFaultLog.Reason(
                $"The {category} step failed; the service will retry.",
                exception,
                service + " " + category).Text;
        return _failureReason;
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
        _loggedFailure.Clear();
        _failureReason = string.Empty;
    }
}

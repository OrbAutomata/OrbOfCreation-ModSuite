using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

internal sealed partial class ServiceCycleSlot<TState, TAction>
{
    public void RequestLifecycle(LifecycleGeneration generation)
    {
        if (IsDisposed ||
            generation.Value == 0 ||
            generation.Value <= _desiredLifecycle.Value)
            return;
        _desiredLifecycle = generation;
        CurrentRunner?.MarkSuperseded();
    }

    public bool ReconcileLifecycle(
        MonotonicTimestamp now,
        long reconciliationEpoch)
    {
        if (reconciliationEpoch <= 0)
            throw new ArgumentOutOfRangeException(nameof(reconciliationEpoch));
        if (IsDisposed) return false;
        var released0 = _position0.TryReleaseStopped();
        var released1 = _position1.TryReleaseStopped();
        if (released0) IncrementPositionTransitions();
        if (released1) IncrementPositionTransitions();
        var changed = released0 | released1;
        var currentPosition =
            _position0.State == ServiceRunnerPositionState.Current
                ? _position0
                : _position1.State == ServiceRunnerPositionState.Current
                    ? _position1
                    : null;
        if (currentPosition?.Runner is { IsSuperseded: true } retiring)
        {
            if (!retiring.TryRetireForLifecycle(now, out var retirement))
                return changed;
            currentPosition.MarkRetiring();
            IncrementPositionTransitions();
            _latestTerminal = new ServiceLifecycleTerminalFact(
                checked(++_terminalSequence),
                retiring.Lifecycle,
                _desiredLifecycle,
                retirement.Phase,
                retirement.Cycle,
                retirement.Batch,
                retirement.Response,
                retirement.Receipt,
                now);
            IncrementLifecycleSemanticVersion();
            changed = true;
        }

        if (CurrentRunner is not null) return changed;
        var vacant = _position0.IsVacant
            ? _position0
            : _position1.IsVacant
                ? _position1
                : null;
        if (vacant is null) return changed;
        if (_hasConstructionRetry && now < _constructionRetryDue)
            return changed;
        if (_lastConstructionAttemptEpoch == reconciliationEpoch)
            return changed;

        _lastConstructionAttemptEpoch = reconciliationEpoch;
        _constructionAttemptCount = checked(_constructionAttemptCount + 1);
        try
        {
            var construction = _factory.TryCreate(_desiredLifecycle);
            if (construction.Contended)
            {
                RecordConstructionContention(now);
                return changed;
            }
            var runner = construction.Runner ??
                throw new InvalidOperationException(
                    "Runner construction returned no runner.");
            if (IsDisposed || runner.Lifecycle != _desiredLifecycle)
            {
                runner.Dispose();
                throw new InvalidOperationException(
                    "Lifecycle ownership changed while a replacement runner was constructed.");
            }
            vacant.InstallCurrent(runner);
            ArmWorldGate();
            IncrementPositionTransitions();
            _constructionFaults.Reset();
            _constructionFault = default;
            _constructionRetryDue = default;
            _hasConstructionRetry = false;
            _constructionContentionCount = 0;
            IncrementLifecycleSemanticVersion();
            return true;
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            var record = _constructionFaults.RecordException(
                ServiceFaultCategory.LifecycleConstruction,
                now,
                exception,
                ServiceId.Value);
            _constructionFault = record.Fault;
            _constructionRetryDue = record.RetryDue;
            _hasConstructionRetry = true;
            IncrementLifecycleSemanticVersion();
            return changed;
        }
    }

    private void IncrementPositionTransitions() =>
        _positionTransitionCount =
            checked(_positionTransitionCount + 1);

    private void IncrementLifecycleSemanticVersion() =>
        _lifecycleSemanticVersion =
            checked(_lifecycleSemanticVersion + 1);

    private void RecordConstructionContention(MonotonicTimestamp now)
    {
        _constructionContentionTotal =
            checked(_constructionContentionTotal + 1);
        _constructionContentionCount =
            Math.Min(_constructionContentionCount + 1, 7);
        var milliseconds =
            Math.Min(1000, 16 << (_constructionContentionCount - 1));
        _constructionRetryDue =
            now + MonotonicDuration.FromTimeSpan(
                TimeSpan.FromMilliseconds(milliseconds));
        _hasConstructionRetry = true;
        _latestConstructionDeferral =
            new ServiceLifecycleConstructionDeferralFact(
                checked(++_constructionDeferralSequence),
                _desiredLifecycle,
                now,
                _constructionRetryDue);
        IncrementLifecycleSemanticVersion();
    }
}

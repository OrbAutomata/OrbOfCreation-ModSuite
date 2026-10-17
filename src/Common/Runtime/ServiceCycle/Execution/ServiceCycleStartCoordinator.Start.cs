using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution;

internal abstract partial class ServiceCycleStartCoordinator<TState, TAction>
{
    internal ServiceCycleStartAttempt TryStart(
        MonotonicTimestamp now,
        bool nonBlockingProbe = false,
        int ordinal = 0,
        IServiceCycleAttemptObserver? observer = null)
    {
        if (Lifetime.IsSuperseded) return default;
        if (_hasPendingRequest)
            return TryPublishPendingRequest(nonBlockingProbe);

        var configuration = _configuration.ReadLatest();
        var world = _world.ReadLatest();
        State.LatestConfigGeneration = configuration.Generation;
        if (State.HasWakeDue &&
            State.WakeInvalidatedByConfiguration &&
            configuration.Generation != State.WakeConfigurationGeneration)
            State.ClearWake();
        if (State.HasWakeDue &&
            State.WakeInvalidatedByWorld &&
            world.Generation != State.WakeWorldGeneration)
            State.ClearWake();
        if (State.HasWakeDue && now < State.NextWakeDue)
            return default;

        ServiceHandoffSnapshot handoff;
        if (nonBlockingProbe)
        {
            if (_handoff.PhaseHint != ServiceHandoffPhase.Empty ||
                !_handoff.TrySnapshot(out handoff))
                return default;
        }
        else
        {
            handoff = _handoff.Snapshot;
        }
        if (handoff.Phase != ServiceHandoffPhase.Empty || handoff.CleanupPending)
            return default;

        var startContext = new ServiceCycleStartContext(
            Lifecycle,
            configuration.Generation,
            State.PreviousReceipt,
            now);
        var startAttemptedAt = Clock.Now;
        observer?.StartAttempted(ordinal, in startContext, startAttemptedAt);
        ServiceStartDecision start;
        IsInvokingStartCallback = true;
        try
        {
            var snapshot = configuration.Snapshot;
            start = _definition.ShouldStart(in snapshot, in startContext);
            if (!start.IsValid)
                throw new InvalidOperationException(
                    "The service returned an invalid start decision.");
        }
        catch
        {
            var startFaultedAt = Clock.Now;
            var record = RecordStartFault(ServiceFaultCategory.Start, startFaultedAt);
            var invocation = new ServiceStartInvocationFact(
                startContext,
                startAttemptedAt,
                startFaultedAt);
            return new ServiceCycleStartAttempt(
                false, default, default, default, default, default,
                record.Fault, record.RetryDue, startInvocation: invocation);
        }
        finally
        {
            IsInvokingStartCallback = false;
        }
        var startObservedAt = Clock.Now;
        var startInvocation = new ServiceStartInvocationFact(
            startContext,
            startAttemptedAt,
            startObservedAt);
        var startFact = new ServiceStartDecisionFact(start, startObservedAt);
        State.LastStartDecision = startFact;

        if (start.ShouldStart)
            observer?.StartReady(
                ordinal,
                in startContext,
                in start,
                startObservedAt,
                new MonotonicDuration(
                    startObservedAt.Ticks - startAttemptedAt.Ticks));

        if (Lifetime.IsSuperseded)
            return new ServiceCycleStartAttempt(
                false, startFact, default, default, default, default,
                startInvocation: startInvocation);

        if (!start.ShouldStart)
        {
            State.ScheduleWake(
                ServiceWakeSchedule.FromRetryPolicy(
                    start.WakePolicy,
                    startObservedAt),
                configuration.Generation,
                world.Generation,
                invalidatedByWorld: WorldEndsWait(start.WakePolicy));
            var recoveredFault = RecoverStartFault(startObservedAt);
            return new ServiceCycleStartAttempt(
                false, startFact, default, default, default, default,
                recoveredFault: recoveredFault,
                startInvocation: startInvocation);
        }

        return Open(
            configuration,
            in startFact,
            in startInvocation,
            nonBlockingProbe,
            ordinal,
            observer);
    }
}

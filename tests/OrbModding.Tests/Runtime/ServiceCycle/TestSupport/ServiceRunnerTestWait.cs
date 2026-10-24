using OrbModding.TestSupport;
using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class ServiceRunnerTestWait
{
    /// <summary>
    /// Waits until the worker has entered the production handoff wait used to receive a request.
    /// </summary>
    internal static void ForWorkerReady<TState, TAction>(
        ServiceRegistration<TState, TAction> registration)
    {
        if (!registration.Runner.WaitForWorkerReady(ServiceCycleTestDeadline.Value))
            throw new TimeoutException("The service worker never entered its request wait.");
    }

    /// <summary>
    /// Waits on the handoff monitor until the worker publishes the response the pump will acquire.
    /// </summary>
    /// <remarks>
    /// Response publication has no production latency contract. The shared deadline is only the
    /// test-suite wedge guard; unlike a short per-test timeout, it is not an assertion that the host
    /// schedules the worker within a particular wall-clock interval.
    /// </remarks>
    internal static void ForResponse<TState, TAction>(
        ServiceRegistration<TState, TAction> registration)
    {
        var runner = registration.Runner;
        var snapshot = runner.Snapshot;
        if (!snapshot.HasInFlightCycle)
            throw new InvalidOperationException("The service has no in-flight cycle to await.");
        if (!registration.Slot.WaitForResponseReadyAndWorkerSettled(
                snapshot.InFlightCycle,
                ServiceCycleTestDeadline.Value))
        {
            throw new TimeoutException(
                "The service worker never published its response and returned to its request wait.");
        }
    }

    internal static void ForPhase<TState, TAction>(
        ServiceRunner<TState, TAction> runner,
        ServiceHandoffPhase phase)
    {
        if (!SpinWait.SpinUntil(() => runner.Snapshot.Handoff.Phase == phase, ServiceCycleTestDeadline.Value))
            throw new TimeoutException($"Runner did not reach phase {phase}; current {runner.Snapshot.Handoff.Phase}.");
    }

    internal static void ForCleanup<TState, TAction>(
        ServiceRunner<TState, TAction> runner)
    {
        if (!SpinWait.SpinUntil(() => !runner.Snapshot.Handoff.CleanupPending, ServiceCycleTestDeadline.Value))
            throw new TimeoutException("Runner suffix cleanup did not complete.");
    }

    internal static ServiceHandoffSnapshot ForHandoff<TState, TAction>(
        ServiceRunner<TState, TAction> runner,
        Func<ServiceHandoffSnapshot, bool> predicate,
        string expectation)
    {
        var observed = default(ServiceHandoffSnapshot);
        if (!SpinWait.SpinUntil(
                () => predicate(observed = runner.ProbeHandoff()),
                ServiceCycleTestDeadline.Value))
        {
            throw new TimeoutException(
                $"Runner did not reach {expectation}; current phase {observed.Phase}, " +
                $"waits {observed.WorkerWaitCount}, cleanup acknowledgements " +
                $"{observed.CleanupAcknowledgementCount}.");
        }
        return observed;
    }

    /// <summary>
    /// Takes the published response the way the pump does: without blocking, and again if the worker
    /// happened to be holding the gate.
    /// </summary>
    /// <remarks>
    /// A non-blocking acquisition is allowed to come back empty. It takes the handoff gate with a zero
    /// timeout precisely so a worker can never park the main thread, and the pump answers an empty one
    /// by trying again on the next frame. A test that reads the acquisition has to do the same, or it
    /// is asserting that no worker held its own gate at that instant — which nothing promises, and
    /// which stops being true as soon as the machine is busy.
    /// </remarks>
    internal static ServiceResponseAcquisition AcquireResponse<TState, TAction>(
        ServiceRunner<TState, TAction> runner,
        ThreadSafeTestClock clock)
    {
        var acquisition = default(ServiceResponseAcquisition);
        if (!SpinWait.SpinUntil(
                () => (acquisition = runner.TryAcquireResponseNonBlocking(clock.Now)).Acquired,
                ServiceCycleTestDeadline.Value))
        {
            throw new TimeoutException("The runner never handed over its published response.");
        }
        return acquisition;
    }

    internal static long PrepareBatch(
        SuiteFramePump pump,
        ServiceRegistration<ExecutionState, ExecutionAction> registration)
    {
        var frame = 1L;
        var waiting = ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.Empty && value.WorkerWaitCount > 0,
            "the initial worker wait");
        Assert.Equal(1, pump.PumpFrame(frame++).CyclesStarted);
        ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.ResponseReady &&
                value.WorkerWaitCount > waiting.WorkerWaitCount,
            "the prepared response and worker wait");
        Assert.Equal(1, pump.PumpFrame(frame++).ResponsesAcquired);
        return frame;
    }

    /// <summary>
    /// Pumps until the runtime has opened a cycle for the service, yielding between frames.
    /// </summary>
    /// <remarks>
    /// Deadlined because the only way out of <see cref="ServiceHandoffPhase.Empty"/> is the
    /// runtime opening a cycle, and the world freshness gate can refuse to do that forever: a
    /// composition whose collector never publishes past the service's last action never leaves this
    /// loop. That must fail the test rather than hang the run.
    /// </remarks>
    internal static void PublishDeferredRequest<TState, TAction>(
        SuiteFramePump pump,
        ServiceRunner<TState, TAction> runner,
        ref long frameIdentity)
    {
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        while (runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
        {
            pump.PumpFrame(frameIdentity++);
            if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                throw new TimeoutException(
                    "The runtime never opened a cycle; the world freshness gate may be holding it.");
            Thread.Yield();
        }
    }

    /// <summary>
    /// Pumps until the runtime has opened a cycle for the service and taken its response.
    /// </summary>
    internal static void PumpToResponse<TState, TAction>(
        SuiteFramePump pump,
        ServiceRunner<TState, TAction> runner,
        ref long frame)
    {
        PublishDeferredRequest(pump, runner, ref frame);
        ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => runner.HandoffPhaseHint != ServiceHandoffPhase.ResponseReady);
    }

    /// <summary>
    /// Pumps until a finished batch has been handed back, which the pump does on a frame after the
    /// one that finished it.
    /// </summary>
    internal static void PumpToHandback<TState, TAction>(
        SuiteFramePump pump,
        ServiceRunner<TState, TAction> runner,
        ref long frame) =>
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.Empty);

    /// <summary>
    /// Builds every service's worker state with one zero-action cycle through the pump, and leaves
    /// each parked, with its own action count back, until the next world reading.
    /// </summary>
    /// <remarks>
    /// Worker state is built on the first evaluation behind the registry-wide single factory claim,
    /// so services taking their first turn together can answer with a transient-contention deferral
    /// instead of an action batch. Priming waits that out, advancing the clock so the deferral's
    /// retry comes due, and parks each primed service on the next publication so it stays idle, not
    /// cycling, until every service is ready and the caller publishes.
    /// </remarks>
    internal static void PrimeWorkerStates(
        SuiteFramePump pump,
        ThreadSafeTestClock clock,
        ref long frame,
        params (ExecutionServiceDefinition Definition,
            ServiceRunner<ExecutionState, ExecutionAction> Runner)[] services)
    {
        var actionCounts = new int[services.Length];
        var wakes = new WakePolicy[services.Length];
        for (var index = 0; index < services.Length; index++)
        {
            actionCounts[index] = services[index].Definition.ActionCount;
            wakes[index] = services[index].Definition.EvaluationWake;
            services[index].Definition.ActionCount = 0;
            services[index].Definition.EvaluationWake = WakePolicy.OnPublication;
        }
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => Array.TrueForAll(services, service =>
                service.Definition.StateCreateCount == 1 &&
                service.Runner.Snapshot.PreviousReceipt.IsPresent &&
                service.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty),
            clock);
        for (var index = 0; index < services.Length; index++)
        {
            Assert.False(services[index].Runner.Snapshot.Fault.IsValid);
            services[index].Definition.ActionCount = actionCounts[index];
            services[index].Definition.EvaluationWake = wakes[index];
        }
    }

    internal static void RunZeroActionCycle<TState, TAction>(
        ServiceRunner<TState, TAction> runner,
        ThreadSafeTestClock clock)
    {
        StartCycle(runner, clock.Now);
        AcquireResponse(runner, clock);
        ForHandback(runner);
    }

    internal static void RunAndDrain(
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock,
        int count)
    {
        StartCycle(runner, clock.Now);
        AcquireResponse(runner, clock);
        var dispatch = default(ServiceActionDispatch);
        for (var index = 0; index < count; index++)
        {
            dispatch = runner.TryExecuteOneNonBlocking(clock.Now);
            Assert.True(dispatch.Attempted);
        }
        if (dispatch.BatchTerminal) ForHandback(runner);
    }

    internal static void RunRejectedBatch(
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        StartCycle(runner, clock.Now);
        AcquireResponse(runner, clock);
        Assert.True(runner.TryExecuteOneNonBlocking(clock.Now).BatchTerminal);
        ForHandback(runner);
    }

    /// <summary>
    /// Queues a cycle the way the pump does, asking again whenever the zero-timeout handoff probe
    /// came back empty because the worker was holding its gate.
    /// </summary>
    /// <remarks>
    /// A probe that loses the gate after the start decision ran stashes the request, and the next
    /// probe publishes that stash without deciding again, so the start callback still runs once.
    /// </remarks>
    internal static ServiceCycleStartAttempt StartCycle<TState, TAction>(
        ServiceRunner<TState, TAction> runner,
        MonotonicTimestamp now)
    {
        var attempt = default(ServiceCycleStartAttempt);
        if (!SpinWait.SpinUntil(
                () => (attempt = runner.TryStartCycleNonBlocking(now)).Queued,
                ServiceCycleTestDeadline.Value))
        {
            throw new TimeoutException("The runner never queued a cycle.");
        }
        return attempt;
    }

    /// <summary>
    /// Asks the runner to start and returns the first attempt that reached the service's own start
    /// decision, for a test about what that decision or its capture did.
    /// </summary>
    /// <remarks>
    /// The handoff probe runs before the decision and a lost probe returns before it, so reading a
    /// single attempt would assert that the worker was not holding its gate at that instant.
    /// </remarks>
    internal static ServiceCycleStartAttempt DecideStart<TState, TAction>(
        ServiceRunner<TState, TAction> runner,
        MonotonicTimestamp now)
    {
        var attempt = default(ServiceCycleStartAttempt);
        if (!SpinWait.SpinUntil(
                () =>
                {
                    attempt = runner.TryStartCycleNonBlocking(now);
                    return attempt.Queued || attempt.StartInvocation.IsPresent;
                },
                ServiceCycleTestDeadline.Value))
        {
            throw new TimeoutException("The runner never reached the service's start decision.");
        }
        return attempt;
    }

    /// <summary>
    /// Hands main ownership back after a terminal, which the pump does on a later frame rather than
    /// inside the call that ended the batch.
    /// </summary>
    internal static void ForHandback<TState, TAction>(
        ServiceRunner<TState, TAction> runner)
    {
        if (!SpinWait.SpinUntil(
                runner.TryAdvancePendingMainOwnership,
                ServiceCycleTestDeadline.Value))
        {
            throw new TimeoutException(
                $"The runner never handed main ownership back; current phase {runner.HandoffPhaseHint}.");
        }
    }
}

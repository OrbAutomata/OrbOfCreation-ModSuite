using OrbModding.TestSupport;
using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Execution;

public sealed class ServiceCommittedRuntimeFactTests
{
    [Fact]
    public void DeferredRequestPublishesQueueFactWithoutRepeatingCaptureFact()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("test.execution.facts.deferred");
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;
        ServiceRunnerTestWait.ForWorkerReady(registration);
        using var contention = new HandoffGateContention(runner);
        definition.CaptureCallback = () =>
        {
            clock.Advance(new MonotonicDuration(3));
            contention.Acquire();
        };

        var captured = runner.TryStartCycleNonBlocking(clock.Now);

        Assert.False(captured.Queued);
        Assert.True(captured.StartDecisionFact.IsPresent);
        Assert.Equal(100, captured.StartDecisionFact.ObservedAt.Ticks);
        Assert.True(captured.CaptureFact.IsPresent);
        Assert.Equal(definition.ServiceId, captured.CaptureFact.Context.Service);
        Assert.Equal(new CaptureSequence(1), captured.CaptureFact.Context.Capture);
        Assert.Equal(new CycleId(1), captured.CaptureFact.Context.Cycle);
        Assert.Equal(100, captured.CaptureFact.StartedAt.Ticks);
        Assert.Equal(103, captured.CaptureFact.CompletedAt.Ticks);
        Assert.True(captured.CaptureFact.Result.IsCaptured);
        Assert.True(captured.Cycle.IsValid);
        Assert.Equal(new BatchId(1), captured.Batch);

        contention.Release();
        definition.CaptureCallback = null;
        clock.Advance(new MonotonicDuration(2));

        var queued = runner.TryStartCycleNonBlocking(clock.Now);

        Assert.True(queued.Queued);
        Assert.True(queued.StartDecisionFact.IsPresent);
        Assert.True(queued.StartDecisionFact.Decision.ShouldStart);
        Assert.False(queued.CaptureFact.IsPresent);
        Assert.Equal(captured.Cycle, queued.Cycle);
        Assert.Equal(captured.Batch, queued.Batch);
        Assert.Equal(105, queued.QueuedAt.Ticks);
        Assert.Equal(1, definition.StartCount);
    }

    [Fact]
    public void ResponseFactsExposeSuccessZeroAndFailureTransactions()
    {
        AssertSuccessfulResponse(actionCount: 2, expectTerminalReceipt: false);
        AssertSuccessfulResponse(actionCount: 0, expectTerminalReceipt: true);

        var clock = new ThreadSafeTestClock(300);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.facts.response.failure")
        {
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        definition.FailNextEvaluations(1);
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        var start = ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        Assert.True(start.Queued);
        ServiceCycleTestDeadline.ForSignal(entered, "the evaluation callback");
        clock.Advance(new MonotonicDuration(9));
        release.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);

        var acquisition = ServiceRunnerTestWait.AcquireResponse(runner, clock);

        Assert.True(acquisition.Acquired);
        Assert.False(acquisition.Response.Succeeded);
        Assert.False(acquisition.Response.TransientContention);
        Assert.Equal(start.Cycle, acquisition.Response.Cycle);
        Assert.Equal(start.Batch, acquisition.Response.Batch);
        Assert.Equal(300, acquisition.Response.EvaluationStartedAt.Ticks);
        Assert.Equal(309, acquisition.Response.EvaluationCompletedAt.Ticks);
        Assert.Equal(ServiceFaultCategory.Evaluation, acquisition.Response.Fault.Category);
        Assert.Equal(319, acquisition.Response.RetryDue.Ticks);
        Assert.False(acquisition.TerminalReceipt.IsPresent);
    }

    [Fact]
    public void ResponseFactDistinguishesTransientStateFactoryContention()
    {
        var clock = new ThreadSafeTestClock(400);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.facts.response.contention");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        var ledger = ServiceCycleRegistryTestAccess.ResourceClaims(registry);
        Assert.Equal(
            ServiceResourceClaimResult.Claimed,
            ledger.TryBeginFactory(ServiceResourceRole.WorkerDefinition, out var blocker));

        try
        {
            var start = ServiceRunnerTestWait.StartCycle(runner, clock.Now);
            Assert.True(start.Queued);
            ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);

            var acquisition = ServiceRunnerTestWait.AcquireResponse(runner, clock);

            Assert.True(acquisition.Acquired);
            Assert.False(acquisition.Response.Succeeded);
            Assert.True(acquisition.Response.TransientContention);
            Assert.Equal(start.Cycle, acquisition.Response.Cycle);
            Assert.Equal(start.Batch, acquisition.Response.Batch);
            Assert.Equal(400, acquisition.Response.EvaluationStartedAt.Ticks);
            Assert.Equal(400, acquisition.Response.EvaluationCompletedAt.Ticks);
            Assert.False(acquisition.Response.Fault.IsValid);
            Assert.Equal(160_400, acquisition.Response.RetryDue.Ticks);
            Assert.False(acquisition.TerminalReceipt.IsPresent);
        }
        finally
        {
            ledger.EndFactory(blocker);
        }
    }

    [Fact]
    public void CaptureAndWorkerFaultFactsExposeRetryAndActualRecovery()
    {
        var captureClock = new ThreadSafeTestClock(100);
        using var captureRegistry = new ServiceCycleRegistry(1, captureClock);
        var captureDefinition = new SourceServiceDefinition("test.execution.facts.capture-recovery")
        {
            CaptureCallback = () =>
            {
                captureClock.Advance(new MonotonicDuration(4));
                throw new InvalidOperationException("capture fault");
            },
        };
        using var captureRegistration = captureRegistry.RegisterSource(
            captureDefinition,
            new LifecycleGeneration(1));
        var captureRunner = captureRegistration.Runner;

        ServiceRunnerTestWait.ForWorkerReady(captureRegistration);
        var captureFault = ServiceRunnerTestWait.DecideStart(captureRunner, captureClock.Now);
        Assert.True(captureFault.CaptureFact.IsPresent);
        Assert.Equal(ServiceFaultCategory.Capture, captureFault.Fault.Category);
        Assert.Equal(captureFault.Fault, captureFault.CaptureFact.Fault);
        Assert.Equal(114, captureFault.RetryDue.Ticks);
        Assert.Equal(captureFault.RetryDue, captureFault.CaptureFact.RetryDue);
        captureDefinition.CaptureCallback = null;
        captureClock.AdvanceTo(captureFault.RetryDue);

        var captureRecovery = ServiceRunnerTestWait.StartCycle(captureRunner, captureClock.Now);
        Assert.True(captureRecovery.Queued);
        Assert.True(captureRecovery.RecoveredFault.IsPresent);
        Assert.Equal(captureFault.Fault, captureRecovery.RecoveredFault.Fault);
        Assert.Equal(114, captureRecovery.RecoveredFault.RecoveredAt.Ticks);

        var workerClock = new ThreadSafeTestClock(500);
        using var workerRegistry = new ServiceCycleRegistry(1, workerClock);
        var workerDefinition = new ExecutionServiceDefinition("test.execution.facts.worker-recovery")
        {
            ActionCount = 0,
        };
        workerDefinition.FailNextEvaluations(1);
        using var workerRegistration = workerRegistry.Register(
            workerDefinition,
            new LifecycleGeneration(1));
        var workerRunner = workerRegistration.Runner;

        ServiceRunnerTestWait.StartCycle(workerRunner, workerClock.Now);
        ServiceRunnerTestWait.ForPhase(workerRunner, ServiceHandoffPhase.ResponseReady);
        var failed = ServiceRunnerTestWait.AcquireResponse(workerRunner, workerClock);
        Assert.True(failed.Response.Fault.IsValid);
        ServiceRunnerTestWait.ForHandback(workerRunner);
        workerClock.AdvanceTo(failed.Response.RetryDue);
        ServiceRunnerTestWait.StartCycle(workerRunner, workerClock.Now);
        ServiceRunnerTestWait.ForPhase(workerRunner, ServiceHandoffPhase.ResponseReady);

        var recovered = ServiceRunnerTestWait.AcquireResponse(workerRunner, workerClock);

        Assert.True(recovered.Response.Succeeded);
        Assert.True(recovered.Response.RecoveredFault.IsPresent);
        Assert.Equal(failed.Response.Fault, recovered.Response.RecoveredFault.Fault);
        Assert.Equal(
            recovered.Response.EvaluationCompletedAt,
            recovered.Response.RecoveredFault.RecoveredAt);
    }

    [Fact]
    public void ActionFactsExposeContextTerminalFaultRetryAndRecovery()
    {
        var clock = new ThreadSafeTestClock(700);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("test.execution.facts.action")
        {
            ActionCount = 1,
            FaultAtIndex = 0,
            ActionCallback = () => clock.Advance(new MonotonicDuration(5)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        var firstStart = StartAndAcquire(runner, clock);
        var faulted = runner.TryExecuteOneNonBlocking(clock.Now);

        Assert.True(faulted.ActionFact.IsPresent);
        Assert.Equal(firstStart.Cycle, faulted.ActionFact.Context.Cycle);
        Assert.Equal(firstStart.Batch, faulted.ActionFact.Context.Batch);
        Assert.Equal(700, faulted.ActionFact.Context.AttemptedAt.Ticks);
        Assert.Equal(705, faulted.ActionFact.CompletedAt.Ticks);
        Assert.Equal(ServiceActionDisposition.Faulted, faulted.ActionFact.Result.Disposition);
        Assert.True(faulted.BatchTerminal);
        Assert.Equal(faulted.ActionFact.Result.Code, faulted.Receipt.ResultCode);
        Assert.Equal(faulted.ActionFact.CompletedAt, faulted.Receipt.CompletedAt);
        Assert.Equal(ServiceFaultCategory.ActionExecution, faulted.Fault.Category);
        Assert.Equal(715, faulted.RetryDue.Ticks);
        ServiceRunnerTestWait.ForHandback(runner);
        ServiceRunnerTestWait.ForCleanup(runner);

        definition.FaultAtIndex = -1;
        clock.AdvanceTo(faulted.RetryDue);
        var secondStart = StartAndAcquire(runner, clock);
        var committed = runner.TryExecuteOneNonBlocking(clock.Now);

        Assert.True(committed.ActionFact.IsPresent);
        Assert.Equal(secondStart.Cycle, committed.ActionFact.Context.Cycle);
        Assert.Equal(ServiceActionDisposition.Committed, committed.Result.Disposition);
        Assert.True(committed.BatchTerminal);
        Assert.Equal(BatchTerminalDisposition.Completed, committed.Receipt.Disposition);
        Assert.True(committed.RecoveredFault.IsPresent);
        Assert.Equal(faulted.Fault, committed.RecoveredFault.Fault);
        Assert.Equal(committed.ActionFact.CompletedAt, committed.RecoveredFault.RecoveredAt);
    }

    [Fact]
    public void LateEmergencyResponseExposesItsExactTerminalReceipt()
    {
        var clock = new ThreadSafeTestClock(900);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition("test.execution.facts.late-emergency")
        {
            ActionCount = 3,
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;
        var emergency = new EmergencyStopContext(
            new EmergencyStopEpisodeId(2),
            new EmergencyStopTransitionGeneration(4),
            EmergencyStopReason.UserRequested);

        var start = ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        Assert.True(start.Queued);
        ServiceCycleTestDeadline.ForSignal(entered, "the evaluation callback");
        Assert.False(runner.RejectForEmergencyStop(emergency, clock.Now, out _));
        clock.Advance(new MonotonicDuration(6));
        release.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);

        var acquisition = ServiceRunnerTestWait.AcquireResponse(runner, clock);

        Assert.True(acquisition.Acquired);
        Assert.True(acquisition.Response.Succeeded);
        Assert.True(acquisition.EmergencyRejected);
        Assert.True(acquisition.TerminalReceipt.IsPresent);
        Assert.Equal(start.Cycle, acquisition.TerminalReceipt.Cycle);
        Assert.Equal(start.Batch, acquisition.TerminalReceipt.Batch);
        Assert.Equal(3, acquisition.TerminalReceipt.ActionCount);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, acquisition.TerminalReceipt.ResultCode);
        Assert.Equal(emergency, acquisition.TerminalReceipt.EmergencyStop);
        Assert.Equal(906, acquisition.TerminalReceipt.CompletedAt.Ticks);
        Assert.Equal(0, definition.ActionExecutionCount);
    }

    private static void AssertSuccessfulResponse(int actionCount, bool expectTerminalReceipt)
    {
        var clock = new ThreadSafeTestClock(200);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var definition = new ExecutionServiceDefinition($"test.execution.facts.response.success.{actionCount}")
        {
            ActionCount = actionCount,
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        var runner = registration.Runner;

        var start = ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        Assert.True(start.Queued);
        ServiceCycleTestDeadline.ForSignal(entered, "the evaluation callback");
        clock.Advance(new MonotonicDuration(7));
        release.Set();
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);

        var acquisition = ServiceRunnerTestWait.AcquireResponse(runner, clock);

        Assert.True(acquisition.Acquired);
        Assert.True(acquisition.Response.Succeeded);
        Assert.False(acquisition.Response.TransientContention);
        Assert.Equal(start.Cycle, acquisition.Response.Cycle);
        Assert.Equal(start.Batch, acquisition.Response.Batch);
        Assert.Equal(200, acquisition.Response.EvaluationStartedAt.Ticks);
        Assert.Equal(207, acquisition.Response.EvaluationCompletedAt.Ticks);
        Assert.True(acquisition.Response.ProjectionContext.Publication.IsValid);
        Assert.Equal(actionCount, acquisition.Response.ActionCount);
        Assert.Equal(expectTerminalReceipt, acquisition.TerminalReceipt.IsPresent);
        if (expectTerminalReceipt)
        {
            Assert.Equal(start.Cycle, acquisition.TerminalReceipt.Cycle);
            Assert.Equal(start.Batch, acquisition.TerminalReceipt.Batch);
            Assert.Equal(BatchTerminalDisposition.Completed, acquisition.TerminalReceipt.Disposition);
            Assert.Equal(207, acquisition.TerminalReceipt.CompletedAt.Ticks);
        }
    }

    private static ServiceCycleStartAttempt StartAndAcquire(
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        var start = ServiceRunnerTestWait.StartCycle(runner, clock.Now);
        Assert.True(start.Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.AcquireResponse(runner, clock);
        return start;
    }
}

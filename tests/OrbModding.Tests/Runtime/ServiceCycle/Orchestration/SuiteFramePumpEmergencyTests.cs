using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpEmergencyTests
{
    [Fact]
    public void EmergencyBeforeResponseRejectsLateBatchWithoutNativeCall()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.late") { ActionCount = 3 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        pump.SetEmergencyStop(true);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        var stopped = pump.PumpFrame(frame++);
        while (stopped.ResponsesAcquired == 0)
            stopped = pump.PumpFrame(frame++);

        Assert.Equal(1, stopped.ResponsesAcquired);
        Assert.Equal(1, stopped.EmergencyBatchesRejected);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, registration.Runner.Snapshot.PreviousReceipt.ResultCode);
        Assert.Equal(0, registration.Runner.Snapshot.PreviousReceipt.NativeCallOutcome.NativeCallsAttempted);
    }

    [Fact]
    public void EmergencyMidDrainPreservesCommittedPrefixAndClearDoesNotResumeBatch()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.mid") { ActionCount = 3 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = PrepareBatch(pump, registration);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        pump.SetEmergencyStop(true);
        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(1, receipt.CommittedCount);
        Assert.Equal(2, receipt.ActionCount - receipt.CommittedCount);
        Assert.Equal(1, receipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(1, definition.ActionExecutionCount);

        pump.SetEmergencyStop(false);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        // One action committed before the stop, so the next capture waits on a reading of the game
        // that contains it.
        TestWorldCollector.CollectedAt(registry, frame);
        var next = pump.PumpFrame(frame);
        Assert.Equal(0, next.ActionsAttempted);
        Assert.Equal(1, next.CyclesStarted);
        Assert.Equal(1, definition.ActionExecutionCount);
    }

    [Fact]
    public void ReentrantEmergencyFinishesCurrentActionAndPreventsEveryLaterNativeCallback()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var first = new ExecutionServiceDefinition("pump.emergency.reentrant.first") { ActionCount = 2 };
        var second = new ExecutionServiceDefinition("pump.emergency.reentrant.second") { ActionCount = 2 };
        using var firstRegistration = registry.Register(
            first,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(16));
        using var secondRegistration = registry.Register(second, new LifecycleGeneration(1));
        registry.Seal();
        first.ActionCount = 0;
        second.ActionCount = 0;
        PrimeWorkerState(firstRegistration.Runner, clock);
        PrimeWorkerState(secondRegistration.Runner, clock);
        first.ActionCount = 2;
        second.ActionCount = 2;
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        first.ActionCallback = () => pump.SetEmergencyStop(true);

        var frame = 1L;
        pump.PumpFrame(frame++);
        while (firstRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty ||
               secondRegistration.Runner.HandoffPhaseHint == ServiceHandoffPhase.Empty)
            pump.PumpFrame(frame++);
        ServiceRunnerTestWait.ForPhase(firstRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(secondRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        pump.PumpFrame(frame++);
        var action = pump.PumpFrame(frame);

        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(2, action.EmergencyBatchesRejected);
        Assert.Equal(1, first.ActionExecutionCount);
        Assert.Equal(0, second.ActionExecutionCount);
        Assert.Equal(1, firstRegistration.Runner.Snapshot.PreviousReceipt.CommittedCount);
        Assert.Equal(0, secondRegistration.Runner.Snapshot.PreviousReceipt.CommittedCount);
    }

    private static void PrimeWorkerState(
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ThreadSafeTestClock clock)
    {
        Assert.True(runner.TryStartCycle(clock.Now).Queued);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
        Assert.True(runner.TryAcquireResponse());
        Assert.False(runner.Snapshot.Fault.IsValid);
    }

    [Fact]
    public void ReentrantEmergencyDuringCapturePreventsLaterShouldStartCallbacks()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var first = new ExecutionServiceDefinition("pump.emergency.capture.first") { ActionCount = 1 };
        var second = new ExecutionServiceDefinition("pump.emergency.capture.second") { ActionCount = 1 };
        using var firstRegistration = registry.Register(first, new LifecycleGeneration(1));
        using var secondRegistration = registry.Register(second, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        first.ShouldStartCallback = () => pump.SetEmergencyStop(true);

        var capture = pump.PumpFrame(1);
        Assert.Equal(1, capture.CyclesStarted);
        Assert.Equal(1, first.StartCount);
        Assert.Equal(0, second.StartCount);
        Assert.Equal(0, first.ActionExecutionCount);
        Assert.Equal(0, second.ActionExecutionCount);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void HundredThousandReferenceActionsRejectInConstantUnityWorkAndClearOnWorker()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.huge") { ActionCount = 100_000 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        PrepareBatch(pump, registration);
        pump.SetEmergencyStop(true);
        var beforeCleanup = registration.Runner.Snapshot;
        Assert.Equal(1, beforeCleanup.Handoff.CleanupRequestCount);
        Assert.Equal(99_999, beforeCleanup.PreviousReceipt.UntouchedSuffixCount);
        Assert.Equal(0, definition.ActionExecutionCount);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        var afterCleanup = registration.Runner.Snapshot;
        Assert.Equal(1, afterCleanup.Handoff.CleanupAcknowledgementCount);
        Assert.Equal(afterCleanup.WorkerThreadId, afterCleanup.LastCleanupThreadId);
    }

    [Fact]
    public void EmergencyDoesNotRewritePinnedConfigurationAndFreshCycleUsesLatestSave()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.config") { ActionCount = 2 };
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(1));
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        registry.Configuration.Publish(TestSuiteConfiguration.WithSetting(2));
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        pump.PumpFrame(frame++);
        Assert.Equal(1, definition.LastExecutedSetting);
        pump.SetEmergencyStop(true);
        pump.SetEmergencyStop(false);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        TestWorldCollector.CollectedAt(registry, frame);
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        pump.PumpFrame(frame);
        Assert.Equal(2, definition.LastExecutedSetting);
    }

    /// <summary>
    /// The pump takes the emergency stop from the configuration slot, and the rejections it causes
    /// are counted as the frame's own.
    /// </summary>
    /// <remarks>
    /// Nothing pushes the stop in: the flag is published like any other setting and read at the top
    /// of every frame, so the state the pump is in cannot drift from what the suite is configured to
    /// do. The rejection count is the load-bearing half — engaging inside the frame through the
    /// ordinary control path would reject the batches before the frame's own rejection step ran and
    /// report zero, which reads as "nothing was stopped".
    /// </remarks>
    [Fact]
    public void ThePumpTakesItsEmergencyStopFromTheConfigurationSlot()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new EmergencyStopServiceDefinition("pump.emergency.configured")
        {
            ActionCount = 3,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        Assert.False(pump.IsEmergencyStopEngaged);

        registry.ConfigurationPublication.Publish(TestSuiteConfiguration.WithEmergencyDisable(true));
        var stopped = pump.PumpFrame(frame++);

        Assert.True(pump.IsEmergencyStopEngaged);
        Assert.Equal(1, stopped.EmergencyBatchesRejected);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(
            CommonActionResultCodes.EmergencyStop,
            registration.Runner.Snapshot.PreviousReceipt.ResultCode);

        registry.ConfigurationPublication.Publish(TestSuiteConfiguration.WithEmergencyDisable(false));
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        var cleared = pump.PumpFrame(frame);

        Assert.False(pump.IsEmergencyStopEngaged);
        Assert.Equal(0, cleared.EmergencyBatchesRejected);
    }

    /// <summary>
    /// The configuration clears only the stop it engaged: a shutdown stop survives every frame that
    /// reads a configuration saying the suite is not disabled.
    /// </summary>
    /// <remarks>
    /// This used to be stated as "a configuration that says nothing leaves the stop alone", which is
    /// no longer a shape a configuration can have — the suite has one configuration record and its
    /// emergency-disable flag always answers, defaulting to false. So "says nothing" and "says not
    /// disabled" became the same reading, and a pump that acted on it would cancel a shutdown stop on
    /// the next frame and let prepared work run against a game that is going away. The reason on the
    /// episode is what keeps them apart.
    /// </remarks>
    [Fact]
    public void AConfiguredReadingDoesNotClearAShutdownStop()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.shutdown-stop") { ActionCount = 1 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        pump.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
        pump.PumpFrame(frame++);

        Assert.True(pump.IsEmergencyStopEngaged);

        registry.ConfigurationPublication.Publish(TestSuiteConfiguration.WithEmergencyDisable(false));
        pump.PumpFrame(frame);

        Assert.True(pump.IsEmergencyStopEngaged);
        Assert.Equal(EmergencyStopReason.SuiteShutdown, pump.ActiveEmergency.Reason);
    }

    [Fact]
    public void AnEmergencyBetweenFramesRejectsTheActiveBatchOnceAndTheNextFrameRejectsNothing()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.emergency.between-frames")
        {
            ActionCount = 3,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = ServiceRunnerTestWait.PrepareBatch(pump, registration);

        pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        pump.SetEmergencyStop(true, EmergencyStopReason.UserRequested);
        var nextFrame = pump.PumpFrame(frame);

        Assert.Equal(0, nextFrame.EmergencyBatchesRejected);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(BatchTerminalDisposition.Rejected, registration.Runner.Snapshot.PreviousReceipt.Disposition);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, registration.Runner.Snapshot.PreviousReceipt.ResultCode);
        Assert.Equal(
            EmergencyStopReason.SafetyInterlock,
            registration.Runner.Snapshot.PreviousReceipt.EmergencyStop.Reason);
    }

    private static long PrepareBatch(
        SuiteFramePump pump,
        ServiceRegistration<ExecutionState, ExecutionAction> registration)
    {
        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        return frame;
    }
}

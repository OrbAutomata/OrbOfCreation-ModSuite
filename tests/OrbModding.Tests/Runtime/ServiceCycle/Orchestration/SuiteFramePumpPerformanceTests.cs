using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.TestSupport;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

[Trait("Category", "PerformanceSimulation")]
public sealed class SuiteFramePumpPerformanceTests
{
    [Fact]
    public void WarmEmptyPumpProducesValueReportsWithoutManagedAllocation()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        pump.PumpFrame(1);
        var frame = 2L;

        var allocated = AllocationProbe.MeasureRepeated(10_000, () => pump.PumpFrame(frame++));

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void WarmInCapacityActionPassAllocatesNoManagedBytesAndDoesNotTimeGate()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.performance.actions") { ActionCount = 256 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frameIdentity = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frameIdentity);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frameIdentity);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frameIdentity);
        pump.PumpFrame(frameIdentity++);

        var actionFrames = 0;
        var allocated = AllocationProbe.MeasureRepeated(
            100,
            () => actionFrames += pump.PumpFrame(frameIdentity++).ActionsAttempted);

        Assert.Equal(0, allocated);
        Assert.Equal(200, actionFrames);
        Assert.Equal(201, definition.ActionExecutionCount);
    }

    [Fact]
    public void WarmRegisteredWaitingSlotScanAllocatesNoManagedBytes()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.performance.waiting")
        {
            StartDecision = ServiceStartDecision.Wait(
                CommonServiceDecisionCodes.NotReady,
                WakePolicy.AfterDecision(new MonotonicDuration(100_000))),
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        pump.PumpFrame(1);
        pump.PumpFrame(2);
        var frame = 3L;

        var allocated = AllocationProbe.MeasureRepeated(10_000, () => pump.PumpFrame(frame++));

        Assert.Equal(0, allocated);
        // Asked once, on the frame that set the wake; the ten thousand scans after it cost nothing
        // and never reach the service.
        Assert.Equal(1, definition.StartCount);
    }

    [Fact]
    public void WarmSuccessfulCycleTransitionsAllocateNoManagedBytes()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.performance.full-cycle") { ActionCount = 1 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        RunSuccessfulCycle(pump, registration.Runner, ref frame);
        // The action changed the game, so the next cycle waits for a reading that contains it.
        TestWorldCollector.CollectedAt(registry, frame);

        var captureAllocation = MeasurePump(pump, ref frame, out var capture);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        var responseAllocation = MeasurePump(pump, ref frame, out var response);
        var terminalAllocation = MeasurePump(pump, ref frame, out var terminal);
        var handbackAllocation = MeasurePump(pump, ref frame, out var handback);

        Assert.Equal(1, capture.CyclesStarted);
        Assert.Equal(1, response.ResponsesAcquired);
        Assert.Equal(1, terminal.ActionsAttempted);
        Assert.Equal(0, handback.CyclesStarted);
        Assert.Equal(0, captureAllocation);
        Assert.Equal(0, responseAllocation);
        Assert.Equal(0, terminalAllocation);
        Assert.Equal(0, handbackAllocation);
    }

    [Fact]
    public void WarmZeroActionTerminalAndDeferredHandbackAllocateNoManagedBytes()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.performance.zero") { ActionCount = 0 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        RunZeroActionCycle(pump, registration.Runner, ref frame);

        var captureAllocation = MeasurePump(pump, ref frame, out var capture);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        var responseAllocation = MeasurePump(pump, ref frame, out var response);
        var handbackAllocation = MeasurePump(pump, ref frame, out var handback);

        Assert.Equal(1, capture.CyclesStarted);
        Assert.Equal(1, response.ResponsesAcquired);
        Assert.Equal(0, response.ActionsAttempted);
        Assert.Equal(0, handback.CyclesStarted);
        Assert.Equal(0, captureAllocation);
        Assert.Equal(0, responseAllocation);
        Assert.Equal(0, handbackAllocation);
    }

    [Fact]
    public void WarmRejectionAndWorkerCleanupHandbackAllocateNoManagedBytes()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.performance.reject")
        {
            ActionCount = 3,
            RejectAtIndex = 0,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        RunRejectedCycle(pump, registration.Runner, ref frame);
        // Rejection proves that the pinned world diverged from native reality. Warm the next
        // cycle only after the collector has published facts from beyond that attempt.
        TestWorldCollector.CollectedAt(registry, frame);

        var captureAllocation = MeasurePump(pump, ref frame, out var capture);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        var responseAllocation = MeasurePump(pump, ref frame, out var response);
        var rejectionAllocation = MeasurePump(pump, ref frame, out var rejection);
        var handbackAllocation = MeasurePump(pump, ref frame, out var handback);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);

        Assert.Equal(1, capture.CyclesStarted);
        Assert.Equal(1, response.ResponsesAcquired);
        Assert.Equal(1, rejection.ActionsAttempted);
        Assert.Equal(BatchTerminalDisposition.Rejected, registration.Runner.Snapshot.PreviousReceipt.Disposition);
        Assert.Equal(0, handback.ActionsAttempted);
        Assert.Equal(0, captureAllocation);
        Assert.Equal(0, responseAllocation);
        Assert.Equal(0, rejectionAllocation);
        Assert.Equal(0, handbackAllocation);
    }

    [Fact]
    public void WarmEmergencyResponseRejectionAllocatesNoManagedBytesOnThePumpThread()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.performance.emergency") { ActionCount = 3 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        RunEmergencyResponseRejection(pump, registration.Runner, ref frame);

        CaptureAndWaitForResponse(pump, registration.Runner, ref frame);
        pump.SetEmergencyStop(true);
        var rejectionAllocation = MeasurePump(pump, ref frame, out var rejection);
        pump.SetEmergencyStop(false);
        var handbackAllocation = MeasurePump(pump, ref frame, out var handback);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);

        Assert.Equal(1, rejection.ResponsesAcquired);
        Assert.Equal(1, rejection.EmergencyBatchesRejected);
        Assert.Equal(0, rejection.ActionsAttempted);
        Assert.Equal(0, handback.ActionsAttempted);
        Assert.Equal(0, rejectionAllocation);
        Assert.Equal(0, handbackAllocation);
        Assert.Equal(0, definition.ActionExecutionCount);
    }

    private static void RunSuccessfulCycle(
        SuiteFramePump pump,
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ref long frame)
    {
        CaptureAndWaitForResponse(pump, runner, ref frame);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        pump.PumpFrame(frame++);
    }

    private static void RunZeroActionCycle(
        SuiteFramePump pump,
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ref long frame)
    {
        CaptureAndWaitForResponse(pump, runner, ref frame);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        pump.PumpFrame(frame++);
    }

    private static void RunRejectedCycle(
        SuiteFramePump pump,
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ref long frame)
    {
        CaptureAndWaitForResponse(pump, runner, ref frame);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        pump.PumpFrame(frame++);
        ServiceRunnerTestWait.ForCleanup(runner);
    }

    private static void RunEmergencyResponseRejection(
        SuiteFramePump pump,
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ref long frame)
    {
        CaptureAndWaitForResponse(pump, runner, ref frame);
        pump.SetEmergencyStop(true);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        pump.SetEmergencyStop(false);
        pump.PumpFrame(frame++);
        ServiceRunnerTestWait.ForCleanup(runner);
    }

    private static void CaptureAndWaitForResponse(
        SuiteFramePump pump,
        ServiceRunner<ExecutionState, ExecutionAction> runner,
        ref long frame)
    {
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, runner, ref frame);
        ServiceRunnerTestWait.ForPhase(runner, ServiceHandoffPhase.ResponseReady);
    }

    private static long MeasurePump(
        SuiteFramePump pump,
        ref long frame,
        out SuiteFramePumpReport report)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        report = pump.PumpFrame(frame++);
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}

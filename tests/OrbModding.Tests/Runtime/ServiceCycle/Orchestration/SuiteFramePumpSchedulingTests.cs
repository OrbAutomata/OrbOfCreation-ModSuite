using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpSchedulingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NegativeFrameIdentityIsRejectedBeforeWorkWithOrWithoutTracing(bool tracing)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.negative-frame");
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        var recorder = tracing
            ? new ServiceCycleSemanticRecorder(new ServiceCycleTraceSessionId(51), 32, 1)
            : null;
        using var pump = new SuiteFramePump(registry, recorder);
        TestWorldCollector.CollectedAtActivation(registry);

        Assert.Throws<ArgumentOutOfRangeException>(() => pump.PumpFrame(-1));

        Assert.False(pump.HasAcceptedFrame);
        Assert.Equal(0, definition.StartCount);
        Assert.False(pump.SemanticTrace?.EmissionFaulted ?? false);
    }

    [Fact]
    public void DuplicateAndStaleFramesHaveNoEffectsAndBackloggedServicesActOnceEach()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(3, clock);
        var first = new ExecutionServiceDefinition("pump.once.first") { ActionCount = 3 };
        var second = new ExecutionServiceDefinition("pump.once.second") { ActionCount = 3 };
        var third = new ExecutionServiceDefinition("pump.once.third") { ActionCount = 3 };
        using var firstRegistration = registry.Register(first, new LifecycleGeneration(1));
        using var secondRegistration = registry.Register(second, new LifecycleGeneration(1));
        using var thirdRegistration = registry.Register(third, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 10L;
        ServiceRunnerTestWait.PrimeWorkerStates(
            pump,
            clock,
            ref frame,
            (first, firstRegistration.Runner),
            (second, secondRegistration.Runner),
            (third, thirdRegistration.Runner));
        TestWorldCollector.CollectedAt(registry, frame);

        Assert.Equal(3, pump.PumpFrame(frame++).CyclesStarted);
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => firstRegistration.Runner.HandoffPhaseHint != ServiceHandoffPhase.Empty &&
                secondRegistration.Runner.HandoffPhaseHint != ServiceHandoffPhase.Empty &&
                thirdRegistration.Runner.HandoffPhaseHint != ServiceHandoffPhase.Empty);
        ServiceRunnerTestWait.ForPhase(firstRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(secondRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(thirdRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        var responseFrame = pump.PumpFrame(frame++);
        Assert.Equal(3, responseFrame.ResponsesAcquired);
        Assert.Equal(0, responseFrame.ActionsAttempted);

        var actionFrame = pump.PumpFrame(frame++);
        Assert.Equal(3, actionFrame.ActionsAttempted);
        Assert.Equal(1, first.ActionExecutionCount);
        Assert.Equal(1, second.ActionExecutionCount);
        Assert.Equal(1, third.ActionExecutionCount);

        var lastAccepted = frame - 1;
        var duplicate = pump.PumpFrame(lastAccepted);
        var stale = pump.PumpFrame(lastAccepted - 1);
        Assert.False(duplicate.Accepted);
        Assert.False(stale.Accepted);
        Assert.Equal(1, first.ActionExecutionCount);
        Assert.Equal(1, second.ActionExecutionCount);
        Assert.Equal(1, third.ActionExecutionCount);
        Assert.Equal(lastAccepted, pump.LastAcceptedFrameIdentity);
    }

    [Fact]
    public void BoundedServiceDrainsItsSliceWithoutTakingTheNextServicesTurn()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, clock);
        var sliced = new ExecutionServiceDefinition("pump.slice.sixteen") { ActionCount = 20 };
        var single = new ExecutionServiceDefinition("pump.slice.single") { ActionCount = 3 };
        using var slicedRegistration = registry.Register(
            sliced,
            new LifecycleGeneration(1),
            ServiceActionDispatchPolicy.Bounded(16));
        using var singleRegistration = registry.Register(
            single,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PrimeWorkerStates(
            pump,
            clock,
            ref frame,
            (sliced, slicedRegistration.Runner),
            (single, singleRegistration.Runner));
        TestWorldCollector.CollectedAt(registry, frame);

        Assert.Equal(2, pump.PumpFrame(frame++).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(slicedRegistration);
        ServiceRunnerTestWait.ForResponse(singleRegistration);
        Assert.Equal(2, pump.PumpFrame(frame++).ResponsesAcquired);

        var firstSlice = pump.PumpFrame(frame++);
        Assert.Equal(17, firstSlice.ActionsAttempted);
        Assert.Equal(16, sliced.ActionExecutionCount);
        Assert.Equal(1, single.ActionExecutionCount);

        var secondSlice = pump.PumpFrame(frame);
        Assert.Equal(5, secondSlice.ActionsAttempted);
        Assert.Equal(20, sliced.ActionExecutionCount);
        Assert.Equal(2, single.ActionExecutionCount);
    }

    [Fact]
    public void InvalidActionDispatchPolicyIsRejectedBeforeConstruction()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new ExecutionServiceDefinition("pump.slice.invalid");

        Assert.Throws<ArgumentException>(() => registry.Register(
            definition,
            new LifecycleGeneration(1),
            default));

        Assert.Equal(0, definition.StateCreateCount);
    }

    [Fact]
    public void ResponseActionAndTerminalRecaptureRemainOnSeparateFrames()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.boundaries") { ActionCount = 1 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = 1L;
        var capture = ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        Assert.Equal(1, capture.CyclesStarted);
        Assert.Equal(0, capture.ActionsAttempted);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);

        var response = ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        Assert.Equal(1, response.ResponsesAcquired);
        Assert.Equal(0, response.ActionsAttempted);
        Assert.Equal(0, response.CyclesStarted);

        var terminalAction = pump.PumpFrame(frame++);
        Assert.Equal(1, terminalAction.ActionsAttempted);
        Assert.Equal(0, terminalAction.CyclesStarted);
        Assert.Equal(1, definition.StartCount);

        // The action changed the game, so the next capture waits on a reading that contains it.
        TestWorldCollector.CollectedAt(registry, frame);
        var nextCapture = ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        Assert.Equal(1, nextCapture.CyclesStarted);
        Assert.Equal(2, definition.StartCount);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    public void FirstOrMiddleTerminalOutcomeAbortsUntouchedSuffix(int terminalIndex, bool fault)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition($"pump.abort.{terminalIndex}.{fault}")
        {
            ActionCount = 4,
            RejectAtIndex = fault ? -1 : terminalIndex,
            FaultAtIndex = fault ? terminalIndex : -1,
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
        for (var index = 0; index <= terminalIndex; index++)
            Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(fault ? BatchTerminalDisposition.Faulted : BatchTerminalDisposition.Rejected, receipt.Disposition);
        Assert.Equal(terminalIndex, receipt.TerminalIndex);
        Assert.Equal(4 - terminalIndex - 1, receipt.UntouchedSuffixCount);
        Assert.Equal(terminalIndex + 1, definition.ActionExecutionCount);
        Assert.Equal(0, pump.PumpFrame(frame).ActionsAttempted);
    }

    [Fact]
    public void RotationAdvancesAcrossZeroActionRejectedAndTombstoneSlots()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(4, clock);
        var active = new ExecutionServiceDefinition("pump.rotate.active") { ActionCount = 2 };
        var empty = new ExecutionServiceDefinition("pump.rotate.empty") { ActionCount = 0 };
        var rejected = new ExecutionServiceDefinition("pump.rotate.rejected") { ActionCount = 2, RejectAtIndex = 0 };
        using var activeRegistration = registry.Register(active, new LifecycleGeneration(1));
        using var emptyRegistration = registry.Register(empty, new LifecycleGeneration(1));
        using var rejectedRegistration = registry.Register(rejected, new LifecycleGeneration(1));
        var tombstone = registry.Register(
            new ExecutionServiceDefinition("pump.rotate.tombstone"),
            new LifecycleGeneration(1));
        tombstone.Dispose();
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var frame = 1L;
        Assert.Equal(0, pump.PumpFrame(frame++).StartingOrdinal);
        TestWorldCollector.CollectedAtActivation(registry);
        ServiceRunnerTestWait.PrimeWorkerStates(
            pump,
            clock,
            ref frame,
            (active, activeRegistration.Runner),
            (empty, emptyRegistration.Runner),
            (rejected, rejectedRegistration.Runner));
        TestWorldCollector.CollectedAt(registry, frame);

        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => activeRegistration.Runner.HandoffPhaseHint != ServiceHandoffPhase.Empty &&
                emptyRegistration.Runner.HandoffPhaseHint != ServiceHandoffPhase.Empty &&
                rejectedRegistration.Runner.HandoffPhaseHint != ServiceHandoffPhase.Empty);
        ServiceRunnerTestWait.ForPhase(activeRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(emptyRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceRunnerTestWait.ForPhase(rejectedRegistration.Runner, ServiceHandoffPhase.ResponseReady);
        var seenStarts = new bool[4];
        for (var index = 0; index < 4; index++)
            seenStarts[pump.PumpFrame(frame++).StartingOrdinal] = true;
        Assert.All(seenStarts, Assert.True);
        Assert.Equal(2, active.ActionExecutionCount);
        Assert.Equal(1, rejected.ActionExecutionCount);
    }

    [Fact]
    public void SealedRegistryCanBeClaimedByExactlyOneOwnerThreadPump()
    {
        using var unsealed = new ServiceCycleRegistry(1);
        Assert.Throws<InvalidOperationException>(() => new SuiteFramePump(unsealed));

        using var registry = new ServiceCycleRegistry(1);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        Assert.Throws<InvalidOperationException>(() => new SuiteFramePump(registry));

        Exception? observed = null;
        var thread = new System.Threading.Thread(() =>
        {
            try { pump.PumpFrame(1); }
            catch (Exception ex) { observed = ex; }
        });
        thread.Start();
        Assert.True(
            thread.Join(ServiceCycleTestDeadline.Value),
            "The foreign-thread pump probe did not complete.");
        Assert.IsType<InvalidOperationException>(observed);
    }
}

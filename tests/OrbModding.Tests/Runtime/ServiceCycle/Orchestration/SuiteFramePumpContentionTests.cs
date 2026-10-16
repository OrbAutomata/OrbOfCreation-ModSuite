using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpContentionTests
{
    [Fact]
    public void SignalOnlyDisposalDoesNotEnterContendedHandoffGate()
    {
        using var registry = new ServiceCycleRegistry(1);
        var definition = new ExecutionServiceDefinition("pump.contention.dispose") { ActionCount = 1 };
        var registration = registry.Register(definition, new LifecycleGeneration(1));
        var runner = registration.Runner;
        using var contention = new HandoffGateContention(runner);
        contention.Acquire();

        HeldGate.AssertReturnsWhileHeld(() => registration.Dispose(), contention.Release);

        contention.Release();
        Assert.True(System.Threading.SpinWait.SpinUntil(
            () => runner.HandoffPhaseHint == ServiceHandoffPhase.Stopped,
            ServiceCycleTestDeadline.Value));
    }

    /// <summary>
    /// A start probe that cannot take the handoff gate starts nothing, does not consult the service
    /// at all, and does not block; the next frame picks the service up.
    /// </summary>
    /// <remarks>
    /// The pump takes that gate with a zero timeout precisely so a worker can never park the main
    /// thread — and a freshly started worker holds the same gate while it parks itself. A caller that
    /// pumps exactly one frame and expects a started cycle is therefore racing the worker rather than
    /// reading a contract, which is what made one pump test fail about one run in thirty on a loaded
    /// machine.
    /// </remarks>
    [Fact]
    public void StartProbeContentionStartsNothingAndRecoversOnTheNextFrame()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.contention.start") { ActionCount = 1 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var contention = new HandoffGateContention(registration.Runner);
        contention.Acquire();

        var contended = HeldGate.AssertReturnsWhileHeld(() => pump.PumpFrame(1), contention.Release);

        Assert.Equal(0, contended.CyclesStarted);
        Assert.Equal(0, definition.StartCount);
        contention.Release();
        ServiceRunnerTestWait.ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.Empty && value.WorkerWaitCount > 0,
            "the initial worker wait");

        var recovered = pump.PumpFrame(2);
        Assert.Equal(1, recovered.CyclesStarted);
        Assert.Equal(1, definition.StartCount);
    }

    [Fact]
    public void RequestPublicationContentionDefersWithoutRecaptureOrBlocking()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.contention.request") { ActionCount = 1 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var contention = new HandoffGateContention(registration.Runner);
        definition.ShouldStartCallback = contention.Acquire;

        var capture = HeldGate.AssertReturnsWhileHeld(() => pump.PumpFrame(1), contention.Release);

        Assert.Equal(1, capture.CyclesStarted);
        Assert.Equal(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        contention.Release();
        definition.ShouldStartCallback = null;

        var publish = pump.PumpFrame(2);
        Assert.Equal(0, publish.CyclesStarted);
        Assert.Equal(1, definition.StartCount);
        Assert.NotEqual(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
    }

    [Fact]
    public void NormalTerminalHandbackContentionDefersWithoutDuplicateNativeCall()
    {
        var fixture = CreateBatchFixture("pump.contention.completed", actionCount: 1);
        using var registry = fixture.Registry;
        using var registration = fixture.Registration;
        using var pump = fixture.Pump;
        using var contention = new HandoffGateContention(registration.Runner);
        fixture.Definition.ActionCallback = contention.Acquire;

        var action = HeldGate.AssertReturnsWhileHeld(() => pump.PumpFrame(fixture.NextFrame++), contention.Release);

        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(1, fixture.Definition.ActionExecutionCount);
        Assert.Equal(ServiceHandoffPhase.MainOwnedBatch, registration.Runner.HandoffPhaseHint);
        contention.Release();
        fixture.Definition.ActionCallback = null;

        var completion = pump.PumpFrame(fixture.NextFrame++);
        Assert.Equal(0, completion.ActionsAttempted);
        Assert.Equal(0, completion.CyclesStarted);
        Assert.Equal(1, registration.Runner.Snapshot.PreviousReceipt.CommittedCount);
        Assert.Equal(1, fixture.Definition.ActionExecutionCount);
    }

    [Fact]
    public void RejectionHandbackContentionDefersWorkerCleanupWithoutDuplicateTerminalization()
    {
        var fixture = CreateBatchFixture("pump.contention.rejected", actionCount: 4, rejectAt: 0);
        using var registry = fixture.Registry;
        using var registration = fixture.Registration;
        using var pump = fixture.Pump;
        using var contention = new HandoffGateContention(registration.Runner);
        fixture.Definition.ActionCallback = contention.Acquire;

        var action = HeldGate.AssertReturnsWhileHeld(() => pump.PumpFrame(fixture.NextFrame++), contention.Release);

        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(1, fixture.Definition.ActionExecutionCount);
        contention.Release();
        fixture.Definition.ActionCallback = null;
        Assert.Equal(3, registration.Runner.Snapshot.PreviousReceipt.UntouchedSuffixCount);
        Assert.Equal(0, registration.Runner.Snapshot.Handoff.CleanupRequestCount);

        pump.PumpFrame(fixture.NextFrame++);
        Assert.Equal(1, registration.Runner.Snapshot.Handoff.CleanupRequestCount);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        Assert.Equal(1, fixture.Definition.ActionExecutionCount);
    }

    [Fact]
    public void EmergencyHandbackContentionReturnsImmediatelyAndRejectsExactlyOnce()
    {
        var fixture = CreateBatchFixture("pump.contention.emergency", actionCount: 100_000);
        using var registry = fixture.Registry;
        using var registration = fixture.Registration;
        using var pump = fixture.Pump;
        using var contention = new HandoffGateContention(registration.Runner);
        contention.Acquire();

        HeldGate.AssertReturnsWhileHeld(() => pump.SetEmergencyStop(true), contention.Release);

        Assert.Equal(0, fixture.Definition.ActionExecutionCount);
        contention.Release();
        Assert.Equal(CommonActionResultCodes.EmergencyStop, registration.Runner.Snapshot.PreviousReceipt.ResultCode);
        Assert.Equal(0, registration.Runner.Snapshot.Handoff.CleanupRequestCount);

        pump.PumpFrame(fixture.NextFrame++);
        Assert.Equal(1, registration.Runner.Snapshot.Handoff.CleanupRequestCount);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        Assert.Equal(0, fixture.Definition.ActionExecutionCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroAndFaultResponseHandbackContentionDefersWithoutRepublishing(bool fault)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition($"pump.contention.response.{fault}")
        {
            ActionCount = fault ? 2 : 0,
        };
        if (fault) definition.FailNextEvaluations(1);
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        Assert.Equal(ServiceHandoffPhase.MainOwnedBatch, registration.Runner.HandoffPhaseHint);
        using var contention = new HandoffGateContention(registration.Runner);
        contention.Acquire();

        var deferred = HeldGate.AssertReturnsWhileHeld(() => pump.PumpFrame(frame++), contention.Release);

        Assert.Equal(0, deferred.CyclesStarted);
        Assert.Equal(1, definition.EvaluationCount);
        Assert.Equal(ServiceHandoffPhase.MainOwnedBatch, registration.Runner.HandoffPhaseHint);
        contention.Release();

        var completed = pump.PumpFrame(frame);
        Assert.Equal(0, completed.CyclesStarted);
        Assert.Equal(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        Assert.Equal(1, definition.EvaluationCount);
        if (fault)
            Assert.Equal(ServiceFaultCategory.Evaluation, registration.Runner.Snapshot.Fault.Category);
        else
            Assert.Equal(0, registration.Runner.Snapshot.PreviousReceipt.ActionCount);
    }

    private static BatchFixture CreateBatchFixture(string id, int actionCount, int rejectAt = -1)
    {
        var clock = new ThreadSafeTestClock(100);
        var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition(id)
        {
            ActionCount = actionCount,
            RejectAtIndex = rejectAt,
        };
        var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        return new BatchFixture(registry, registration, pump, definition, frame);
    }

    private sealed class BatchFixture
    {
        internal BatchFixture(
            ServiceCycleRegistry registry,
            ServiceRegistration<ExecutionState, ExecutionAction> registration,
            SuiteFramePump pump,
            ExecutionServiceDefinition definition,
            long nextFrame)
        {
            Registry = registry;
            Registration = registration;
            Pump = pump;
            Definition = definition;
            NextFrame = nextFrame;
        }

        internal ServiceCycleRegistry Registry { get; }
        internal ServiceRegistration<ExecutionState, ExecutionAction> Registration { get; }
        internal SuiteFramePump Pump { get; }
        internal ExecutionServiceDefinition Definition { get; }
        internal long NextFrame { get; set; }
    }
}

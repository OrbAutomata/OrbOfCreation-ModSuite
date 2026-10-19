using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Diagnostics;

public sealed class ServiceCycleDiagnosticsEvidenceTests
{
    [Fact]
    public void EvaluatingTimingPublicationContentionIsExplicitlyReported()
    {
        var snapshot = ServiceRunnerDiagnosticsAssembler<
            ExecutionState,
            ExecutionAction>.SelectEvaluationTiming(
                workerTimingReadSucceeded: false,
                default,
                requestSequence: 1,
                default);

        Assert.Equal(
            ServiceRunnerEvaluationTimingAvailability.PublicationContended,
            snapshot.Availability);
        Assert.False(snapshot.Fact.IsPresent);
    }

    [Fact]
    public void RetainedStorageRemainsVisibleWhileIdleAndOnRetiringPosition()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("diagnostics.storage") { ActionCount = 5 },
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PumpToResponse(pump, registration.Runner, ref frame);
        for (var index = 0; index < 5; index++)
            Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        ServiceRunnerTestWait.PumpToHandback(pump, registration.Runner, ref frame);

        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        Assert.Equal(ServiceCycleOperationalPhase.Idle, buffer[0].Phase);
        Assert.False(buffer[0].ActiveBatch.IsPresent);
        Assert.Equal(ServiceCycleStorageDiagnosticsAvailability.Exact, buffer[0].Storage.Availability);
        Assert.True(buffer[0].Storage.Capacity >= 5);
        Assert.Equal(5, buffer[0].Storage.HighWater);
        Assert.Equal(buffer[0].Storage.Capacity, buffer[0].Storage.RetainedSlots);

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        var retired = buffer[0].Lifecycle.Position0.Lifecycle == new LifecycleGeneration(1)
            ? buffer[0].Lifecycle.Position0
            : buffer[0].Lifecycle.Position1;
        Assert.Equal(ServiceRunnerPositionState.Retiring, retired.State);
        Assert.True(retired.Storage.HasEvidence);
        Assert.True(retired.Storage.Capacity >= 5);
        Assert.Equal(5, retired.Storage.HighWater);
    }

    [Fact]
    public void TimingAndLastFactsAreRecordedAtTheirCommitSites()
    {
        var clock = new ThreadSafeTestClock(100);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("diagnostics.timing")
        {
            ActionCount = 1,
            EvaluationEntered = entered,
            EvaluationRelease = release,
            ShouldStartCallback = () => clock.Advance(new MonotonicDuration(2)),
            ActionCallback = () => clock.Advance(new MonotonicDuration(4)),
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
        clock.Advance(new MonotonicDuration(7));
        release.Set();
        ServiceRunnerTestWait.PumpToResponse(pump, registration.Runner, ref frame);
        Assert.Equal(1, pump.PumpFrame(frame++).ActionsAttempted);
        ServiceRunnerTestWait.PumpToHandback(pump, registration.Runner, ref frame);

        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        var snapshot = buffer[0];
        Assert.Equal(ServiceCycleOperationalPhase.Idle, snapshot.Phase);
        Assert.True(snapshot.LastStartDecision.IsPresent);
        Assert.True(snapshot.LastStartDecision.Decision.ShouldStart);
        Assert.Equal(102, snapshot.LastStartDecision.ObservedAt.Ticks);
        Assert.False(snapshot.LastCapture.IsPresent);
        Assert.True(snapshot.Timing.HasEvaluation);
        Assert.True(snapshot.Timing.EvaluationComplete);
        Assert.Equal(102, snapshot.Timing.EvaluationStartedAt.Ticks);
        Assert.Equal(109, snapshot.Timing.EvaluationCompletedAt.Ticks);
        Assert.Equal(7, snapshot.Timing.EvaluationDuration.Ticks);
        Assert.Equal(11, snapshot.Timing.EvaluationAge.Ticks);
        Assert.True(snapshot.LastAction.IsPresent);
        Assert.Equal(109, snapshot.LastAction.Context.AttemptedAt.Ticks);
        Assert.Equal(113, snapshot.LastAction.CompletedAt.Ticks);
        Assert.Equal(4, snapshot.LastAction.Duration.Ticks);
        Assert.Equal(ServiceActionDisposition.Committed, snapshot.LastAction.Result.Disposition);
        Assert.True(snapshot.Timing.WakeIsLate);
        Assert.Equal(4, snapshot.Timing.WakeLateness.Ticks);
    }

    [Fact]
    public void RetryAndEmergencyOperationalPhasesDoNotMasqueradeAsIdle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("diagnostics.operational");
        definition.FailNextEvaluations(1);
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];

        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PumpToResponse(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.PumpToHandback(pump, registration.Runner, ref frame);
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        Assert.Equal(ServiceCycleOperationalPhase.RetryBackoff, buffer[0].Phase);

        clock.AdvanceTo(buffer[0].NextWakeDue);
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        Assert.Equal(ServiceCycleOperationalPhase.Faulted, buffer[0].Phase);

        pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        ServiceCycleDiagnostics.CopyServices(pump, buffer);
        Assert.Equal(ServiceCycleOperationalPhase.EmergencyStopped, buffer[0].Phase);
    }

    [Fact]
    public void ReentrantEmergencyTransitionsKeepFirstCausativeContextOnReceipt()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("diagnostics.emergency.reentrant") { ActionCount = 2 };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        definition.ActionCallback = () =>
        {
            pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
            pump.SetEmergencyStop(false);
            pump.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
            pump.SetEmergencyStop(false);
        };

        var frame = 1L;
        ServiceCyclePumpTestWait.PumpUntil(
            pump,
            ref frame,
            () => registration.Runner.HandoffPhaseHint == ServiceHandoffPhase.MainOwnedBatch);
        var report = pump.PumpFrame(frame++);
        var receipt = registration.Runner.Snapshot.PreviousReceipt;

        Assert.Equal(1, report.ActionsAttempted);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.True(receipt.HasEmergencyStopContext);
        Assert.Equal(1, receipt.EmergencyStop.Episode.Value);
        Assert.Equal(1, receipt.EmergencyStop.Transition.Value);
        Assert.Equal(EmergencyStopReason.SafetyInterlock, receipt.EmergencyStop.Reason);
        Assert.False(pump.IsEmergencyStopEngaged);
        Assert.Equal(2, pump.LatestEmergency.Episode.Value);
        Assert.Equal(3, pump.LatestEmergency.Transition.Value);
        Assert.Equal(EmergencyStopReason.SuiteShutdown, pump.LatestEmergency.Reason);
        Assert.Equal(4, pump.EmergencyTransition.Value);
    }

    [Fact]
    public void LateResponseKeepsFirstEmergencyAcrossClearAndReengage()
    {
        var clock = new ThreadSafeTestClock(100);
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("diagnostics.emergency.late")
        {
            ActionCount = 1,
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        var frame = TestWorldCollector.ActivationFrame + 1;
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));
        pump.SetEmergencyStop(true, EmergencyStopReason.SafetyInterlock);
        pump.SetEmergencyStop(false);
        pump.SetEmergencyStop(true, EmergencyStopReason.SuiteShutdown);
        pump.SetEmergencyStop(false);
        release.Set();
        ServiceRunnerTestWait.PumpToResponse(pump, registration.Runner, ref frame);

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(1, receipt.EmergencyStop.Episode.Value);
        Assert.Equal(1, receipt.EmergencyStop.Transition.Value);
        Assert.Equal(EmergencyStopReason.SafetyInterlock, receipt.EmergencyStop.Reason);
        Assert.Equal(0, definition.ActionExecutionCount);
    }

    [Fact]
    public void DiagnosticsReadDoesNotAcknowledgeStoppedWorker()
    {
        using var registry = new ServiceCycleRegistry(1);
        using var registration = registry.Register(
            new ExecutionServiceDefinition("diagnostics.exit"),
            new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var oldRunner = registration.Runner;

        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        Assert.True(SpinWait.SpinUntil(() => oldRunner.WorkerExitPrepared, ServiceCycleTestDeadline.Value));
        Assert.Equal(ServiceHandoffPhase.Stopping, oldRunner.DiagnosticsHandoffPhaseHint);
        var buffer = new ServiceCycleServiceDiagnosticsSnapshot[1];
        ServiceCycleDiagnostics.CopyServices(pump, buffer);

        Assert.Equal(ServiceHandoffPhase.Stopping, oldRunner.DiagnosticsHandoffPhaseHint);
        Assert.True(oldRunner.TryAcknowledgeWorkerExit());
    }
}

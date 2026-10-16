using System;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class SuiteFramePumpControlSafetyTests
{
    [Fact]
    public void ResponseReadyDuringEmergencyEpisodeRemainsRejectedAfterImmediateClear()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.episode.response") { ActionCount = 3 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);

        pump.SetEmergencyStop(true);
        var engagedGeneration = pump.EmergencyTransition;
        pump.SetEmergencyStop(false);
        clock.Advance(new MonotonicDuration(17));
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(1, engagedGeneration.Value);
        Assert.Equal(2, pump.EmergencyTransition.Value);
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(clock.Now, receipt.CompletedAt);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.True(registration.Runner.Snapshot.Projection.IsPresent);
    }

    [Fact]
    public void EvaluationBlockedDuringEmergencyEpisodeRemainsRejectedAfterImmediateClear()
    {
        using var entered = new ManualResetEventSlim(false);
        using var release = new ManualResetEventSlim(false);
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.episode.evaluation")
        {
            ActionCount = 2,
            EvaluationEntered = entered,
            EvaluationRelease = release,
        };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = 1L;
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.PublishDeferredRequest(pump, registration.Runner, ref frame);
        Assert.True(entered.Wait(ServiceCycleTestDeadline.Value));

        pump.SetEmergencyStop(true);
        pump.SetEmergencyStop(false);
        release.Set();
        ServiceRunnerTestWait.ForPhase(registration.Runner, ServiceHandoffPhase.ResponseReady);
        clock.Advance(new MonotonicDuration(23));
        ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);

        var receipt = registration.Runner.Snapshot.PreviousReceipt;
        Assert.Equal(CommonActionResultCodes.EmergencyStop, receipt.ResultCode);
        Assert.Equal(clock.Now, receipt.CompletedAt);
        Assert.Equal(0, definition.ActionExecutionCount);
        Assert.Equal(1, definition.EvaluationCount);
    }

    [Fact]
    public void RegistrationDisposeDuringCaptureIsRejectedWithoutLosingHandle()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.guard.registration") { ActionCount = 0 };
        var registration = registry.Register(definition, new LifecycleGeneration(1));
        Exception? observed = null;
        definition.ShouldStartCallback = () =>
        {
            try { registration.Dispose(); }
            catch (Exception ex) { observed = ex; }
        };
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        // The pump probes the handoff with a zero-timeout lock so a worker can never park the main
        // thread, and the freshly started worker holds that same lock while it parks itself. Pumping
        // one frame before the park is a race the pump is designed to lose — it comes back with no
        // cycle and tries again next frame — so this frame waits for the park it is asserting about
        // rather than for the scheduler.
        ServiceRunnerTestWait.ForHandoff(
            registration.Runner,
            value => value.Phase == ServiceHandoffPhase.Empty && value.WorkerWaitCount > 0,
            "the initial worker wait");

        var report = pump.PumpFrame(1);

        Assert.Equal(1, report.CyclesStarted);
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(1, registry.Count);
        Assert.Same(TestSuiteConfiguration.Default, registry.Configuration.ReadLatest().Snapshot);
        definition.ShouldStartCallback = null;
        registration.Dispose();
        Assert.Equal(0, registry.Count);
    }

    [Fact]
    public void RegistryDisposeDuringActionIsRejectedUntilCallbackReturns()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.guard.registry") { ActionCount = 1 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        Exception? observed = null;
        definition.ActionCallback = () =>
        {
            try { registry.Dispose(); }
            catch (Exception ex) { observed = ex; }
        };
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = PrepareBatch(pump, registration);

        var action = pump.PumpFrame(frame);

        Assert.Equal(1, action.ActionsAttempted);
        Assert.IsType<InvalidOperationException>(observed);
        Assert.Equal(1, registry.Count);
        Assert.Equal(1, definition.ActionExecutionCount);
        Assert.Same(TestSuiteConfiguration.Default, registry.Configuration.ReadLatest().Snapshot);
    }

    [Fact]
    public void ThrowingCaptureIsCountedAndTimedWithoutPublishingWork()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new SourceServiceDefinition("pump.capture.throw");
        definition.CaptureCallback = () =>
        {
            clock.Advance(new MonotonicDuration(13));
            throw new InvalidOperationException("synthetic capture fault");
        };
        using var registration = registry.RegisterSource(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        const int maximumCaptureFrames = 32;
        var report = default(SuiteFramePumpReport);
        var captureFrame = 0L;
        var capturesAttempted = 0;
        for (var frame = 1L; frame <= maximumCaptureFrames; frame++)
        {
            var candidate = pump.PumpFrame(frame);
            capturesAttempted += candidate.CapturesAttempted;
            if (candidate.CapturesAttempted != 0)
            {
                report = candidate;
                captureFrame = frame;
                break;
            }
            Thread.Yield();
        }

        Assert.NotEqual(0, captureFrame);
        var afterCapture = pump.PumpFrame(captureFrame + 1);
        capturesAttempted += afterCapture.CapturesAttempted;
        var duplicate = pump.PumpFrame(captureFrame);

        Assert.Equal(1, report.CapturesAttempted);
        Assert.Equal(1, capturesAttempted);
        Assert.Equal(13, report.CaptureDuration.Ticks);
        Assert.True(report.TotalDuration.Ticks >= 13);
        Assert.True(afterCapture.Accepted);
        Assert.Equal(0, afterCapture.CapturesAttempted);
        Assert.Equal(1, definition.StartCount);
        Assert.Equal(ServiceHandoffPhase.Empty, registration.Runner.HandoffPhaseHint);
        Assert.False(duplicate.Accepted);
        Assert.Equal(1, definition.StartCount);
    }

    [Fact]
    public void ThrowingActionMapsToAdapterFaultAndAbortsTheUntouchedSuffix()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var definition = new ExecutionServiceDefinition("pump.action.throw") { ActionCount = 3 };
        definition.ActionCallback = () => throw new InvalidOperationException("synthetic action fault");
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var frame = PrepareBatch(pump, registration);

        var action = pump.PumpFrame(frame++);
        var receipt = registration.Runner.Snapshot.PreviousReceipt;

        Assert.Equal(1, action.ActionsAttempted);
        Assert.Equal(1, definition.ActionExecutionCount);
        Assert.Equal(BatchTerminalDisposition.Faulted, receipt.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, receipt.ResultCode);
        Assert.Equal(0, receipt.CommittedCount);
        Assert.Equal(2, receipt.UntouchedSuffixCount);
        Assert.Equal(0, receipt.NativeCallOutcome.NativeCallsAttempted);
        Assert.Equal(ServiceFaultCategory.ActionExecution, registration.Runner.Snapshot.Fault.Category);

        var handback = pump.PumpFrame(frame);
        Assert.Equal(0, handback.ActionsAttempted);
        ServiceRunnerTestWait.ForCleanup(registration.Runner);
        Assert.Equal(1, definition.ActionExecutionCount);
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

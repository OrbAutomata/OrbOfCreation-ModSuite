using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Diagnostics;

public sealed class FeatureWorkerRetryDiagnosticsTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FailedEvaluationWaitsNeutrallyExplainsRepeatsAndRecoversThroughPumpAndRegistry(
        bool spellLevel,
        bool previouslySuccessful)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        };
        var currentConfiguration = registry.ConfigurationPublication.Publish(configuration);
        var definition = new ExecutionServiceDefinition(
            (spellLevel ? SpellLevelServicePolicies.ServiceId : AutoCastServicePolicies.ServiceId).Value);
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var statuses = new FeatureStatusRegistry();
        var key = new FeatureStatusKey("test.suite", spellLevel ? "SpellLevel" : "AutoCast");
        using var reporter = new AutomataFeatureStatusReporter(statuses, new FeatureStatusSnapshot(
            key, "Test feature", true, FeatureStatusState.NotReady,
            new FeatureStatusReason(FeatureStatusReasonCode.Initializing, "Waiting for evaluation."), 1));
        var probe = new CapabilityProbe();
        Action<SuiteFramePump, SuiteFramePumpReport> observe;
        Action<ConfigGeneration> observeConfiguration;
        if (spellLevel)
        {
            var bridge = new SpellLevelServiceCycleDiagnosticsBridge(
                1, currentConfiguration, true, new SpellLevelCapabilityState(),
                new SpellLevelBoundaryStatusState(), probe, reporter);
            observe = (currentPump, report) => bridge.Observe(currentPump, in report, true);
            observeConfiguration = bridge.ObserveConfiguration;
        }
        else
        {
            var bridge = new AutoCastServiceCycleDiagnosticsBridge(
                1, currentConfiguration, true, new AutoCastManualPauseState(), reporter);
            observe = (currentPump, report) => bridge.Observe(currentPump, in report, true);
            observeConfiguration = bridge.ObserveConfiguration;
        }
        long frame = 3;
        if (previouslySuccessful)
        {
            observe(pump, RunCycle(pump, registry, registration, ref frame));
            Assert.Equal(FeatureStatusState.Operational, reporter.Current.State);
            currentConfiguration = registry.ConfigurationPublication.Publish(configuration);
            observeConfiguration(currentConfiguration);
        }
        var probesBeforeFailure = probe.Reads;
        definition.FailNextEvaluations(2);
        observe(pump, RunCycle(pump, registry, registration, ref frame));
        Assert.True(statuses.TryGet(key, out var first));
        Assert.Equal(FeatureStatusState.NotReady, first.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, first.Reason.Code);
        Assert.Contains("retrying Evaluation", first.Reason.Summary);
        Assert.Contains("1 failed attempt", first.Reason.Summary);
        Assert.Equal(probesBeforeFailure, probe.Reads);
        var available = ServiceCycleDiagnostics.ReadAvailableService(pump, definition.ServiceId);
        Assert.True(available.HasValue);
        Assert.Equal(1, available.Value.LatestFault.OccurrenceCount);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        observe(pump, RunCycle(pump, registry, registration, ref frame));
        Assert.True(statuses.TryGet(key, out var repeated));
        Assert.Equal(FeatureStatusState.NotReady, repeated.State);
        Assert.Contains("2 failed attempt", repeated.Reason.Summary);
        Assert.Equal(probesBeforeFailure, probe.Reads);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        observe(pump, RunCycle(pump, registry, registration, ref frame));
        Assert.True(statuses.TryGet(key, out var recovered));
        Assert.Equal(FeatureStatusState.Operational, recovered.State);
        Assert.Equal(FeatureStatusReasonCode.None, recovered.Reason.Code);
        Assert.False(registration.Runner.Snapshot.Fault.IsValid);
        Assert.Equal(spellLevel ? probesBeforeFailure + 1 : 0, probe.Reads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DeferredServiceReadRefreshesFailureAndRecoveryBeforeConsumingProjection(int feature)
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        var configuration = registry.ConfigurationPublication.Publish(new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        });
        var serviceId = feature switch
        {
            0 => AutoBuyServicePolicies.ServiceId,
            1 => SpellLevelServicePolicies.ServiceId,
            _ => AutoHarvestServicePolicies.ServiceId,
        };
        var definition = new ExecutionServiceDefinition(serviceId.Value)
        {
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        using var reporter = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(new FeatureStatusKey("test.suite", "deferred"),
                "Deferred diagnostics", true, FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.Initializing, "Waiting."), 1));
        using var harvest = feature == 2
            ? new AutoHarvestServiceCycleDiagnosticsBridge(1, configuration, true, null, reporter)
            : null;
        Action<SuiteFramePump, SuiteFramePumpReport> observe;
        if (feature == 0)
        {
            var bridge = new AutoBuyServiceCycleDiagnosticsBridge(
                1, configuration, AutoBuyCandidateKinds.All, reporter,
                new AutoBuyInvariantLatch(), static _ => { });
            observe = (currentPump, report) => bridge.Observe(currentPump, in report, AutoBuyCandidateKinds.All);
        }
        else if (feature == 1)
        {
            var bridge = new SpellLevelServiceCycleDiagnosticsBridge(
                1, configuration, true, new SpellLevelCapabilityState(),
                new SpellLevelBoundaryStatusState(), new CapabilityProbe(), reporter);
            observe = (currentPump, report) => bridge.Observe(currentPump, in report, true);
        }
        else
        {
            observe = (currentPump, report) => harvest!.Observe(currentPump, in report, true);
        }

        long frame = 3;
        definition.FailNextEvaluations(2);
        observe(pump, RunCycle(pump, registry, registration, ref frame));
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, reporter.Current.Reason.Code);
        Assert.Contains("1 failed attempt", reporter.Current.Reason.Summary);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        var repeated = RunCycle(pump, registry, registration, ref frame);
        ObserveWhileContended(repeated);
        Assert.Contains("1 failed attempt", reporter.Current.Reason.Summary);
        ObserveQuietFrame();
        Assert.Equal(FeatureStatusState.NotReady, reporter.Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, reporter.Current.Reason.Code);
        Assert.Contains("2 failed attempt", reporter.Current.Reason.Summary);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        var recovery = RunCycle(pump, registry, registration, ref frame);
        ObserveWhileContended(recovery);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, reporter.Current.Reason.Code);
        ObserveQuietFrame();
        Assert.NotEqual(FeatureStatusReasonCode.RuntimeFailure, reporter.Current.Reason.Code);
        // The shared worker fixture has no Harvest pair-health fields. Its runtime fault still
        // clears; the existing Harvest projection tests own the native-free pair-health contract.
        Assert.Equal(feature == 2 ? FeatureStatusState.NotReady : FeatureStatusState.Operational,
            reporter.Current.State);

        void ObserveWhileContended(SuiteFramePumpReport report)
        {
            using var contention = new HandoffGateContention(registration.Runner);
            contention.Acquire();
            try
            {
                HeldGate.AssertReturnsWhileHeld(() => observe(pump, report), contention.Release);
            }
            finally { contention.Release(); }
        }

        void ObserveQuietFrame()
        {
            var quiet = pump.PumpFrame(frame++);
            Assert.Equal(0, quiet.ResponsesAcquired);
            observe(pump, quiet);
        }
    }

    private static SuiteFramePumpReport RunCycle(
        SuiteFramePump pump,
        ServiceCycleRegistry registry,
        ServiceRegistration<ExecutionState, ExecutionAction> registration,
        ref long frame)
    {
        TestWorldCollector.CollectedAt(registry, frame);
        ServiceRunnerTestWait.ForWorkerReady(registration);
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.ForResponse(registration);
        var report = ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        ServiceRunnerTestWait.ForHandback(registration.Runner);
        return report;
    }

    private sealed class CapabilityProbe : ISpellLevelCapabilityPort
    {
        internal int Reads { get; private set; }
        public bool TryReadCapability(
            out AutoSpellLevelCapability capability,
            out GameActionReason failure,
            out Exception? cause)
        {
            Reads++;
            capability = AutoSpellLevelCapability.All;
            failure = default;
            cause = null;
            return true;
        }
    }
}

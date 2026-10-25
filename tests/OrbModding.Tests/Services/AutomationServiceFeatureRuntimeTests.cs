using System;
using System.Collections;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services;

public sealed class AutomationServiceFeatureRuntimeTests
{
    [Fact]
    public void A_contended_response_is_observed_on_a_quiet_frame_without_another_response()
    {
        using var harness = new Harness();
        var response = harness.EvaluateWorld();
        Assert.Equal(1, response.ResponsesAcquired);
        using (var contention = new HandoffGateContention(harness.Registration.Runner))
        {
            contention.Acquire();
            try
            {
                HeldGate.AssertReturnsWhileHeld(
                    () => harness.Runtime.ObserveFrame(harness.Pump, in response), contention.Release);
                Assert.Equal(FeatureStatusState.NotReady, harness.Status.Current.State);
                Assert.Equal(string.Empty, harness.Status.Current.Detail);
                Assert.Equal(0, harness.DetailCalls);
            }
            finally { contention.Release(); }
        }

        var quiet = harness.ObserveQuietFrame();

        Assert.Equal(0, quiet.ResponsesAcquired);
        Assert.Equal(0, quiet.ActionsAttempted);
        Assert.Equal(FeatureStatusState.Operational, harness.Status.Current.State);
        Assert.True(harness.Status.Current.Reason.IsEmpty);
        Assert.Equal("Latest evaluation\nPassive evidence ready.", harness.Status.Current.Detail);
        Assert.Equal(1, harness.DetailCalls);
        harness.ObserveQuietFrame();
        Assert.Equal(1, harness.DetailCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Configuration_and_lifecycle_changes_clear_details_and_decline_retained_old_projections(bool lifecycle)
    {
        using var harness = new Harness();
        var response = harness.EvaluateWorld();
        harness.Runtime.ObserveFrame(harness.Pump, in response);
        Assert.NotEmpty(harness.Status.Current.Detail);
        Assert.Equal(1, harness.DetailCalls);

        if (lifecycle) harness.Runtime.ObserveLifecycle(2, new ConfigGeneration(1));
        else harness.Runtime.ObserveConfiguration(new ConfigGeneration(2));

        Assert.Equal(string.Empty, harness.Status.Current.Detail);
        Assert.Equal(FeatureStatusState.NotReady, harness.Status.Current.State);
        // A response racing the reset still describes the older pinned lifecycle/configuration.
        harness.Runtime.ObserveFrame(harness.Pump, in response);
        harness.ObserveQuietFrame();
        Assert.Equal(string.Empty, harness.Status.Current.Detail);
        Assert.Equal(1, harness.DetailCalls);
    }

    [Fact]
    public void Current_evaluation_details_remain_separate_from_a_later_ownership_block()
    {
        using var harness = new Harness();
        var response = harness.EvaluateWorld();
        harness.Runtime.ObserveFrame(harness.Pump, in response);
        Assert.True(harness.Status.Current.Reason.IsEmpty);
        var detail = harness.Status.Current.Detail;

        harness.Owned = false;
        harness.ObserveQuietFrame();

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, harness.Status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ActionFamilyConflict, harness.Status.Current.Reason.Code);
        Assert.Equal("Notification mute is owned elsewhere.", harness.Status.Current.Reason.Summary);
        Assert.Equal(detail, harness.Status.Current.Detail);
    }

    [Fact]
    public void A_start_fault_reaches_health_without_any_response_or_action_and_survives_configuration_reset()
    {
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), new ThreadSafeTestClock(1_000));
        var configuration = registry.ConfigurationPublication.Publish(new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        });
        var definition = new ExecutionServiceDefinition("shared-runtime.start-failure")
        {
            ShouldStartCallback = static () => throw new InvalidOperationException("injected start failure"),
        };
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "SharedStartFailure"), "Start failure", true,
            FeatureStatusState.NotReady, new FeatureStatusReason(FeatureStatusReasonCode.Initializing, "Waiting."), 1));
        using var gameAction = new PassiveNotificationMuteGameAction(
            TestRegistryResolver.For(static () => 1, new Hashtable()), static () => 1,
            static () => true, static () => string.Empty);
        var runtime = new AutomationServiceFeatureRuntime<ExecutionState, ExecutionAction>(
            registration, definition.ServiceId, gameAction, status,
            static () => true, static () => string.Empty,
            static _ => (FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty),
            1, configuration, static _ => { });
        runtime.ActivateDiagnostics();
        ServiceRunnerTestWait.ForWorkerReady(registration);

        var failed = pump.PumpFrame(3);
        Assert.Equal(0, failed.ResponsesAcquired);
        Assert.Equal(0, failed.ActionsAttempted);
        Assert.Equal(0, failed.CyclesStarted);
        Assert.Equal(1, definition.StartCount);
        runtime.ObserveFrame(pump, in failed);

        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, status.Current.Reason.Code);
        Assert.Contains("retrying Start after 1 failed attempt", status.Current.Reason.Summary);
        Assert.Equal(string.Empty, status.Current.Detail);

        runtime.ObserveConfiguration(configuration.Next());
        Assert.Equal(FeatureStatusReasonCode.GameplayNotReady, status.Current.Reason.Code);
        var quiet = pump.PumpFrame(4);
        Assert.Equal(0, quiet.ResponsesAcquired);
        Assert.Equal(0, quiet.ActionsAttempted);
        runtime.ObserveFrame(pump, in quiet);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, status.Current.Reason.Code);
        Assert.Contains("retrying Start after 1 failed attempt", status.Current.Reason.Summary);
        runtime.DisposeDiagnostics();
    }

    [Fact]
    public void A_replacement_construction_fault_is_visible_without_a_current_runner_and_clears_on_recovery()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, clock);
        var configuration = registry.ConfigurationPublication.Publish(new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        });
        var definition = new LifecycleServiceDefinition("shared-runtime.construction-failure") { ActionCount = 0 };
        using var registration = registry.Register(definition, new LifecycleGeneration(1));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        using var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "SharedConstructionFailure"), "Construction failure", true,
            FeatureStatusState.NotReady, new FeatureStatusReason(FeatureStatusReasonCode.Initializing, "Waiting."), 1));
        using var gameAction = new PassiveNotificationMuteGameAction(
            TestRegistryResolver.For(static () => 2, new Hashtable()), static () => 2,
            static () => true, static () => string.Empty);
        var runtime = new AutomationServiceFeatureRuntime<LifecycleState, LifecycleAction>(
            registration, definition.ServiceId, gameAction, status,
            static () => true, static () => string.Empty,
            static _ => (FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty),
            1, configuration, static _ => { });
        runtime.ActivateDiagnostics();
        definition.FailNextWorkerFactories(1);
        Assert.True(pump.RequestLifecycleReplacement(new LifecycleGeneration(2)));
        runtime.ObserveLifecycle(2, configuration);
        var fault = registration.LifecycleSnapshot.ConstructionFault;
        Assert.True(fault.IsValid);
        Assert.Equal(ServiceFaultCategory.LifecycleConstruction, fault.Category);
        Assert.Null(ServiceCycleDiagnostics.ReadAvailableService(pump, definition.ServiceId));

        var failed = pump.PumpFrame(3);
        Assert.Equal(0, failed.ResponsesAcquired);
        Assert.Equal(0, failed.ActionsAttempted);
        runtime.ObserveFrame(pump, in failed);

        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, status.Current.Reason.Code);
        Assert.Contains("retrying LifecycleConstruction after 1 failed attempt", status.Current.Reason.Summary);
        Assert.Contains(fault.Reason, status.Current.Reason.Summary);
        clock.Advance(new MonotonicDuration(10));
        var frame = 4L;
        var deadline = new ServiceCycleTestDeadline.Budget();
        deadline.For(() =>
        {
            var recovered = pump.PumpFrame(frame++);
            runtime.ObserveFrame(pump, in recovered);
            return !registration.LifecycleSnapshot.ConstructionFault.IsValid &&
                status.Current.Reason.Code == FeatureStatusReasonCode.GameplayNotReady;
        }, "construction failure health to clear when the current runner recovers");
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(string.Empty, status.Current.Detail);
        runtime.DisposeDiagnostics();
    }

    private sealed class Harness : IDisposable
    {
        private readonly ServiceCycleRegistry _registry;
        private readonly PassiveNotificationMuteGameAction _gameAction;
        private long _frame = 1;
        internal bool Owned { get; set; } = true;
        internal int DetailCalls { get; private set; }
        internal AutomataFeatureStatusReporter Status { get; }
        internal ServiceRegistration<QualityOfLifeCycleState, QualityOfLifeCycleAction> Registration { get; }
        internal AutomationServiceFeatureRuntime<QualityOfLifeCycleState, QualityOfLifeCycleAction> Runtime { get; }
        internal SuiteFramePump Pump { get; }

        internal Harness()
        {
            var config = new SuiteRuntimeConfiguration
            {
                General = new SuiteGeneralConfiguration { Enabled = true },
                QualityOfLife = new QualityOfLifeConfiguration { Mode = QualityOfLifeOperationMode.Active },
            };
            Status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, QualityOfLifeFeatureContribution.FeatureId),
                "Quality of Life", true, FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "Waiting for a world."), 1));
            var resolver = TestRegistryResolver.For(static () => 1, new Hashtable());
            _gameAction = new PassiveNotificationMuteGameAction(resolver, static () => 1,
                static () => true, static () => string.Empty);
            var dependencies = new QualityOfLifeFeatureDependencies(resolver, static () => 1,
                () => Owned, static () => true, static () => "Notification mute is owned elsewhere.",
                Status, static _ => { });
            _registry = new ServiceCycleRegistry(1, config, new ConfigGeneration(1),
                new LifecycleGeneration(1), new ThreadSafeTestClock(1_000));
            Registration = _registry.Register(QualityOfLifeService.Define(_gameAction, dependencies),
                ServiceActionDispatchPolicy.Bounded(1));
            Runtime = new AutomationServiceFeatureRuntime<QualityOfLifeCycleState, QualityOfLifeCycleAction>(
                Registration, QualityOfLifeService.ServiceId, _gameAction, Status,
                () => Owned, static () => "Notification mute is owned elsewhere.",
                static _ => (FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty),
                1, new ConfigGeneration(1), static _ => { },
                describeDetail: projection =>
                {
                    DetailCalls++;
                    Assert.True(projection.GetEntry(0).Value.Boolean);
                    return "Latest evaluation\nPassive evidence ready.";
                });
            _registry.Seal();
            Pump = new SuiteFramePump(_registry);
            var categories = new[]
            {
                new WorldCollectionCategoryStatus("passive abilities", WorldCategoryOutcome.Collected, 0, 0, default),
            };
            TestWorldCollector.CollectedAt(_registry, 2, new GameWorldState
            {
                CollectedAtFrame = 2,
                CollectedAtEpoch = 1,
                CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(categories, categories.Length),
            });
            Runtime.ActivateDiagnostics();
        }

        internal SuiteFramePumpReport EvaluateWorld()
        {
            ServiceRunnerTestWait.ForWorkerReady(Registration);
            Assert.Equal(1, Pump.PumpFrame(_frame++).CyclesStarted);
            ServiceRunnerTestWait.ForResponse(Registration);
            return Pump.PumpFrame(_frame++);
        }

        internal SuiteFramePumpReport ObserveQuietFrame()
        {
            var report = Pump.PumpFrame(_frame++);
            Assert.Equal(0, report.ResponsesAcquired);
            Runtime.ObserveFrame(Pump, in report);
            return report;
        }

        public void Dispose()
        {
            Runtime.DisposeDiagnostics();
            Pump.Dispose();
            Runtime.DisposeRegistration();
            _gameAction.Dispose();
            _registry.Dispose();
            Status.Dispose();
        }
    }
}

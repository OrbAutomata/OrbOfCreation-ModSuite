using OrbModding.TestSupport;
using System;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using OrbModding.Tests.Services.GameFixes;
using Xunit;

namespace OrbModding.Tests.Services;

[Collection(GameFixesCollection.Name)]
public sealed class AutomataServiceCycleEmergencyStopTests
{
    [Fact]
    public void PlainToggleResumeRestoresServiceDispatchInTheSameRuntime()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        var feature = new DispatchFeature();
        var frame = 0L;
        using var runtime = AutomataServiceCycleComposition.Create(
            configuration.Current,
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => ++frame,
                () => 1,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[] { feature },
            new ManualLogSource());
        var store = new AutomataConfigurationStore(
            configuration,
            runtime.PublishSavedConfiguration);
        var control = new EmergencyStopControl(
            store,
            _ => runtime.CancelPreparedWork());

        AssertDispatchesAfter(runtime, feature.Definition, 0);

        control.Activate();
        var actionsAtStop = feature.Definition.ActionExecutionCount;
        for (var index = 0; index < 10; index++)
            runtime.Tick(0);

        Assert.True(runtime.EmergencyStopEngaged);
        Assert.Equal(actionsAtStop, feature.Definition.ActionExecutionCount);

        control.Activate();
        feature.CollectAfterResume();

        AssertDispatchesAfter(runtime, feature.Definition, actionsAtStop);
        Assert.False(runtime.EmergencyStopEngaged);
    }

    [Fact]
    public void StopAllLeavesGameFixesRunningAndReportedRunning()
    {
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile());
        using var statuses = new AutomataFeatureStatuses(configuration.Current, 1, new FeatureStatusRegistry());
        var frame = 0L;
        using var runtime = AutomataServiceCycleComposition.Create(
            configuration.Current,
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                () => ++frame,
                () => 1,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[]
            {
                new DispatchFeature(),
                new GameFixesServiceCycleFeature(GameFixCatalog.All, new Dictionary<GameFix, string>(),
                    statuses.Get(GameFixesFeatureContribution.FeatureId)),
            },
            new ManualLogSource());
        var store = new AutomataConfigurationStore(configuration, (published, generation) =>
        {
            statuses.ObserveConfiguration(published, generation);
            runtime.PublishSavedConfiguration(published, generation);
        });
        var control = new EmergencyStopControl(store, _ => runtime.CancelPreparedWork());
        Assert.True(SoundPoolGuard.IsActive);

        control.Activate();
        for (var index = 0; index < 10; index++)
            runtime.Tick(0);

        Assert.True(runtime.EmergencyStopEngaged);
        Assert.True(SoundPoolGuard.IsActive);
        Assert.Equal(
            FeatureStatusState.Operational,
            statuses.Get(GameFixesFeatureContribution.FeatureId).Current.State);
    }

    private static void AssertDispatchesAfter(
        AutomataServiceCycleRuntime runtime,
        ExecutionServiceDefinition definition,
        int priorActionCount)
    {
        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    runtime.Tick(0);
                    return definition.ActionExecutionCount > priorActionCount;
                },
                ServiceCycleTestDeadline.Value),
            "The service did not dispatch an action before the test deadline.");
    }

    private sealed class DispatchFeature : IAutomataServiceCycleFeature
    {
        private ServiceCycleRegistry? _registry;

        internal ExecutionServiceDefinition Definition { get; } =
            new("test.automata.emergency-stop-clear")
            {
                ActionCount = 1,
            };

        internal void CollectAfterResume() =>
            TestWorldCollector.CollectedAt(
                _registry ?? throw new InvalidOperationException("The feature is not registered."),
                100);

        public IAutomataServiceCycleFeatureRuntime Register(
            in AutomataServiceCycleFeatureContext context)
        {
            _registry = context.Registry;
            var registration = context.Registry.Register(Definition);
            TestWorldCollector.CollectedAtActivation(context.Registry);
            return new DispatchRuntime(registration);
        }

    }

    private sealed class DispatchRuntime : IAutomataServiceCycleFeatureRuntime
    {
        private readonly ServiceRegistration<ExecutionState, ExecutionAction> _registration;

        internal DispatchRuntime(
            ServiceRegistration<ExecutionState, ExecutionAction> registration) =>
            _registration = registration;

        public void ActivateDiagnostics()
        {
        }

        public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
        {
        }

        public void ObserveConfiguration(ConfigGeneration configurationGeneration)
        {
        }

        public void ObserveLifecycle(
            long nativeLifecycle,
            ConfigGeneration configurationGeneration)
        {
        }

        public void DisposeDiagnostics()
        {
        }

        public void DisposeRegistration() => _registration.Dispose();
    }
}

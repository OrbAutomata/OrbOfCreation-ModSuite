using System;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoConcept.Runtime.ServiceCycle;

public sealed class AutoConceptServiceCompositionTests
{
    private static readonly Guid Recipe = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LockedRecipe = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Core = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    [Fact]
    public void PublishedWorldReachesTheWorkerAndTheActionReturnsToTheMainThread()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoConceptService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            ServiceActionDispatchPolicy.Bounded(1));
        registry.WorldPublication.Publish(World(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(1, actions.ExecutionCount);
        Assert.Equal(AutoConceptActionKind.Add, actions.LastKind);
        Assert.Equal(Recipe, actions.LastRecipe);
    }

    [Fact]
    public void EnablingAfterDisabledStartInvalidatesThePublicationWait()
    {
        var clock = new ThreadSafeTestClock(100);
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoConceptService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, clock);
        registry.ConfigurationPublication.Publish(Configuration(AutoConceptOperationMode.Disabled));
        using var registration = registry.Register(definition, new LifecycleGeneration(7));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);
        var runner = registration.Runner;
        var frame = TestWorldCollector.ActivationFrame + 1;

        ServiceCyclePumpTestWait.PumpUntil(pump, ref frame, () => runner.Snapshot.HasWakeDue);

        Assert.Equal(ServiceHandoffPhase.Empty, runner.HandoffPhaseHint);
        Assert.Equal(long.MaxValue, runner.Snapshot.NextWakeDue.Ticks);

        registry.ConfigurationPublication.Publish(Configuration(AutoConceptOperationMode.Active));
        pump.PumpFrame(frame++);

        Assert.False(
            runner.Snapshot.HasWakeDue,
            "Publishing Active intent must wake Auto Concept instead of retaining its disabled-state publication wait.");
        ServiceRunnerTestWait.PublishDeferredRequest(pump, runner, ref frame);
    }

    [Fact]
    public void LockedOnlyAlternativeIsHighlightedAfterTraining()
    {
        var clock = new ThreadSafeTestClock();
        var definition = AutoConceptService.Define(
            new ActionPort(Thread.CurrentThread.ManagedThreadId));
        using var registry = new ServiceCycleRegistry(1, clock);
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            new LifecycleGeneration(7));
        registry.WorldPublication.Publish(IdleWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        var featureRegistry = new FeatureStatusRegistry();
        using var status = new AutomataFeatureStatusReporter(
            featureRegistry,
            new FeatureStatusSnapshot(
                new FeatureStatusKey(
                    PluginIds.SuiteGuid,
                    AutomataFeatureStatuses.AutoConceptFeatureId),
                "Auto Concept",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycleGeneration: 7));
        var bridge = new AutoConceptServiceCycleDiagnosticsBridge(
            lifecycle: 7,
            configurationGeneration: new ConfigGeneration(1),
            owned: true,
            featureStatus: status,
            warn: static _ => { });

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var training = pump.PumpFrame(2);
        bridge.Observe(pump, in training, owned: true);

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.NativeBusy, status.Current.Reason.Code);

        pump.PumpFrame(3);
        clock.Advance(MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(61)));
        registry.WorldPublication.Publish(IdleWorld(), new WorldGeneration(3));
        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(4).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var idle = pump.PumpFrame(5);
        bridge.Observe(pump, in idle, owned: true);

        Assert.Equal(FeatureStatusState.Locked, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.ProgressionLocked, status.Current.Reason.Code);
        Assert.Contains("No other unlocked", status.Current.Reason.Summary);
    }

    private static GameWorldState World()
    {
        var concepts = new WorldConceptRecipeBuffer();
        var concept = new WorldConceptRecipe(Recipe, Core);
        concepts.Append(in concept);
        var recipes = new[]
        {
            new WorldAlchemyRecipe(
                Recipe, Core, true, 0, 0, 0, default, 0, default,
                false, false, false, 0, false,
                default, default, default, default, default, default, default, default,
                default, default, default, default, default, new BigDouble(2), default,
                new BigDouble(1)),
        };
        return new GameWorldState
        {
            AlchemyRecipes = WorldTable.Create(recipes),
            ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
            CollectedAtEpoch = 1,
        };
    }

    private static GameWorldState IdleWorld()
    {
        var concepts = new WorldConceptRecipeBuffer();
        var activeConcept = new WorldConceptRecipe(Recipe, Core);
        var lockedConcept = new WorldConceptRecipe(LockedRecipe, Core);
        concepts.Append(in activeConcept);
        concepts.Append(in lockedConcept);
        var recipes = new[]
        {
            RecipeRow(Recipe, discovered: true),
            RecipeRow(LockedRecipe, discovered: false),
        };
        var instances = new WorldAlchemyInstanceBuffer();
        var active = new WorldAlchemyInstance(
            Recipe, quantity: 1, queuedQuantity: 1,
            drainReadable: true, drainRatio: new BigDouble(1));
        instances.Append(in active);
        return new GameWorldState
        {
            AlchemyRecipes = WorldTable.Create(recipes),
            ConceptRecipes = WorldAlchemyRowDeriver.Build(concepts),
            AlchemyInstances = WorldAlchemyRowDeriver.Build(instances),
            CollectedAtEpoch = 1,
        };
    }

    private static WorldAlchemyRecipe RecipeRow(Guid id, bool discovered) =>
        new(
            id, Core, discovered, 0, 0, 0, default, 0, default,
            false, false, false, 0, false,
            default, default, default, default, default, default, default, default,
            default, default, default, default, default, new BigDouble(1), default,
            new BigDouble(1));

    private static SuiteRuntimeConfiguration Configuration(
        AutoConceptOperationMode mode = AutoConceptOperationMode.Active) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = mode,
                SlotManagement = AutoConceptSlotManagementMode.TimedCycle,
                TrainingPeriodSeconds = 60,
            },
        };

    private sealed class ActionPort : IAutoConceptCycleActionPort
    {
        private readonly int _ownerThread;

        internal ActionPort(int ownerThread) => _ownerThread = ownerThread;
        internal int ExecutionCount { get; private set; }
        internal AutoConceptActionKind LastKind { get; private set; }
        internal Guid LastRecipe { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoConceptCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastKind = action.Kind;
            LastRecipe = action.RecipeId;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }
    }
}

using System;
using System.Threading;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Runtime.ServiceCycle;

/// <summary>
/// End-to-end wiring proof for Auto Cast's ServiceCycle composition: the shared pump drives the
/// typed definition, and everything that touches the game stays on the main thread.
/// </summary>
/// <remarks>
/// This is also the only place the runtime's structural audits run against this service. The
/// worker-storage rule and the worker-definition validator fire when a definition is registered, not
/// when it is compiled, so without a test that registers one they would first be checked inside the
/// game.
/// </remarks>
public sealed class AutoCastServiceCompositionTests : IDisposable
{
    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Firebolt = Guid.Parse("db0694f1-82bf-4ecf-a139-1341efee2bb8");

    public void Dispose() =>
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Unbound(1));

    [Fact]
    public void CompositionForwardsNativeWarningsAndKeepsRepeatSuppression()
    {
        var warnings = new System.Collections.Generic.List<string>();
        using var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutoCastFeatureContribution.FeatureId),
                "Auto Cast", true, FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "waiting"), 7));
        var adapters = AutoCastServiceAdapterComposition.Create(new AutoCastFeatureDependencies(
            static () => throw new InvalidOperationException("cast epoch unavailable"),
            static () => true, new AutoCastManualPauseState(), status, warnings.Add));
        var action = new AutoCastCycleAction(AutoCastActionKind.Fire, 0, Ember, 7);

        var first = adapters.Definition.TryExecute(action, Configuration(), default);
        var second = adapters.Definition.TryExecute(action, Configuration(), default);

        Assert.Equal(CommonActionResultCodes.AdapterFault, first.Code);
        Assert.Equal(ServiceActionDisposition.Faulted, second.Disposition);
        Assert.Contains("cast epoch unavailable", Assert.Single(warnings));
    }

    [Fact]
    public void CommonPumpExecutesThePlainServiceThroughMainThreadPorts()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoCastService.Define(actions, new AutoCastManualPauseState());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        TestWorldCollector.CollectedAtActivation(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        // An empty loadout is nothing to cast, so the boundary is never reached.
        Assert.Equal(0, actions.ExecutionCount);
    }

    /// <summary>
    /// A world published into the registry reaches the worker and becomes a cast, without the service
    /// ever being handed a publisher.
    /// </summary>
    /// <remarks>
    /// The composition pumps to completion against an empty world, so the test above cannot tell a
    /// wired world from an unwired one. This one can: nothing but the registry's publication could
    /// have put a slot in front of the action port.
    /// </remarks>
    [Fact]
    public void AWorldPublishedIntoTheRegistryReachesTheWorker()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = AutoCastService.Define(actions, new AutoCastManualPauseState());
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(definition);
        registry.WorldPublication.Publish(ReadyLoadoutWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.True(actions.ExecutionCount > 0);
        Assert.Equal(AutoCastActionKind.Fire, actions.LastKind);
        Assert.Equal(Ember, actions.LastSpellRecipeId);
    }

    /// <summary>
    /// A ritual hold travels from the worker's plan to the feature-status line the toggle tooltip and
    /// <c>suite_health</c> read, naming the spell and the reason in player words.
    /// </summary>
    [Fact]
    public void AnAttackSpellHeldOutsideARitualIsNamedOnTheStatusLine()
    {
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(Firebolt, "SpellRecipeSO", "Firebolt", "Firebolt"),
        }));
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var manualPause = new AutoCastManualPauseState();
        var definition = AutoCastService.Define(actions, manualPause);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration(ritualAware: true));
        using var registration = registry.Register(definition);
        registry.WorldPublication.Publish(FireboltOutsideARitualWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        using var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutoCastFeatureContribution.FeatureId),
                "Auto Cast",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycleGeneration: 7));
        var bridge = new AutoCastServiceCycleDiagnosticsBridge(
            lifecycle: 7,
            configurationGeneration: new ConfigGeneration(1),
            owned: true,
            manualPause,
            status);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var report = pump.PumpFrame(2);
        bridge.Observe(pump, in report, owned: true);

        Assert.Equal(0, actions.ExecutionCount);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.TemporarySafetyBlock, status.Current.Reason.Code);
        Assert.Equal(
            "Auto Cast holds Firebolt: no ritual is running. AutoCast/RitualAware turns this off.",
            status.Current.Reason.Summary);
    }

    /// <summary>
    /// A response read while the worker holds the handoff carries no projection, so the hold the last
    /// fresh read named stays on the status line instead of flickering off until the next response.
    /// </summary>
    [Fact]
    public void AHoldStaysNamedWhenTheNextResponseIsReadWhileTheWorkerHoldsTheHandoff()
    {
        EntityIdentityCatalogPublication.Publish(EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(Firebolt, "SpellRecipeSO", "Firebolt", "Firebolt"),
        }));
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var manualPause = new AutoCastManualPauseState();
        var definition = AutoCastService.Define(actions, manualPause);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(7));
        registry.ConfigurationPublication.Publish(Configuration(ritualAware: true));
        using var registration = registry.Register(definition);
        registry.WorldPublication.Publish(FireboltOutsideARitualWorld(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        using var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutoCastFeatureContribution.FeatureId),
                "Auto Cast",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(
                    FeatureStatusReasonCode.GameplayNotReady,
                    "Gameplay lifecycle is not ready."),
                lifecycleGeneration: 7));
        var bridge = new AutoCastServiceCycleDiagnosticsBridge(
            lifecycle: 7,
            configurationGeneration: new ConfigGeneration(1),
            owned: true,
            manualPause,
            status);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        var first = pump.PumpFrame(2);
        bridge.Observe(pump, in first, owned: true);
        var named = status.Current;
        registry.WorldPublication.Publish(FireboltOutsideARitualWorld(), new WorldGeneration(3));
        var frame = 3L;
        var second = ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        using var contention = new HandoffGateContention(registration.Runner);
        contention.Acquire();
        HeldGate.AssertReturnsWhileHeld(
            () => bridge.Observe(pump, in second, owned: true),
            contention.Release);
        contention.Release();

        Assert.Equal(FeatureStatusState.TemporarilyBlocked, named.State);
        Assert.Equal(FeatureStatusState.TemporarilyBlocked, status.Current.State);
        Assert.Equal(named.Reason.Summary, status.Current.Reason.Summary);
    }

    /// <summary>Firebolt equipped and ready, authored as one combat effect, with no ritual battle running.</summary>
    private static GameWorldState FireboltOutsideARitualWorld()
    {
        var slots = new WorldRelationBuffer<WorldSpellSlot>();
        var slot = new WorldSpellSlot(
            0, Firebolt,
            occupied: true,
            casting: false,
            readyingCast: false,
            attuning: false,
            channeled: false,
            toggled: false,
            chargeable: false,
            castReady: true,
            chargeAvailable: true,
            resourcesCovered: true,
            currentCharges: 1,
            maximumCharges: 1,
            cooldownRemaining: default);
        slots.Append(in slot);

        return new GameWorldState
        {
            SpellSlots = WorldRelationTable.Build(slots, WorldSpellSlotLookup.Order),
            SpellCosts = PublicationTable<WorldSpellCost>.Empty,
            SpellRecipeAuthoring = PublicationTable<WorldSpellRecipeAuthoring>.Create(new[]
            {
                new WorldSpellRecipeAuthoring(Firebolt, 0, 0d, 1d, 0, 0d, 0d, combatOnly: true),
            }),
        };
    }

    /// <summary>One equipped slot the game calls ready, costing nothing.</summary>
    private static GameWorldState ReadyLoadoutWorld()
    {
        var slots = new WorldRelationBuffer<WorldSpellSlot>();
        var slot = new WorldSpellSlot(
            0, Ember,
            occupied: true,
            casting: false,
            readyingCast: false,
            attuning: false,
            channeled: false,
            toggled: false,
            chargeable: false,
            castReady: true,
            chargeAvailable: true,
            resourcesCovered: true,
            currentCharges: 1,
            maximumCharges: 1,
            cooldownRemaining: default);
        slots.Append(in slot);

        return new GameWorldState
        {
            SpellSlots = WorldRelationTable.Build(slots, WorldSpellSlotLookup.Order),
            SpellCosts = PublicationTable<WorldSpellCost>.Empty,
        };
    }

    private static SuiteRuntimeConfiguration Configuration(bool ritualAware = false) =>
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoCast = new AutoCastConfiguration
            {
                Mode = AutoCastOperationMode.Active,
                RitualAware = ritualAware,
            },
        };

    private sealed class ActionPort : IAutoCastCycleActionPort
    {
        private readonly int _ownerThread;

        public ActionPort(int ownerThread) => _ownerThread = ownerThread;

        public int ExecutionCount { get; private set; }
        public AutoCastActionKind LastKind { get; private set; }
        public Guid LastSpellRecipeId { get; private set; }

        public ServiceActionResult TryExecute(
            in AutoCastCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            ExecutionCount++;
            LastKind = action.Kind;
            LastSpellRecipeId = action.SpellRecipeId;
            var call = new NativeMutationCallOutcome(1, 1, 1);
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, call));
        }

        public void ObserveOperational(bool operational) { }
    }
}

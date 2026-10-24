using OrbModding.TestSupport;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsTemporaryItemPolicyTests : IDisposable
{
    public AutoItemsTemporaryItemPolicyTests() => ResetNativeStubs();

    public void Dispose() => ResetNativeStubs();

    [Fact]
    public void ExactAllowlistEntryPlansOneTemporaryConsumableUse()
    {
        var itemId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var configuration = Configuration(itemId.ToString("D"));

        var action = Assert.Single(Plan(TemporaryWorld(itemId), configuration, out var wake));

        Assert.Equal(AutoItemsConsumableFamily.Thread, action.Family);
        Assert.Equal(itemId, action.ItemId);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void NearMissUuidDoesNothingAndAnEmptyAllowlistIsInert()
    {
        var itemId = Guid.Parse("00000000-0000-0000-0000-000000000201");
        var nearMiss = Guid.Parse("00000000-0000-0000-0000-000000000202");

        var nearMissActions = Plan(
            TemporaryWorld(itemId),
            Configuration(nearMiss.ToString("D")),
            out var nearMissWake);
        var emptyActions = Plan(
            TemporaryWorld(itemId),
            Configuration(string.Empty),
            out var emptyWake);

        Assert.Empty(nearMissActions);
        Assert.Empty(emptyActions);
        Assert.Equal(WakePolicy.OnPublication, nearMissWake);
        Assert.Equal(WakePolicy.OnPublication, emptyWake);
    }

    [Theory]
    [InlineData(false, 1, 0, 0)]
    [InlineData(true, 0, 0, 0)]
    [InlineData(true, 1, 1, 0)]
    [InlineData(true, 1, 0, 1)]
    public void VisibilityStockPreparationAndCooldownAreRequired(
        bool visible,
        int quantity,
        double preparation,
        double cooldown)
    {
        var itemId = Guid.NewGuid();
        var world = TemporaryWorld(
            itemId,
            visible: visible,
            quantity: quantity,
            preparation: preparation,
            cooldown: cooldown);

        var actions = Plan(
            world,
            Configuration(itemId.ToString("D")),
            out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void DurationCostsAndToxicityHeadroomAllFailClosed()
    {
        var durationId = Guid.Parse("00000000-0000-0000-0000-000000000301");
        var extraCostId = Guid.Parse("00000000-0000-0000-0000-000000000302");
        var toxicityId = Guid.Parse("00000000-0000-0000-0000-000000000303");

        var durationActions = Plan(
            TemporaryWorld(durationId, duration: double.NaN),
            Configuration(durationId.ToString("D")),
            out _);
        var extraCostActions = Plan(
            TemporaryWorld(extraCostId, addHeldNonToxicityCost: true),
            Configuration(extraCostId.ToString("D")),
            out _);
        var toxicityActions = Plan(
            TemporaryWorld(toxicityId, toxicity: BigDouble.Zero),
            Configuration(toxicityId.ToString("D")),
            out _);

        Assert.Empty(durationActions);
        Assert.Empty(extraCostActions);
        Assert.Empty(toxicityActions);
    }

    [Fact]
    public void PendingTemporaryUsageBlocksScrollAndRelicPlanning()
    {
        var temporaryId = Guid.Parse("00000000-0000-0000-0000-000000000401");
        var relicId = Guid.Parse("00000000-0000-0000-0000-000000000402");
        var temporary = Consumable(temporaryId, hasDuration: true, duration: 60);
        var relic = Consumable(relicId, hasDuration: false, duration: 0);
        var world = new GameWorldState
        {
            Consumables = WorldTable.Create(temporary, relic),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(
                new[]
                {
                    new WorldConsumableType(
                        temporaryId,
                        KnownEntities.ConsumableThreadType.Uuid),
                    new WorldConsumableType(
                        relicId,
                        KnownEntities.ConsumableRelicType.Uuid),
                }),
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(
                new[]
                {
                    ToxicityCost(temporaryId),
                    ToxicityCost(relicId),
                }),
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(
                new[]
                {
                    new WorldConsumableUsage(
                        temporaryId,
                        Guid.NewGuid(),
                        1,
                        engaged: false,
                        new BigDouble(60),
                        new BigDouble(60)),
                }),
            Resources = WorldTable.Create(Toxicity(new BigDouble(100))),
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
        };

        var actions = Plan(
            world,
            Configuration(temporaryId.ToString("D"), useRelics: true),
            out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void ContinuousCoconutFruitRelicTopologyPlansAsPermanentRelic()
    {
        var itemId = Guid.Parse("a1799c52-f9ff-4556-b052-f577ac3e7270");
        var world = new GameWorldState
        {
            Consumables = WorldTable.Create(
                Consumable(itemId, hasDuration: false, duration: 8)),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(
                new[]
                {
                    new WorldConsumableType(
                        itemId,
                        KnownEntities.ConsumableFruitType.Uuid),
                    new WorldConsumableType(
                        itemId,
                        KnownEntities.ConsumableRelicType.Uuid),
                }),
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(
                new[] { ToxicityCost(itemId) }),
            Resources = WorldTable.Create(Toxicity(new BigDouble(100))),
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
        };

        var action = Assert.Single(Plan(
            world,
            Configuration(string.Empty, useRelics: true),
            out var wake));

        Assert.Equal(itemId, action.ItemId);
        Assert.Equal(AutoItemsConsumableFamily.Relic, action.Family);
        Assert.Equal(WakePolicy.OnPublication, wake);
    }

    [Fact]
    public void DuplicateAndUnsupportedCrossOperationMembershipsRemainFailClosed()
    {
        var duplicateId = Guid.NewGuid();
        var crossOperationId = Guid.NewGuid();

        var duplicate = Plan(
            TemporaryWorld(
                duplicateId,
                familyIds: new[]
                {
                    KnownEntities.ConsumableThreadType.Uuid,
                    KnownEntities.ConsumableThreadType.Uuid,
                }),
            Configuration(duplicateId.ToString("D")),
            out _);
        var crossOperation = Plan(
            TemporaryWorld(
                crossOperationId,
                familyIds: new[]
                {
                    KnownEntities.ConsumablePotionType.Uuid,
                    KnownEntities.ConsumableRelicType.Uuid,
                }),
            Configuration(crossOperationId.ToString("D"), useRelics: true),
            out _);

        Assert.Empty(duplicate);
        Assert.Empty(crossOperation);
    }

    [Fact]
    public void FollowUpRequiresOneObservedEngagementBeforeDisappearance()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);
        var pending = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(
                itemId,
                frame: 11,
                Usage(itemId, engaged: false, remaining: 60)),
            ref state);
        var active = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(
                itemId,
                frame: 12,
                Usage(itemId, engaged: true, remaining: 30)),
            ref state);
        var completed = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(itemId, frame: 13),
            ref state);

        Assert.Equal(AutoItemsTemporaryActivationState.AwaitingActivation, pending.State);
        Assert.Equal(AutoItemsTemporaryActivationState.Active, active.State);
        Assert.Equal(AutoItemsTemporaryActivationState.Completed, completed.State);
        Assert.Equal(Guid.Empty, state.PausedTemporaryItem);
        Assert.Equal(Guid.Empty, state.PendingTemporaryItem);
    }

    [Fact]
    public void AUsageAutoItemsDidNotCreatePausesTheItemWhileAnyUsageOfItRemains()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);

        var paused = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(
                itemId,
                frame: 11,
                Usage(itemId, engaged: false, remaining: 60),
                Usage(itemId, engaged: false, remaining: 60)),
            ref state);
        var stillPaused = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(
                itemId,
                frame: 12,
                Usage(itemId, engaged: true, remaining: 30)),
            ref state);
        var resumed = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(itemId, frame: 13),
            ref state);

        Assert.Equal(AutoItemsTemporaryActivationState.Paused, paused.State);
        Assert.Equal(AutoItemsTemporaryPauseCause.MultipleUsages, paused.PauseCause);
        Assert.Equal(itemId, paused.ItemId);
        Assert.Equal(AutoItemsTemporaryActivationState.Paused, stillPaused.State);
        Assert.Equal(AutoItemsTemporaryActivationState.None, resumed.State);
        Assert.Equal(Guid.Empty, state.PausedTemporaryItem);
        Assert.Equal(AutoItemsTemporaryPauseCause.None, state.TemporaryPauseCause);
    }

    [Fact]
    public void AQueuedUsageThatVanishesUnengagedIsCancelledAndStopsBeingTracked()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);

        var cancelled = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(itemId, frame: 11),
            ref state);
        var after = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(itemId, frame: 12),
            ref state);

        Assert.Equal(AutoItemsTemporaryActivationState.Cancelled, cancelled.State);
        Assert.Equal(itemId, cancelled.ItemId);
        Assert.Equal(AutoItemsTemporaryActivationState.None, after.State);
        Assert.Equal(Guid.Empty, state.PendingTemporaryItem);
        Assert.Equal(Guid.Empty, state.PausedTemporaryItem);
    }

    [Fact]
    public void ACancelledFruitBacksOffWithDoublingUntilItsUsageEngages()
    {
        var registry = new Dictionary<Guid, object>();
        var fruit = ConsumableTestItems.Create(registry, AutoItemsConsumableFamily.Fruit);
        var itemId = fruit.GetGuid();
        var warnings = new List<string>();
        var status = FeatureStatus();
        using var runtime = ComposeAutoItems(registry, itemId, status, warnings, out var capture);
        var world = TemporaryWorld(itemId, familyIds: new[] { KnownEntities.ConsumableFruitType.Uuid });

        Publish(capture, world);
        TickUntil(runtime, () => fruit.GetQueued() == 1);
        CancelQueuedUse(fruit);
        CollectUntil(runtime, capture, world, () => fruit.SelectAndFireTimestamps.Count == 2);
        CancelQueuedUse(fruit);
        CollectUntil(runtime, capture, world, () => fruit.SelectAndFireTimestamps.Count == 3);
        var engaged = world with
        {
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(
                new[] { Usage(itemId, engaged: true, remaining: 30) }),
        };
        CollectUntil(
            runtime,
            capture,
            engaged,
            () => warnings.Exists(line => line.Contains("stops backing off")));

        var calls = fruit.SelectAndFireTimestamps;
        Assert.True(
            Elapsed(calls[0], calls[1]) >= TimeSpan.FromMilliseconds(250),
            $"The first retry came {Elapsed(calls[0], calls[1])} after the first use.");
        Assert.True(
            Elapsed(calls[1], calls[2]) >= TimeSpan.FromMilliseconds(500),
            $"The second retry came {Elapsed(calls[1], calls[2])} after the second use.");
        AssertWarningCount(2, warnings);
        Assert.Contains("after 1 failed attempt", warnings[0]);
        Assert.Contains("disappeared before it engaged", warnings[0]);
        Assert.Contains("stops backing off", warnings[1]);
        Assert.EndsWith("after 2 failed attempts in a row.", warnings[1]);
        Assert.All(warnings, line => Assert.Contains(itemId.ToString("D"), line));
        Assert.Equal(FeatureStatusState.Operational, status.Current.State);
    }

    [Fact]
    public void APlayerUsageOfACommittedFruitPausesItLoudlyAndResumesWithoutFaulting()
    {
        var registry = new Dictionary<Guid, object>();
        var fruit = ConsumableTestItems.Create(registry, AutoItemsConsumableFamily.Fruit);
        var itemId = fruit.GetGuid();
        var warnings = new List<string>();
        var status = FeatureStatus();
        using var runtime = ComposeAutoItems(registry, itemId, status, warnings, out var capture);
        var world = TemporaryWorld(itemId, familyIds: new[] { KnownEntities.ConsumableFruitType.Uuid });

        Publish(capture, world);
        TickUntil(runtime, () => fruit.GetQueued() == 1);
        var twoUsages = world with
        {
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(
                new[]
                {
                    Usage(itemId, engaged: false, remaining: 60),
                    Usage(itemId, engaged: false, remaining: 60),
                }),
        };
        CollectUntil(runtime, capture, twoUsages, () => warnings.Count > 0);
        var pausedStatus = status.Current;
        var noUsage = TemporaryWorld(
            itemId,
            quantity: 0,
            familyIds: new[] { KnownEntities.ConsumableFruitType.Uuid });
        CollectUntil(runtime, capture, noUsage, () => warnings.Count > 1);

        AssertWarningCount(2, warnings);
        Assert.Contains(itemId.ToString("D"), warnings[0]);
        Assert.Contains("more than one native usage appeared", warnings[0]);
        Assert.Contains(itemId.ToString("D"), warnings[1]);
        Assert.Contains("resumed", warnings[1]);
        Assert.Equal(FeatureStatusState.Operational, pausedStatus.State);
        Assert.Equal(FeatureStatusState.Operational, status.Current.State);
        Assert.Equal(1, fruit.GetQueued());
    }

    [Fact]
    public void AUseThatDoesNotVerifyBacksOffLoudlyAndRetriesTheSameFruit()
    {
        var registry = new Dictionary<Guid, object>();
        var fruit = ConsumableTestItems.Create(registry, AutoItemsConsumableFamily.Fruit);
        fruit.SelectionNoOp = true;
        var itemId = fruit.GetGuid();
        var warnings = new List<string>();
        var status = FeatureStatus();
        using var runtime = ComposeAutoItems(registry, itemId, status, warnings, out var capture);
        var world = TemporaryWorld(itemId, familyIds: new[] { KnownEntities.ConsumableFruitType.Uuid });

        Publish(capture, world);
        CollectUntil(runtime, capture, world, () => fruit.SelectAndFireTimestamps.Count == 2);
        var backingOffStatus = status.Current;
        fruit.SelectionNoOp = false;
        CollectUntil(runtime, capture, world, () => fruit.GetQueued() == 1);
        var engaged = world with
        {
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(
                new[] { Usage(itemId, engaged: true, remaining: 30) }),
        };
        CollectUntil(
            runtime,
            capture,
            engaged,
            () => status.Current.State == FeatureStatusState.Operational &&
                warnings.Exists(line => line.Contains("stops backing off")));

        Assert.Equal(3, fruit.SelectAndFireTimestamps.Count);
        Assert.Contains(itemId.ToString("D"), warnings[0]);
        Assert.Contains("backs off", warnings[0]);
        Assert.Contains("stops backing off", warnings[1]);
        Assert.EndsWith("after 2 failed attempts in a row.", warnings[1]);
        AssertWarningCount(2, warnings);
        Assert.Equal(FeatureStatusState.Degraded, backingOffStatus.State);
        Assert.Equal(FeatureStatusReasonCode.NativeMutationFailed, backingOffStatus.Reason.Code);
        Assert.Contains(itemId.ToString("D"), backingOffStatus.Reason.Summary);
        Assert.Equal(1, fruit.GetQueued());
    }

    [Fact]
    public void ABackedOffFruitTheAllowlistNoLongerNamesStopsBackingOff()
    {
        var registry = new Dictionary<Guid, object>();
        var fruit = ConsumableTestItems.Create(registry, AutoItemsConsumableFamily.Fruit);
        fruit.SelectionNoOp = true;
        var itemId = fruit.GetGuid();
        var warnings = new List<string>();
        var status = FeatureStatus();
        using var runtime = ComposeAutoItems(registry, itemId, status, warnings, out var capture);
        var world = TemporaryWorld(itemId, familyIds: new[] { KnownEntities.ConsumableFruitType.Uuid });

        Publish(capture, world);
        CollectUntil(
            runtime,
            capture,
            world,
            () => status.Current.State == FeatureStatusState.Degraded);
        runtime.PublishSavedConfiguration(
            Configuration(string.Empty, useRelics: true),
            new ConfigGeneration(2));
        CollectUntil(
            runtime,
            capture,
            world,
            () => status.Current.State == FeatureStatusState.Operational &&
                warnings.Exists(line => line.Contains("stops backing off")));

        Assert.Single(fruit.SelectAndFireTimestamps);
        AssertWarningCount(2, warnings);
        Assert.Contains("backs off", warnings[0]);
        Assert.Contains("stops backing off", warnings[1]);
        Assert.Contains(itemId.ToString("D"), warnings[1]);
        Assert.EndsWith("after 1 failed attempt.", warnings[1]);
    }

    [Fact]
    public void OnlyACommittedTemporaryReceiptStartsFollowUpTracking()
    {
        var itemId = Guid.NewGuid();
        var action = new AutoItemsCycleAction(
            itemId,
            AutoItemsConsumableFamily.Thread,
            collectedAtEpoch: 1,
            plannedLevel: 0,
            collectedAtFrame: 41);
        var state = AutoItemsCycleState.Create(new LifecycleGeneration(1));
        state.RecordPlanned(in action);
        var identity = Identity();
        var receipt = BatchReceipt.Completed(
            identity,
            new BatchId(1),
            actionCount: 1,
            new ServiceNativeCallTotals(1, 1, 1),
            new MonotonicTimestamp(2));

        AutoItemsTemporaryActivationPolicy.ReconcileReceipt(in receipt, ref state);

        Assert.False(state.HasPendingReceipt);
        Assert.Equal(itemId, state.PendingTemporaryItem);
        Assert.Equal(41, state.TemporarySubmittedFromFrame);
    }

    private static IReadOnlyList<AutoItemsCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        out WakePolicy wake)
    {
        var store = new ReusableActionStore<AutoItemsCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoItemsCycleAction>(store);
        var identity = Identity();
        var context = new ServiceCycleContext(
            identity,
            default,
            new MonotonicTimestamp(1));
        var state = AutoItemsCycleState.Create(identity.Lifecycle);
        state.ObserveConfiguration(identity.Config, configuration.AutoItems);
        wake = AutoItemsCycleEvaluator.Evaluate(
            world,
            in configuration,
            in context,
            ref state,
            writer,
            out _);
        var actions = new List<AutoItemsCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }
        return actions;
    }

    private static ServiceCycleIdentity Identity() =>
        new(
            AutoItemsServicePolicies.ServiceId,
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));

    private static AutoItemsCycleState SubmittedState(Guid itemId, long submittedFrame)
    {
        var state = AutoItemsCycleState.Create(new LifecycleGeneration(1));
        var action = new AutoItemsCycleAction(
            itemId,
            AutoItemsConsumableFamily.Thread,
            collectedAtEpoch: 1,
            plannedLevel: 0,
            collectedAtFrame: submittedFrame);
        state.RecordSubmittedTemporary(in action);
        return state;
    }

    private static GameWorldState FollowUpWorld(
        Guid itemId,
        long frame,
        params WorldConsumableUsage[] usages) =>
        new()
        {
            Consumables = WorldTable.Create(
                Consumable(itemId, hasDuration: true, duration: 60)),
            ConsumableUsages = PublicationTable<WorldConsumableUsage>.Create(usages),
            CollectedAtFrame = frame,
            CollectedAtEpoch = 1,
        };

    private static WorldConsumableUsage Usage(
        Guid itemId,
        bool engaged,
        double remaining) =>
        new(
            itemId,
            Guid.NewGuid(),
            1,
            engaged,
            new BigDouble(remaining),
            new BigDouble(60));

    private static GameWorldState TemporaryWorld(
        Guid itemId,
        bool visible = true,
        int quantity = 1,
        double preparation = 0,
        double cooldown = 0,
        double duration = 60,
        BigDouble? toxicity = null,
        bool addHeldNonToxicityCost = false,
        Guid[]? familyIds = null)
    {
        var toxicityQuantity = toxicity ?? new BigDouble(100);
        var costs = addHeldNonToxicityCost
            ? new[]
            {
                ToxicityCost(itemId),
                new WorldConsumableCost(
                    itemId,
                    WorldConsumableCostKind.Usage,
                    Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"),
                    new BigDouble(1)),
            }
            : new[] { ToxicityCost(itemId) };
        var selectedFamilies = familyIds ?? new[] { KnownEntities.ConsumableThreadType.Uuid };
        var types = new WorldConsumableType[selectedFamilies.Length];
        for (var index = 0; index < types.Length; index++)
            types[index] = new WorldConsumableType(itemId, selectedFamilies[index]);
        return new GameWorldState
        {
            Consumables = WorldTable.Create(
                Consumable(
                    itemId,
                    hasDuration: true,
                    duration,
                    visible,
                    quantity,
                    preparation,
                    cooldown)),
            ConsumableTypes = PublicationTable<WorldConsumableType>.Create(
                types),
            ConsumableCosts = PublicationTable<WorldConsumableCost>.Create(costs),
            Resources = WorldTable.Create(Toxicity(toxicityQuantity)),
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
        };
    }

    private static WorldConsumable Consumable(
        Guid itemId,
        bool hasDuration,
        double duration,
        bool visible = true,
        int quantity = 1,
        double preparation = 0,
        double cooldown = 0)
    {
        var modifiers = default(RawConsumableModifiers);
        return new WorldConsumable(
            itemId,
            visible,
            randomized: false,
            quantity,
            queuedQuantity: 0,
            maximumCarryLoad: 10,
            gainedSince: 0,
            maxCreatedLevel: 1,
            currentPrepTime: new BigDouble(preparation),
            currentCooldown: new BigDouble(cooldown),
            currentCooldownTime: BigDouble.Zero,
            in modifiers,
            preparationTime: 1,
            canBeRandomized: false,
            hasDuration,
            duration,
            queueOnStart: false);
    }

    private static WorldConsumableCost ToxicityCost(Guid itemId) =>
        new(
            itemId,
            WorldConsumableCostKind.Consume,
            KnownEntities.PotionToxicity.Uuid,
            new BigDouble(1));

    private static WorldResource Toxicity(BigDouble trueQuantity)
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = new RawResourceTraits(
            0, 0, 0, false, false, false, false, true, false, true,
            BigDouble.Zero, 0, 0, 0, false, 0,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, false);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            KnownEntities.PotionToxicity.Uuid,
            trueQuantity,
            new BigDouble(100),
            visible: true,
            lifetimeQuantity: BigDouble.Zero,
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100),
            gainRate: BigDouble.Zero,
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: false,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: BigDouble.Zero,
            fillFraction: 1,
            isAtCapacity: false,
            trueQuantity,
            trueRate: BigDouble.Zero);
    }

    private static void CancelQueuedUse(ConsumableSO fruit)
    {
        fruit.CancelUsage();
        fruit.SetStock(1, 0, 0);
    }

    private static TimeSpan Elapsed(long from, long to) =>
        TimeSpan.FromSeconds((to - from) / (double)System.Diagnostics.Stopwatch.Frequency);

    private static AutomataServiceCycleRuntime ComposeAutoItems(
        Dictionary<Guid, object> registry,
        Guid itemId,
        AutomataFeatureStatusReporter status,
        List<string> warnings,
        out RegistryCapture capture)
    {
        IDictionary source = registry;
        var composed = new RegistryCapture(new AutoItemsServiceCycleFeature(new AutoItemsFeatureDependencies(
            TestRegistryResolver.For(static () => 1, source),
            static () => 1,
            ownsActionFamily: static () => true,
            tryCaptureMutationPermit: static () => true,
            readOwnershipFailure: static () => string.Empty,
            featureStatus: status,
            warn: warnings.Add)));
        capture = composed;
        return AutomataServiceCycleComposition.Create(
            Configuration(itemId.ToString("D")),
            new ConfigGeneration(1),
            new AutomataServiceCycleHostDependencies(
                composed.NextFrame,
                static () => 1,
                new ServiceActionTimelineRegistry()),
            new IAutomataServiceCycleFeature[] { composed },
            new ManualLogSource());
    }

    private static void ResetNativeStubs()
    {
        Inventory.Preparing = false;
        TargetingManager.OpenRequests = 0;
        ConsumableSO.All.Clear();
        Inventory.ResetLists();
        GlobalVariables.MultiBuy = new IntVariable { Value = 1 };
        NativeMultiBuyScope.ResetForTests();
    }

    private static void Publish(RegistryCapture capture, GameWorldState world) =>
        TestWorldCollector.CollectedAt(
            capture.Registry,
            capture.Frame,
            world with { CollectedAtFrame = capture.Frame });

    private static void CollectUntil(
        AutomataServiceCycleRuntime runtime,
        RegistryCapture capture,
        GameWorldState world,
        Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + ServiceCycleTestDeadline.Value;
        while (!condition())
        {
            Assert.True(
                DateTime.UtcNow < deadline,
                "The runtime did not reach the expected state before the test deadline.");
            runtime.Tick(0);
            Publish(capture, world);
        }
    }

    private static void AssertWarningCount(int expected, List<string> warnings) =>
        Assert.True(
            warnings.Count == expected,
            $"Expected {expected} warnings but saw {warnings.Count}:\n{string.Join("\n", warnings)}");

    private static void TickUntil(AutomataServiceCycleRuntime runtime, Func<bool> condition) =>
        Assert.True(
            SpinWait.SpinUntil(
                () =>
                {
                    runtime.Tick(0);
                    return condition();
                },
                ServiceCycleTestDeadline.Value),
            "The runtime did not reach the expected state before the test deadline.");

    private static AutomataFeatureStatusReporter FeatureStatus() =>
        new(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoItemsFeatureId),
                "Auto Items",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycleGeneration: 1));

    private sealed class RegistryCapture : IAutomataServiceCycleFeature
    {
        private readonly IAutomataServiceCycleFeature _feature;
        private ServiceCycleRegistry? _registry;

        internal RegistryCapture(IAutomataServiceCycleFeature feature) => _feature = feature;

        internal ServiceCycleRegistry Registry =>
            _registry ?? throw new InvalidOperationException("The runtime has not registered the feature.");

        internal long Frame { get; private set; } = TestWorldCollector.ActivationFrame;

        internal long NextFrame() => ++Frame;

        public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
        {
            _registry = context.Registry;
            return _feature.Register(in context);
        }
    }

    private static SuiteRuntimeConfiguration Configuration(
        string allowlist,
        bool useRelics = false) =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseRelics = useRelics,
                UseScrolls = false,
                TemporaryItemAllowlist = allowlist,
            },
        };
}

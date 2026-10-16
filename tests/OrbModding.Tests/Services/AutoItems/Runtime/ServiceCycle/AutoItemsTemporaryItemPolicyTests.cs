using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoItems.Runtime.ServiceCycle;

public sealed class AutoItemsTemporaryItemPolicyTests
{
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
        Assert.False(state.IsTemporaryQuarantined(itemId));
        Assert.Equal(Guid.Empty, state.PendingTemporaryItem);
    }

    [Fact]
    public void DoubleUsageQuarantinesOnlyTheExactTemporaryItem()
    {
        var itemId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);

        var observation = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(
                itemId,
                frame: 11,
                Usage(itemId, engaged: false, remaining: 60),
                Usage(itemId, engaged: false, remaining: 60)),
            ref state);

        Assert.Equal(AutoItemsTemporaryActivationState.Quarantined, observation.State);
        Assert.Equal(AutoItemsTemporaryQuarantineCause.MultipleUsages, observation.QuarantineCause);
        Assert.Equal(itemId, observation.ItemId);
        Assert.True(state.IsTemporaryQuarantined(itemId));
        Assert.False(state.IsTemporaryQuarantined(otherId));
    }

    [Fact]
    public void PrematureExpiryQuarantinesWithTheExactCause()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);

        var observation = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(
                itemId,
                frame: 11,
                Usage(itemId, engaged: true, remaining: 0)),
            ref state);

        Assert.Equal(AutoItemsTemporaryActivationState.Quarantined, observation.State);
        Assert.Equal(AutoItemsTemporaryQuarantineCause.PrematureExpiry, observation.QuarantineCause);
        Assert.True(state.IsTemporaryQuarantined(itemId));
    }

    [Fact]
    public void MissingEngagementEvidenceQuarantinesWithTheExactCause()
    {
        var itemId = Guid.NewGuid();
        var state = SubmittedState(itemId, submittedFrame: 10);

        var observation = AutoItemsTemporaryActivationPolicy.Observe(
            FollowUpWorld(itemId, frame: 11),
            ref state);

        Assert.Equal(AutoItemsTemporaryActivationState.Quarantined, observation.State);
        Assert.Equal(
            AutoItemsTemporaryQuarantineCause.MissingEngagementEvidence,
            observation.QuarantineCause);
        Assert.True(state.IsTemporaryQuarantined(itemId));
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

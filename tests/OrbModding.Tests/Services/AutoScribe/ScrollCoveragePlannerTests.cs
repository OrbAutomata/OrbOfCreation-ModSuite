using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoScribe;

public sealed class ScrollCoveragePlannerTests
{
    private readonly AutoScribeIdentityProfile _profile = AutoScribeIdentityCatalog.Audited;

    [Fact]
    public void SemanticCostRankChoosesAdvancementBeforeOtherDeficits()
    {
        var plan = ScrollCoveragePlanner.Build(World(), _profile);

        var selection = plan.ChooseCraft(
            enabledRoles: null,
            afterCraftCostOrder: -1);
        var selected = selection.SelectedScroll;

        Assert.Equal(ScrollCraftSelectionKind.Selected, selection.Kind);
        Assert.Equal("scribe.advancement", selected.Role.Value);
        Assert.Equal(0, selected.CraftCostOrder);
    }

    [Fact]
    public void ProductionSelectionRotatesAcrossEveryProducibleRole()
    {
        var plan = ScrollCoveragePlanner.Build(World(), _profile);
        var expected = new[]
        {
            "scribe.advancement",
            "scribe.power",
            "scribe.learning",
            "scribe.excellence",
            "scribe.development",
            "scribe.echo",
            "scribe.advancement",
        };
        var cursor = -1;

        for (var index = 0; index < expected.Length; index++)
        {
            var selection = plan.ChooseCraft(
                enabledRoles: null,
                cursor);
            var selected = selection.SelectedScroll;
            Assert.Equal(ScrollCraftSelectionKind.Selected, selection.Kind);
            Assert.Equal(expected[index], selected.Role.Value);
            cursor = selected.CraftCostOrder;
        }
    }

    [Fact]
    public void CoveredRoleProbesItsOwnNextLevel()
    {
        var role = _profile.Roles[0];
        var world = World() with
        {
            ConsumableCounts = Table(new WorldConsumableCount(
                role.Scroll.Uuid,
                level: 3,
                quantity: 1,
                freeQuantity: 1)),
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            role.Key);

        Assert.Equal(ScrollCoverageState.Covered, coverage.State);
        Assert.False(coverage.ShouldProduce);
        Assert.True(coverage.ShouldProbeProgression);
        Assert.Equal(3, coverage.TargetLevel);
        Assert.Equal(4, coverage.RequestedCraftLevel);
    }

    [Fact]
    public void DemandBelowCarryRequestsOnlyUncoveredCount()
    {
        var role = _profile.Roles[0];
        var world = World(candidateCount: 2) with
        {
            Consumables = ScrollsWithOverride(
                role.Key,
                maxCreatedLevel: 3,
                maximumCarryLoad: 4),
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            role.Key);

        Assert.Equal(2, coverage.ValidTargets);
        Assert.Equal(2, coverage.Deficit);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, coverage.State);
    }

    [Fact]
    public void DemandAboveCarryClampsInFlightSupplyToCarry()
    {
        var role = _profile.Roles[0];
        var world = World(candidateCount: 7) with
        {
            Consumables = ScrollsWithOverride(
                role.Key,
                maxCreatedLevel: 3,
                maximumCarryLoad: 4),
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            role.Key);

        Assert.Equal(7, coverage.ValidTargets);
        Assert.Equal(4, coverage.Deficit);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, coverage.State);
    }

    [Fact]
    public void FullTargetLevelInventoryStallsWithoutEqualLevelChurn()
    {
        var role = _profile.Roles[0];
        var world = World(candidateCount: 7) with
        {
            ConsumableCounts = Table(new WorldConsumableCount(
                role.Scroll.Uuid,
                level: 3,
                quantity: 4,
                freeQuantity: 0)),
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            role.Key);

        Assert.Equal(4, coverage.OwnedSupply);
        Assert.Equal(0, coverage.Deficit);
        Assert.Equal(ScrollCoverageState.Covered, coverage.State);
        Assert.False(coverage.ShouldProduce);
        Assert.True(coverage.ShouldProbeProgression);
        Assert.Equal(4, coverage.RequestedCraftLevel);
    }

    [Fact]
    public void ExternalGiftShrinksDeficitOnNextPublication()
    {
        var role = _profile.Roles[0];
        var before = FindRole(
            ScrollCoveragePlanner.Build(World(candidateCount: 3), _profile),
            role.Key);
        var afterWorld = World(candidateCount: 3) with
        {
            ConsumableCounts = Table(new WorldConsumableCount(
                role.Scroll.Uuid,
                level: 3,
                quantity: 1,
                freeQuantity: 1)),
        };

        var after = FindRole(
            ScrollCoveragePlanner.Build(afterWorld, _profile),
            role.Key);

        Assert.Equal(3, before.Deficit);
        Assert.Equal(1, after.OwnedSupply);
        Assert.Equal(2, after.Deficit);
    }

    [Fact]
    public void WeakerOwnedStockDoesNotReduceDemandOrRequestCleanup()
    {
        var role = _profile.Roles[0];
        var world = World(candidateCount: 2) with
        {
            ConsumableCounts = Table(new WorldConsumableCount(
                role.Scroll.Uuid,
                level: 2,
                quantity: 4,
                freeQuantity: 0)),
        };

        var plan = ScrollCoveragePlanner.Build(world, _profile);
        var coverage = FindRole(plan, role.Key);
        var selection = plan.ChooseCraft(Table(role.Key), afterCraftCostOrder: -1);

        Assert.Equal(3, coverage.TargetLevel);
        Assert.Equal(0, coverage.OwnedSupply);
        Assert.Equal(2, coverage.Deficit);
        Assert.Equal(ScrollCoverageState.ProductionNeeded, coverage.State);
        Assert.Equal(ScrollCraftSelectionKind.Selected, selection.Kind);
        Assert.Equal(3, selection.SelectedScroll.RequestedCraftLevel);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCarryRefusesGuaranteedPaidLossWithNamedReason(
        int maximumCarryLoad)
    {
        var role = _profile.Roles[0];
        var plan = ScrollCoveragePlanner.Build(
            World(candidateCount: 3, maximumCarryLoad: maximumCarryLoad),
            _profile);
        var coverage = FindRole(plan, role.Key);
        var selection = plan.ChooseCraft(Table(role.Key), afterCraftCostOrder: -1);

        Assert.Equal(3, coverage.ValidTargets);
        Assert.Equal(0, coverage.Deficit);
        Assert.Equal(ScrollCoverageState.NativeGainUnavailable, coverage.State);
        Assert.Equal(
            AutoScribeEvidenceReason.NonPositiveCarryLimit,
            coverage.EvidenceReason);
        Assert.False(coverage.ShouldAttemptCraft);
        Assert.Equal(ScrollCraftSelectionKind.EvidenceBlocked, selection.Kind);
        Assert.Equal(
            AutoScribeEvidenceReason.NonPositiveCarryLimit,
            selection.BlockedReason);
        Assert.Contains(
            "native Gain() silently drops",
            ScrollCoveragePlanner.DescribeEvidence(in coverage));
    }

    [Fact]
    public void SharedScribeMaximumDoesNotRaiseAnotherRecipesFrontier()
    {
        var power = FindProfileRole("scribe.power");
        var world = World() with
        {
            CraftingRecipeTypes = Table(RecipeType(maxStartingLevel: 67)),
            Consumables = ScrollsWithOverride(power.Key, maxCreatedLevel: 24),
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            power.Key);

        Assert.Equal(24, coverage.TargetLevel);
        Assert.Equal(25, coverage.ProgressionLevel);
        Assert.NotEqual(67, coverage.TargetLevel);
    }

    [Fact]
    public void HigherQueuedWorkRaisesOnlyItsRecipesFrontierAndSuppressesProbe()
    {
        var advancement = FindProfileRole("scribe.advancement");
        var world = World() with
        {
            ScribeWork = Table(new WorldScribeWork(
                _profile.ActiveInstances.Uuid,
                advancement.Recipe!.Value.Uuid,
                level: 17,
                isAutomatic: false,
                isExpired: false)),
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            advancement.Key);

        Assert.Equal(17, coverage.TargetLevel);
        Assert.Equal(1, coverage.QueuedSupply);
        Assert.False(coverage.ShouldProbeProgression);
    }

    [Fact]
    public void VisibleRecipeWithoutItsScrollFrontierFailsClosed()
    {
        var advancement = FindProfileRole("scribe.advancement");
        var world = World() with
        {
            Consumables = PublicationTable<WorldConsumable>.Empty,
        };

        var coverage = FindRole(
            ScrollCoveragePlanner.Build(world, _profile),
            advancement.Key);

        Assert.Equal(ScrollCoverageState.EvidenceUnknown, coverage.State);
        Assert.Equal(
            AutoScribeEvidenceReason.TargetLevelUnavailable,
            coverage.EvidenceReason);
        Assert.False(coverage.ShouldAttemptCraft);
    }

    [Fact]
    public void UnknownEnabledRoleBlocksWholePublicationBeforeHealthyRoleCanProduce()
    {
        var plan = ScrollCoveragePlanner.Build(
            World(omitTargetEvidenceForRole: 1),
            _profile);

        var selection = plan.ChooseCraft(
            enabledRoles: null,
            afterCraftCostOrder: -1);
        var blocked = plan.Roles[selection.BlockedRoleOrdinal];

        Assert.Equal(ScrollCraftSelectionKind.EvidenceBlocked, selection.Kind);
        Assert.Equal("scribe.development", blocked.Role.Value);
        Assert.Equal(AutoScribeEvidenceReason.TargetEvidenceMissing, selection.BlockedReason);
        Assert.Contains("Development", ScrollCoveragePlanner.DescribeEvidence(in blocked));
        Assert.Contains("target relationship was unavailable",
            ScrollCoveragePlanner.DescribeEvidence(in blocked));
    }

    [Fact]
    public void DefaultSelectionResultIsInvalid()
    {
        var selection = default(ScrollCraftSelectionResult);

        Assert.Equal(ScrollCraftSelectionKind.Invalid, selection.Kind);
        Assert.Throws<InvalidOperationException>(() => selection.SelectedScroll);
    }

    [Fact]
    public void IdleSelectionDoesNotMasqueradeAsEvidenceBlocked()
    {
        var world = World(candidateCount: 0, maxCreatedLevel: int.MaxValue);
        var selection = ScrollCoveragePlanner.Build(world, _profile).ChooseCraft(
            enabledRoles: null,
            afterCraftCostOrder: -1);
        var active = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration { Mode = AutoScribeOperationMode.Active },
        };
        var store = new ReusableActionStore<AutoScribeCycleAction>();
        store.BeginWrite();

        AutoScribeCycleEvaluator.Evaluate(
            world,
            in active,
            _profile,
            enabledRoles: null,
            afterCraftCostOrder: -1,
            new ServiceActionWriter<AutoScribeCycleAction>(store),
            out var metrics);

        Assert.Equal(ScrollCraftSelectionKind.Idle, selection.Kind);
        Assert.Equal(AutoScribeDecisionKind.Idle, metrics.Kind);
        Assert.Equal(-1, metrics.BlockedRoleOrdinal);
        Assert.Equal(AutoScribeEvidenceReason.None, metrics.BlockedReason);
        Assert.Equal(0, store.Count);
    }

    [Fact]
    public void AutomaticWorkIsExternalPressureAndNeverAProductionPlan()
    {
        var automaticRole = _profile.Roles[0];
        var world = World() with
        {
            ScribeWork = Table(new WorldScribeWork(
                _profile.AutomaticInstances.Uuid,
                automaticRole.Recipe!.Value.Uuid,
                level: 3,
                isAutomatic: true,
                isExpired: false)),
        };

        var plan = ScrollCoveragePlanner.Build(world, _profile);

        Assert.Equal(ScrollCoverageState.ExternallyProducing, plan.Roles[0].State);
        Assert.False(plan.Roles[0].ShouldProduce);
    }

    [Fact]
    public void RoleSelectionUsesSemanticKeysAndEmptyMeansEveryAuditedRole()
    {
        Assert.Null(AutoScribeRoleSelection.ParsePublication(string.Empty, _profile.Roles));

        var selected = AutoScribeRoleSelection.ParsePublication(
            "scribe.power,native-uuid-is-not-a-role",
            _profile.Roles);

        Assert.NotNull(selected);
        Assert.Equal(1, selected!.Count);
        Assert.Equal("scribe.power", selected[0].Value);
        Assert.False(AutoScribeRoleSelection.Contains(
            selected,
            new ScrollRoleKey("scribe.advancement")));
    }

    [Fact]
    public void EveryEvaluatorDispositionWaitsOnlyForAnotherPublication()
    {
        var active = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration { Mode = AutoScribeOperationMode.Active },
        };

        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(new GameWorldState(), new SuiteRuntimeConfiguration(), out var disabled));
        Assert.Equal(0, disabled);
        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(World(), active, out var planned));
        Assert.Equal(1, planned);
        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(World(omitTargetEvidenceForRole: 1), active, out var blocked));
        Assert.Equal(0, blocked);
        Assert.Equal(
            WakePolicy.OnPublication,
            Evaluate(
                World(candidateCount: 0, maxCreatedLevel: int.MaxValue),
                active,
                out var idle));
        Assert.Equal(0, idle);
    }

    [Fact]
    public void FullActiveQueueIsPublishedBackpressureAndNeverBecomesAnActionFault()
    {
        var world = World() with
        {
            ScribeQueues = Table(
                new WorldScribeQueue(
                    _profile.ActiveInstances.Uuid,
                    isAutomatic: false,
                    used: 1,
                    maximum: 1),
                new WorldScribeQueue(
                    _profile.AutomaticInstances.Uuid,
                    isAutomatic: true,
                    used: 0,
                    maximum: 1)),
        };
        var active = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoItems = new AutoItemsConfiguration
            {
                Mode = AutoItemsOperationMode.Active,
                UseScrolls = true,
            },
            AutoScribe = new AutoScribeConfiguration { Mode = AutoScribeOperationMode.Active },
        };
        var store = new ReusableActionStore<AutoScribeCycleAction>();
        store.BeginWrite();

        AutoScribeCycleEvaluator.Evaluate(
            world,
            in active,
            _profile,
            enabledRoles: null,
            afterCraftCostOrder: -1,
            new ServiceActionWriter<AutoScribeCycleAction>(store),
            out var metrics);

        Assert.Equal(AutoScribeDecisionKind.QueueBusy, metrics.Kind);
        Assert.Equal(6, metrics.DeficientRoles);
        Assert.Equal(0, metrics.PlannedActions);
        Assert.Equal(0, store.Count);
    }

    private WakePolicy Evaluate(
        GameWorldState world,
        SuiteRuntimeConfiguration configuration,
        out int actionCount)
    {
        var store = new ReusableActionStore<AutoScribeCycleAction>();
        store.BeginWrite();
        var wake = AutoScribeCycleEvaluator.Evaluate(
            world,
            in configuration,
            _profile,
            enabledRoles: null,
            afterCraftCostOrder: -1,
            new ServiceActionWriter<AutoScribeCycleAction>(store),
            out _);
        actionCount = store.Count;
        return wake;
    }

    private GameWorldState World(
        int omitTargetEvidenceForRole = -1,
        int candidateCount = 1,
        int maxCreatedLevel = 3,
        int maximumCarryLoad = 4)
    {
        var recipes = new List<WorldScribeRecipe>();
        var consumables = new List<WorldConsumable>();
        var targets = new List<WorldScrollTarget>();
        var evidence = new List<WorldScrollTargetEvidence>();
        for (var index = 0; index < _profile.Roles.Count; index++)
        {
            var role = _profile.Roles[index];
            if (!role.IsProducible) continue;
            recipes.Add(new WorldScribeRecipe(
                role.Recipe!.Value.Uuid,
                _profile.RecipeType.Uuid,
                role.Scroll.Uuid,
                visible: true,
                usesQuantityAsLevel: true));
            consumables.Add(Scroll(
                role.Scroll.Uuid,
                maxCreatedLevel,
                maximumCarryLoad));
            for (var candidateIndex = 0; candidateIndex < candidateCount; candidateIndex++)
                targets.Add(new WorldScrollTarget(
                    role.Scroll.Uuid,
                    role.Enchantment.Uuid,
                    Guid.NewGuid()));
            if (index != omitTargetEvidenceForRole)
                evidence.Add(new WorldScrollTargetEvidence(
                    role.Scroll.Uuid,
                    role.Enchantment.Uuid,
                    candidateCount));
        }

        return new GameWorldState
        {
            CollectedAtFrame = 10,
            CollectedAtEpoch = 1,
            CollectionCategories = Table(new WorldCollectionCategoryStatus(
                ScrollCoveragePlanner.CollectionCategory,
                WorldCategoryOutcome.Collected,
                sampled: 1,
                skipped: 0,
                firstFailure: string.Empty)),
            ScribeRecipes = Table(recipes.ToArray()),
            ScribeQueues = Table(
                new WorldScribeQueue(
                    _profile.ActiveInstances.Uuid,
                    isAutomatic: false,
                    used: 0,
                    maximum: 1),
                new WorldScribeQueue(
                    _profile.AutomaticInstances.Uuid,
                    isAutomatic: true,
                    used: 0,
                    maximum: 1)),
            Consumables = Table(consumables.ToArray()),
            ScrollTargets = Table(targets.ToArray()),
            ScrollTargetEvidence = Table(evidence.ToArray()),
            CraftingRecipeTypes = Table(new WorldCraftingRecipeType(
                _profile.RecipeType.Uuid,
                startingLevel: 1,
                maxStartingLevel: 3,
                craftVerb: "Scribe",
                isLevelType: true,
                initiated: true,
                magnitudeLoss: 0,
                magnitudeTime: 0,
                magnitudeIncrement: BigDouble.Zero,
                powerModifiers: 0,
                speedModifiers: 0,
                costModModifiers: 0,
                costIncrementModModifiers: 0,
                efficiencyModModifiers: 0,
                autoPenaltyModModifiers: 0,
                multiPenaltyModModifiers: 0)),
        };
    }

    private AutoScribeRoleDescriptor FindProfileRole(string key)
    {
        Assert.True(_profile.TryFind(new ScrollRoleKey(key), out var role));
        return role;
    }

    private static ScrollRoleCoverage FindRole(
        ScrollCoveragePlan plan,
        ScrollRoleKey key)
    {
        for (var index = 0; index < plan.Roles.Length; index++)
            if (plan.Roles[index].Role == key)
                return plan.Roles[index];
        throw new InvalidOperationException($"Coverage role {key.Value} was absent.");
    }

    private PublicationTable<WorldConsumable> ScrollsWithOverride(
        ScrollRoleKey overrideRole,
        int maxCreatedLevel,
        int maximumCarryLoad = 4)
    {
        var rows = new List<WorldConsumable>();
        for (var index = 0; index < _profile.Roles.Count; index++)
        {
            var role = _profile.Roles[index];
            if (!role.IsProducible) continue;
            rows.Add(Scroll(
                role.Scroll.Uuid,
                role.Key == overrideRole ? maxCreatedLevel : 3,
                role.Key == overrideRole ? maximumCarryLoad : 4));
        }
        return Table(rows.ToArray());
    }

    private static WorldConsumable Scroll(
        Guid scrollId,
        int maxCreatedLevel,
        int maximumCarryLoad = 4)
    {
        var modifiers = default(RawConsumableModifiers);
        return new WorldConsumable(
            scrollId,
            visible: true,
            randomized: true,
            quantity: 0,
            queuedQuantity: 0,
            maximumCarryLoad,
            gainedSince: 0,
            maxCreatedLevel,
            currentPrepTime: BigDouble.Zero,
            currentCooldown: BigDouble.Zero,
            currentCooldownTime: BigDouble.Zero,
            in modifiers,
            preparationTime: 0,
            canBeRandomized: true,
            hasDuration: false,
            durationBase: 0,
            queueOnStart: false);
    }

    private static WorldCraftingRecipeType RecipeType(int maxStartingLevel) =>
        new(
            KnownEntities.ScribeCrafting.Uuid,
            startingLevel: 1,
            maxStartingLevel,
            craftVerb: "Scribe",
            isLevelType: true,
            initiated: true,
            magnitudeLoss: 0,
            magnitudeTime: 0,
            magnitudeIncrement: BigDouble.Zero,
            powerModifiers: 0,
            speedModifiers: 0,
            costModModifiers: 0,
            costIncrementModModifiers: 0,
            efficiencyModModifiers: 0,
            autoPenaltyModModifiers: 0,
            multiPenaltyModModifiers: 0);

    private static PublicationTable<T> Table<T>(params T[] rows)
        where T : struct =>
        rows.Length == 0
            ? PublicationTable<T>.Empty
            : PublicationTable<T>.Create(rows, rows.Length);
}

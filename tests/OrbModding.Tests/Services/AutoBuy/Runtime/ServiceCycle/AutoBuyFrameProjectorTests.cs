using System;
using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoBuy.Runtime.ServiceCycle;

/// <summary>
/// Auto Buy's projection of the pinned world snapshot into its frame. It runs on the worker thread;
/// what the main thread still does is pin the publication. See W50.
/// </summary>
public sealed class AutoBuyFrameProjectorTests : IDisposable
{
    public AutoBuyFrameProjectorTests()
    {
        ResetNativeState();
    }

    public void Dispose()
    {
        ResetNativeState();
    }

    [Fact]
    public void Project_ReadsGlobalsAndStructureCandidateWithCostAndResource()
    {
        global::ActionManager.instance.actionableItems.maxQueuedItems.Value = 12;
        global::ActionManager.RemainingRoom = 5;
        global::GlobalVariables.MultiBuy.Value = 3;
        global::Player.BulkDevelopment.Value = 2;

        var resource = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(4.0, 6),
            bandwidthResource = false,
        };
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            quantity = 7,
            queuedQuantity = 9,
        };
        PriceStructure(structure, resource, new BigDouble(2.0, 1));
        global::ResourceSO.All.Add(resource);
        global::StructureSO.All.Add(structure);

        var frame = Project(Config(structures: true, upgrades: true));

        Assert.Equal(2, frame.Global.BulkDevelopment);

        var candidate = Assert.Single(frame.Candidates.ToArray());
        Assert.Equal(AutoBuyCandidateKind.Structure, candidate.Kind);
        Assert.Equal(Guid.Parse(structure.uuid), candidate.Uuid);
        Assert.True(candidate.IsAvailable);
        Assert.Equal(7, candidate.CurrentLevel);
        Assert.Equal(9, candidate.QueuedLevels);
        Assert.Equal(1, candidate.CostRowCount);

        var cost = frame.Costs[candidate.CostRowStart];
        Assert.Equal(2.0, cost.Cost.Mantissa);
        Assert.Equal(1, cost.Cost.Exponent);
        Assert.Equal(2, cost.ExactGroupedLevels);
        Assert.Equal(40d, cost.ExactGroupedCost.ToDouble(), 6);

        var resourceRow = frame.Resources[cost.ResourceRowIndex];
        Assert.Equal(Guid.Parse(resource.uuid), resourceRow.ResourceId);
        Assert.Equal(4.0, resourceRow.TrueQuantity.Mantissa);
        Assert.Equal(6, resourceRow.TrueQuantity.Exponent);
    }

    [Fact]
    public void Project_LockedOwningView_PublishesNamedPlannerGate()
    {
        global::ViewSO.All[0].available = false;
        var resource = Resource();
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        PriceStructure(structure, resource, new BigDouble(1.0, 0));
        global::StructureSO.All.Add(structure);

        var candidate = Assert.Single(
            Project(Config(structures: true, upgrades: false)).Candidates.ToArray());

        Assert.True(candidate.IsAvailable);
        Assert.Equal(AutoBuyOwningViewStatus.Unavailable, candidate.OwningView);
    }

    [Fact]
    public void Project_ReadsAuthoredListMembershipInsteadOfTheGlobalTypeRegistry()
    {
        global::ViewSO.All.Clear();
        var resource = Resource();
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        PriceStructure(structure, resource, new BigDouble(1.0, 0));
        global::StructureSO.All.Add(structure);

        var containingList = new global::StructureListVariable();
        containingList.value.Add(structure);
        var unrelatedList = new global::StructureListVariable();
        var containingView = new global::ViewSO { available = true };
        containingView.relevantLists.Add(containingList);
        var unrelatedView = new global::ViewSO { available = true };
        unrelatedView.relevantLists.Add(unrelatedList);
        global::ViewSO.All.Add(containingView);
        global::ViewSO.All.Add(unrelatedView);

        Assert.Same(global::StructureSO.All, unrelatedList.GetAll());
        Assert.Empty(unrelatedList.value);
        var candidate = Assert.Single(
            Project(Config(structures: true, upgrades: false)).Candidates.ToArray());

        Assert.Equal(AutoBuyOwningViewStatus.Available, candidate.OwningView);
    }

    [Theory]
    [InlineData(true, true, (int)AutoBuyOwningViewStatus.Available, 1)]
    [InlineData(true, false, (int)AutoBuyOwningViewStatus.Available, 1)]
    [InlineData(false, false, (int)AutoBuyOwningViewStatus.Unavailable, 0)]
    public void CollectorProjectorEvaluator_AppliesAnyVisibleRouteAdmission(
        bool firstVisible,
        bool secondVisible,
        int expectedStatus,
        int expectedActions)
    {
        global::ViewSO.All.Clear();
        var resource = Resource();
        resource.quantity = new BigDouble(1.0, 6);
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        PriceStructure(structure, resource, new BigDouble(1.0, 0));
        global::StructureSO.All.Add(structure);
        AddStructureRoute(structure, firstVisible);
        AddStructureRoute(structure, secondVisible);

        var config = Config(structures: true, upgrades: false);
        var frame = Project(config);

        Assert.Equal((AutoBuyOwningViewStatus)expectedStatus, Assert.Single(frame.Candidates.ToArray()).OwningView);
        Assert.Equal(expectedActions, PlannedActionCount(in frame, in config));
    }

    [Fact]
    public void CollectorProjectorEvaluator_LockedParentAndVisibleChildSharingOneList_FailsClosed()
    {
        global::ViewSO.All.Clear();
        var resource = Resource();
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        PriceStructure(structure, resource, new BigDouble(1.0, 0));
        global::StructureSO.All.Add(structure);

        var sharedList = new global::StructureListVariable { value = global::StructureSO.All };
        var lockedParent = new global::ViewSO { available = false };
        lockedParent.relevantLists.Add(sharedList);
        var visibleChild = new global::ViewSO { available = true };
        visibleChild.relevantLists.Add(sharedList);
        global::ViewSO.All.Add(lockedParent);
        global::ViewSO.All.Add(visibleChild);

        var config = Config(structures: true, upgrades: false);
        var frame = Project(config);

        Assert.Equal(
            AutoBuyOwningViewStatus.Unavailable,
            Assert.Single(frame.Candidates.ToArray()).OwningView);
        Assert.Equal(0, PlannedActionCount(in frame, in config));
    }

    [Fact]
    public void CollectorProjectorEvaluator_ZeroRoutesFailsClosedAsMissing()
    {
        global::ViewSO.All.Clear();
        var resource = Resource();
        resource.quantity = new BigDouble(1.0, 6);
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        PriceStructure(structure, resource, new BigDouble(1.0, 0));
        global::StructureSO.All.Add(structure);

        var config = Config(structures: true, upgrades: false);
        var frame = Project(config);

        Assert.Equal(
            AutoBuyOwningViewStatus.RelationMissing,
            Assert.Single(frame.Candidates.ToArray()).OwningView);
        Assert.Equal(0, PlannedActionCount(in frame, in config));
    }

    [Fact]
    public void CollectorProjectorEvaluator_DuplicateMembershipFailsClosedAsContradictory()
    {
        global::ViewSO.All.Clear();
        var resource = Resource();
        resource.quantity = new BigDouble(1.0, 6);
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        PriceStructure(structure, resource, new BigDouble(1.0, 0));
        global::StructureSO.All.Add(structure);
        var list = new global::StructureListVariable();
        list.value.Add(structure);
        list.value.Add(structure);
        var view = new global::ViewSO { available = true };
        view.relevantLists.Add(list);
        global::ViewSO.All.Add(view);

        var config = Config(structures: true, upgrades: false);
        var frame = Project(config);

        Assert.Equal(
            AutoBuyOwningViewStatus.RelationContradictory,
            Assert.Single(frame.Candidates.ToArray()).OwningView);
        Assert.Equal(0, PlannedActionCount(in frame, in config));
    }

    [Fact]
    public void Project_UpgradeQueuedLevelsAreQueuedMinusCurrent()
    {
        var upgrade = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            level = 4,
            queuedLevels = 2,
            maxLevel = 10,
        };
        PriceUpgrade(upgrade, Resource(), new BigDouble(2.0, 1));
        global::UpgradeSO.All.Add(upgrade);

        var frame = Project(Config(structures: false, upgrades: true));

        var candidate = Assert.Single(frame.Candidates.ToArray());
        Assert.Equal(AutoBuyCandidateKind.Upgrade, candidate.Kind);
        Assert.Equal(4, candidate.CurrentLevel);
        Assert.Equal(2, candidate.QueuedLevels);
        Assert.True(candidate.HasFiniteLevels);
        Assert.False(candidate.IsMaxLevel);
        Assert.False(candidate.IsMaxQueuedLevel);
        Assert.True(candidate.MeetsNextLevelRequirements);
    }

    /// <summary>
    /// The shape that started all of this. ScribeScroll4 is available, unfinished and affordable, and
    /// the game refuses it until ImprovedScribing reaches level six — a condition on the level being
    /// bought, which nothing in the frame could see.
    /// </summary>
    [Fact]
    public void Project_RefusesAnUpgradeWhoseNextLevelWaitsOnResearch()
    {
        var scribing = new global::ResearchSO { level = 5, maxLevel = 10 };
        global::ResearchSO.All.Add(scribing);

        var scroll = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            maxLevel = 10,
        };
        scroll.prerequisitesPerLevel.prerequisites.Add(new Requirements.ResearchRequirement
        {
            item = scribing,
            reqType = Requirements.UpgradeRequirementType.AtLeast,
            value = new Requirements.LeveledValue { baseValue = 6d },
        });
        PriceUpgrade(scroll, Resource(), new BigDouble(2.0, 1));
        global::UpgradeSO.All.Add(scroll);

        Assert.False(
            Assert.Single(Project(Config(structures: false, upgrades: true)).Candidates.ToArray())
                .MeetsNextLevelRequirements);

        scribing.level = 6;

        Assert.True(
            Assert.Single(Project(Config(structures: false, upgrades: true)).Candidates.ToArray())
                .MeetsNextLevelRequirements);
    }

    /// <summary>
    /// A condition class the suite cannot evaluate refuses the purchase too. The frame carries one
    /// bool, so "does not hold" and "cannot be read" arrive the same way — which is right, because
    /// only one thing may follow from either.
    /// </summary>
    [Fact]
    public void Project_RefusesAnUpgradeGatedByAConditionNobodyModelled()
    {
        var gated = new global::UpgradeSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            maxLevel = 10,
        };
        gated.prerequisitesPerLevel.prerequisites.Add(new Requirements.UnsupportedRequirement());
        PriceUpgrade(gated, Resource(), new BigDouble(2.0, 1));
        global::UpgradeSO.All.Add(gated);

        var candidate = Assert.Single(Project(Config(structures: false, upgrades: true)).Candidates.ToArray());

        Assert.True(candidate.IsAvailable);
        Assert.False(candidate.MeetsNextLevelRequirements);
    }

    [Fact]
    public void Project_ExcludesKindsNotEnabledInConfig()
    {
        var resource = Resource();
        var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
        PriceStructure(structure, resource, new BigDouble(2.0, 1));
        global::StructureSO.All.Add(structure);

        var upgrade = new global::UpgradeSO { uuid = Guid.NewGuid().ToString() };
        PriceUpgrade(upgrade, resource, new BigDouble(2.0, 1));
        global::UpgradeSO.All.Add(upgrade);

        var frame = Project(Config(structures: true, upgrades: false));

        var candidate = Assert.Single(frame.Candidates.ToArray());
        Assert.Equal(AutoBuyCandidateKind.Structure, candidate.Kind);
    }

    [Fact]
    public void Project_DeduplicatesResourceRowsSharedByCandidates()
    {
        var resource = Resource();
        for (var i = 0; i < 2; i++)
        {
            var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
            PriceStructure(structure, resource, new BigDouble(1.0, i));
            global::StructureSO.All.Add(structure);
        }

        var frame = Project(Config(structures: true, upgrades: false));

        Assert.Equal(2, frame.CandidateCount);
        Assert.Equal(2, frame.StructureCount);
        Assert.Equal(0, frame.UpgradeCount);
        Assert.Equal(2, frame.CostCount);
        Assert.Equal(1, frame.ResourceCount);
        Assert.Equal(0, frame.Costs[0].ResourceRowIndex);
        Assert.Equal(0, frame.Costs[1].ResourceRowIndex);
    }

    /// <summary>
    /// Each cost row points at the resource it actually names.
    /// </summary>
    /// <remarks>
    /// Deduplication is a linear scan over the rows already written, so it is one comparison away
    /// from returning whichever row it looked at first. Every other projection test prices everything
    /// in one resource, where a scan that ignored identity would give the same answer as one that
    /// respected it — so two distinct resources are what make the comparison load-bearing.
    /// </remarks>
    [Fact]
    public void Project_PointsEachCostRowAtTheResourceItNames()
    {
        var first = Resource();
        var second = Resource();
        foreach (var resource in new[] { first, second })
        {
            var structure = new global::StructureSO { uuid = Guid.NewGuid().ToString() };
            PriceStructure(structure, resource, new BigDouble(1.0, 0));
            global::StructureSO.All.Add(structure);
        }

        var frame = Project(Config(structures: true, upgrades: false));

        Assert.Equal(2, frame.ResourceCount);
        var byCandidate = new[]
        {
            frame.Resources[frame.Costs[frame.Candidates[0].CostRowStart].ResourceRowIndex].ResourceId,
            frame.Resources[frame.Costs[frame.Candidates[1].CostRowStart].ResourceRowIndex].ResourceId,
        };
        Assert.Contains(Guid.Parse(first.uuid), byCandidate);
        Assert.Contains(Guid.Parse(second.uuid), byCandidate);
        Assert.NotEqual(byCandidate[0], byCandidate[1]);
    }

    /// <summary>
    /// No world is a wiring mistake, not an empty game.
    /// </summary>
    /// <remarks>
    /// The runtime cannot hand over null — the publisher refuses a null snapshot — so this can only
    /// happen to a caller that composed the projector itself. Substituting the empty world would turn
    /// that into a service that finds nothing to buy and reports it as a quiet save.
    /// </remarks>
    [Fact]
    public void Project_RefusesAWorldThatIsNotThere()
    {
        var frame = default(AutoBuyCycleFrame);
        var config = Config(structures: true, upgrades: false);

        Assert.Throws<ArgumentNullException>(
            () => AutoBuyFrameProjector.Project(ref frame, in config, null!));
    }

    /// <summary>
    /// The frame takes its epoch from the world it was projected from, which is what every purchase
    /// planned off it is judged by at the boundary.
    /// </summary>
    /// <remarks>
    /// It used to be the runner's own lifecycle generation, passed in beside the world and read by
    /// nothing. The two answer different questions: a runner's generation is frozen when the runner is
    /// built, while the snapshot's epoch says which run of the game was actually read. The projector is
    /// handed no lifecycle at all now, so the two cannot be confused again.
    /// </remarks>
    [Fact]
    public void Project_TakesTheEpochFromTheWorldRatherThanTheRunner()
    {
        var config = Config(structures: true, upgrades: true);

        var frame = Project(config, new GameWorldState { CollectedAtEpoch = 31 });

        Assert.Equal(31, frame.Global.CollectedAtEpoch);
    }

    /// <summary>
    /// A world nobody collected carries no epoch, and the frame says so rather than substituting one.
    /// The boundary is what turns that into a refusal.
    /// </summary>
    [Fact]
    public void Project_OfAWorldNobodyCollected_CarriesNoEpoch()
    {
        var config = Config(structures: true, upgrades: true);

        var frame = Project(config, GameWorldStateDefaults.Empty);

        Assert.Equal(0, frame.Global.CollectedAtEpoch);
    }

    /// <summary>
    /// A second cycle overwrites what the first wrote into the frame's borrowed buffers.
    /// </summary>
    /// <remarks>
    /// The projector reuses the row arrays the frame hands it rather than allocating three per cycle,
    /// which is the whole reason the frame lends them out. Reuse that appended, or that left the
    /// previous cycle's rows visible past the new counts, would compile and would show the caller a
    /// world that is part this cycle's and part the last one's.
    /// </remarks>
    [Fact]
    public void Project_ReusesTheFramesBuffersWithoutCarryingStaleRows()
    {
        var id = Guid.NewGuid();
        var structure = new global::StructureSO
        {
            uuid = id.ToString(),
            available = true,
            queuedQuantity = 2,
        };
        PriceStructure(structure, Resource(), new BigDouble(2.0, 1));
        global::StructureSO.All.Add(structure);
        var config = Config(structures: true, upgrades: false);

        var frame = default(AutoBuyCycleFrame);
        AutoBuyFrameProjector.Project(ref frame, in config, TestWorlds.FromLoadedRegistries());
        var borrowed = frame.LendCandidates();
        Assert.Equal(id, frame.Candidates[0].Uuid);
        Assert.True(frame.Candidates[0].IsAvailable);
        Assert.Equal(2, frame.Candidates[0].QueuedLevels);

        structure.available = false;
        structure.queuedQuantity = 7;

        AutoBuyFrameProjector.Project(ref frame, in config, TestWorlds.FromLoadedRegistries());

        Assert.Same(borrowed, frame.LendCandidates());
        Assert.Equal(1, frame.CandidateCount);
        Assert.Equal(id, frame.Candidates[0].Uuid);
        Assert.False(frame.Candidates[0].IsAvailable);
        Assert.Equal(7, frame.Candidates[0].QueuedLevels);
    }

    /// <summary>
    /// The candidates are the snapshot's population, not the game's registry.
    /// </summary>
    /// <remarks>
    /// The projection used to walk <c>StructureSO.All</c> and look each entry's facts up in the snapshot
    /// by the stable id it read off the live object, so a registry entry the snapshot never described
    /// became a candidate that was then dropped, and a published entity the registry had forgotten was
    /// invisible. One place decides now. Pinning that needs a snapshot that disagrees with the
    /// registries, which is why this is the one projection test that publishes a hand-built world instead
    /// of collecting the stubs.
    /// </remarks>
    [Fact]
    public void Project_TakesItsCandidatesFromTheSnapshotRatherThanTheRegistry()
    {
        var published = Guid.NewGuid();
        var resource = Guid.NewGuid();
        global::StructureSO.All.Add(
            new global::StructureSO { uuid = Guid.NewGuid().ToString(), available = true });
        var frame = Project(
            Config(structures: true, upgrades: false),
            WorldPricing(published, resource));

        Assert.Equal(published, Assert.Single(frame.Candidates.ToArray()).Uuid);
    }

    /// <summary>
    /// A structure the snapshot published but did not price is not a candidate.
    /// </summary>
    /// <remarks>
    /// The deriver withholds a price it could not complete, so an entity with no cost rows is one
    /// whose price is unknown rather than one that is free. Projecting it with an empty cost range
    /// would make it the cheapest thing in the game and therefore the first thing bought.
    /// </remarks>
    [Fact]
    public void Project_DropsAPublishedStructureTheSnapshotDidNotPrice()
    {
        var priced = Guid.NewGuid();
        var unpriced = Guid.NewGuid();
        var frame = Project(
            Config(structures: true, upgrades: false),
            WorldPricing(priced, Guid.NewGuid(), alsoPublishing: unpriced));

        Assert.Equal(priced, Assert.Single(frame.Candidates.ToArray()).Uuid);
    }

    /// <summary>
    /// A bandwidth resource reaches the frame with the room it has left, not just what it holds.
    /// </summary>
    /// <remarks>
    /// The room is what a bandwidth cost is paid out of, and the snapshot has already worked it out
    /// — so the projection has only to carry it. Carrying the capacity alone would leave the worker
    /// to subtract, which is exactly the per-consumer arithmetic the shared snapshot exists to stop.
    /// </remarks>
    [Fact]
    public void Project_CarriesABandwidthResourcesRemainingRoom()
    {
        var structureId = Guid.NewGuid();
        var resourceId = Guid.NewGuid();
        var costs = new[] { new WorldPurchaseCost(structureId, resourceId, new BigDouble(2.0, 1)) };
        var world = new GameWorldState
        {
            Structures = WorldTable.Create(
                new[] { WorldStructureDeriver.Shared.Derive(WorldSamples.Structure(structureId)) }),
            Resources = WorldTable.Create(
                new[]
                {
                    new WorldResourceDeriver(default).Derive(
                        WorldSamples.Resource(
                            resourceId,
                            quantity: 60d,
                            capacity: 100d,
                            traits: WorldSamples.Traits(bandwidthResource: true))),
                }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(costs, costs.Length),
        };

        var frame = Project(Config(structures: true, upgrades: false), world);

        var row = frame.Resources[frame.Costs[frame.Candidates[0].CostRowStart].ResourceRowIndex];
        Assert.True(row.IsBandwidth);
        Assert.Equal(40d, row.Spendable.ToDouble());
    }

    /// <summary>
    /// One published structure with one published price, optionally alongside a second structure the
    /// snapshot describes but never prices.
    /// </summary>
    private static GameWorldState WorldPricing(
        Guid structureId,
        Guid resourceId,
        Guid alsoPublishing = default)
    {
        var costs = new[] { new WorldPurchaseCost(structureId, resourceId, new BigDouble(2.0, 1)) };
        var structures = alsoPublishing == Guid.Empty
            ? new[] { structureId }
            : new[] { structureId, alsoPublishing };
        return new GameWorldState
        {
            Structures = WorldTable.Create(
                Array.ConvertAll(
                    structures,
                    id => WorldStructureDeriver.Shared.Derive(WorldSamples.Structure(id)))),
            Resources = WorldTable.Create(
                new[]
                {
                    new WorldResourceDeriver(default).Derive(
                        WorldSamples.Resource(resourceId, new BigDouble(1.0, 3), new BigDouble(-1d))),
                }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(costs, costs.Length),
        };
    }

    /// <summary>
    /// A candidate is only captured if the snapshot published a price for it, so every fixture has
    /// to describe what the collection pass prices rather than what the game would have answered.
    /// </summary>
    /// <remarks>
    /// Every modifier involved is left at the parity the stubs author, so the published price is the
    /// authored cost unchanged and these tests stay about capture rather than about arithmetic —
    /// which <c>GameWorldCollectorTests</c> covers, away from parity, where it belongs.
    /// </remarks>
    private static void PriceStructure(
        global::StructureSO structure,
        global::ResourceSO resource,
        BigDouble amount)
    {
        structure.baseCost.costs.Add(new global::ResourceTuple(resource, amount));
        structure.costPerQuantity = new global::ValueModifierRef { variable = NeutralScaling() };
    }

    private static void PriceUpgrade(
        global::UpgradeSO upgrade,
        global::ResourceSO resource,
        BigDouble amount) =>
        upgrade.resourceCost.costs.Add(new global::ResourceTuple(resource, amount));

    /// <summary>The identity of the modifier stack: adding nothing, at parity.</summary>
    private static global::ValueModifierVariable NeutralScaling()
    {
        var variable = new global::ValueModifierVariable
        {
            value = new global::ValueModifier(
                global::ValueModifier.ValueModifierType.Raw, BigDouble.Zero),
        };
        global::ValueModifierVariable.All.Add(variable);
        return variable;
    }

    private static global::ResourceSO Resource()
    {
        var resource = new global::ResourceSO { uuid = Guid.NewGuid().ToString() };
        global::ResourceSO.All.Add(resource);
        return resource;
    }

    private static void AddStructureRoute(global::StructureSO structure, bool available)
    {
        var list = new global::StructureListVariable();
        list.value.Add(structure);
        var view = new global::ViewSO { available = available };
        view.relevantLists.Add(list);
        global::ViewSO.All.Add(view);
    }

    private static int PlannedActionCount(
        in AutoBuyCycleFrame frame,
        in SuiteRuntimeConfiguration config)
    {
        var store = new ReusableActionStore<AutoBuyCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoBuyCycleAction>(store);
        AutoBuyCycleEvaluator.Evaluate(in frame, in config, writer, out _);
        return store.Count;
    }

    private static AutoBuyCycleFrame Project(SuiteRuntimeConfiguration config) =>
        Project(config, TestWorlds.FromLoadedRegistries());

    /// <summary>Projects the world the runtime pinned, exactly as the worker does.</summary>
    private static AutoBuyCycleFrame Project(SuiteRuntimeConfiguration config, GameWorldState world)
    {
        var frame = default(AutoBuyCycleFrame);
        AutoBuyFrameProjector.Project(ref frame, in config, world);
        return frame;
    }

    private static SuiteRuntimeConfiguration Config(bool structures, bool upgrades) =>
        new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                IncludeStructures = structures,
                IncludeUpgrades = upgrades,
            },
        };

    private static void ResetNativeState()
    {
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ViewSO.All.Clear();
        var structures = new global::StructureListVariable { value = global::StructureSO.All };
        var upgrades = new global::UpgradeListVariable { value = global::UpgradeSO.All };
        var owningView = new global::ViewSO { available = true };
        owningView.relevantLists.Add(structures);
        owningView.relevantLists.Add(upgrades);
        global::ViewSO.All.Add(owningView);
        global::ResearchSO.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::GameManager.currentFrame = 0;
        global::ResourceSO.All.Clear();
        global::ValueModifierVariable.All.Clear();
        global::ActionManager.instance = new global::ActionManager();
        global::ActionManager.RemainingRoom = 0;
        global::GlobalVariables.MultiBuy = new global::IntVariable();
        global::Player.BulkDevelopment = new global::IntVariable();

        // Capturing warms the process-global native-contract caches and their
        // resolution counters (multi-buy and int-variable). Reset them so this
        // suite leaves no trace for order-sensitive assertions elsewhere.
        NativeMultiBuyScope.ResetQuarantineForTests();
    }
}

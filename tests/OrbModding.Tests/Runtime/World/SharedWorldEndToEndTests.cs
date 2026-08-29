using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The whole shared-world path with nothing faked: stub registries through the real collector into a
/// frame, derived on the worker half, published, pinned by a consuming service, and read back as
/// candidate facts.
/// </summary>
/// <remarks>
/// Every other test in this area substitutes something — a hand-built snapshot, a fake capture port,
/// a fake publisher. Each seam is worth isolating, and none of them proves the seams line up. This is
/// the test that fails if collection publishes rows Auto Buy cannot find, or finds under a different
/// identity, or reads a generation nobody advanced.
/// </remarks>
public sealed class SharedWorldEndToEndTests : IDisposable
{
    private readonly ServiceWorldPublisher<GameWorldState> _world = new(GameWorldStateDefaults.Empty);

    public SharedWorldEndToEndTests() => ResetRegistries();

    public void Dispose()
    {
        ResetRegistries();
        _world.Dispose();
    }

    [Fact]
    public void CollectionPublishesWhatAutoBuyThenCapturesAsCandidateFacts()
    {
        var resourceId = Guid.NewGuid();
        var structureId = Guid.NewGuid();
        var resource = new global::ResourceSO
        {
            uuid = resourceId.ToString(),
            quantity = new BigDouble(5.0, 0),
        };
        var structure = new global::StructureSO
        {
            uuid = structureId.ToString(),
            available = true,
            purchasable = true,
            quantity = 3,
            queuedQuantity = 1,
        };
        Price(structure, resource, new BigDouble(2.0, 0));
        global::ResourceSO.All.Add(resource);
        global::StructureSO.All.Add(structure);

        var generation = RunCollectionCycle();

        Assert.Equal(new WorldGeneration(2), generation);

        var frame = ProjectAutoBuy();

        var candidate = Assert.Single(frame.Candidates.ToArray());
        Assert.Equal(structureId, candidate.Uuid);
        Assert.Equal(3, candidate.CurrentLevel);
        Assert.Equal(1, candidate.QueuedLevels);
        Assert.True(candidate.IsAvailable);

        var cost = frame.Costs[candidate.CostRowStart];
        Assert.Equal(resourceId, frame.Resources[cost.ResourceRowIndex].ResourceId);
        Assert.Equal(5.0, frame.Resources[cost.ResourceRowIndex].TrueQuantity.ToDouble());
    }

    /// <summary>
    /// A consumer must see the world move. Auto Buy pins per capture, so a second collection cycle
    /// has to reach it without anyone re-wiring the source.
    /// </summary>
    [Fact]
    public void ASecondCollectionCycleReachesTheConsumer()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
            queuedQuantity = 1,
        };
        Price(structure, Resource(), new BigDouble(2.0, 0));
        global::StructureSO.All.Add(structure);

        RunCollectionCycle();
        var first = ProjectAutoBuy();
        Assert.Equal(1, first.Candidates[0].QueuedLevels);

        structure.queuedQuantity = 9;
        var second = RunCollectionCycle();
        var refreshed = ProjectAutoBuy();

        Assert.Equal(new WorldGeneration(3), second);
        Assert.Equal(9, refreshed.Candidates[0].QueuedLevels);
    }

    /// <summary>
    /// Collection publishing nothing is not the same as a consumer being wired to the wrong
    /// publisher, but both look identical from inside the consumer. Pinning before any cycle has run
    /// must yield the empty snapshot rather than throw or block.
    /// </summary>
    [Fact]
    public void AConsumerThatPinsBeforeTheFirstCycleSeesTheEmptySnapshot()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        Price(structure, Resource(), new BigDouble(2.0, 0));
        global::StructureSO.All.Add(structure);

        var frame = ProjectAutoBuy();

        Assert.Equal(0, frame.CandidateCount);
    }

    /// <summary>
    /// A pair the game is running reaches the snapshot twice over: as the plot's own instance, and
    /// as the queue slot it occupies.
    /// </summary>
    /// <remarks>
    /// Both readings are of the same game objects through the same collector, and the names they are
    /// read by are the shipped ones — which is what this file exists to prove and what a fixture with
    /// purpose-built types cannot. A boundary moving onto the snapshot needs both: which instance to
    /// submit into, and whether the queue has room for another.
    /// </remarks>
    [Fact]
    public void ARunningPairReachesTheSnapshotAsAnInstanceAndAsAQueueSlot()
    {
        var world = AutoHarvestTestWorlds.Harvestable(instances: 2, queued: true);
        var plot = new Guid(AutoHarvestKnownIds.FruitTreePlot);
        var action = new Guid(AutoHarvestKnownIds.FruitTreeCollect);

        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, plot, action, out var start, out var count));
        Assert.Equal(2, count);
        Assert.Equal(0, world.PlotActionInstances[start].Ordinal);
        Assert.Equal(1, world.PlotActionInstances[start].Quantity);
        Assert.False(world.PlotActionInstances[start].Empty);

        Assert.True(WorldLookup.TryFind(
            world.ActionQueues, KnownEntities.ActivePlotNodeActions.Uuid, out var queue));
        Assert.Equal(2, queue.UsedSlots);
        Assert.Equal(1, queue.EmptySlots);
        Assert.True(queue.HasEmptySlot);
        Assert.True(queue.Consistent);

        Assert.True(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, queue.QueueId, out var slotStart, out var slotCount));
        Assert.Equal(3, slotCount);

        var running = world.ActionQueueSlots[slotStart];
        Assert.Equal(plot, running.PlotNodeId);
        Assert.Equal(action, running.PlotNodeActionId);
        Assert.False(running.Empty);
        Assert.True(world.ActionQueueSlots[slotStart + 2].Empty);
    }

    // Generation 1 belongs to the publisher's empty seed, so a frame-stamped collection has to
    // start past it. Unity's counter is in the thousands by the time a save is playable, so this
    // is the shape production has rather than an allowance the test needs.
    private long _frame = 1;

    private WorldGeneration RunCollectionCycle()
    {
        var definition = AutomataWorldCollectionService.Define(
            new AutomataWorldCapturePort(new GameWorldCollector(), () => ++_frame, () => 1),
            _world);
        var worker = definition.CreateWorkerDefinition();
        var frame = new GameWorldCycleFrame();
        var state = worker.CreateState(new LifecycleGeneration(1));
        var config = Config();

        var capture = definition.Capture(
            frame,
            in config,
            new ServiceCaptureContext(
                AutomataWorldCollectionPolicies.ServiceId,
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                StrategyGeneration.Initial,
                new CaptureSequence(1),
                new CycleId(1),
                GameWorldStateDefaults.Empty,
                default));
        Assert.Equal(ServiceCaptureDisposition.Captured, capture.Disposition);

        var store = new ReusableActionStore<AutomataWorldCollectionAction>();
        store.BeginWrite();
        worker.Evaluate(
            frame,
            in config,
            default,
            ref state,
            new ServiceActionWriter<AutomataWorldCollectionAction>(store));

        // Publication is a main-thread action, so the cycle is not finished until it is dispatched.
        while (!store.IsComplete)
        {
            ref readonly var action = ref store.GetCurrent();
            var result = definition.TryExecute(
                in action,
                in config,
                new ServiceActionContext(
                    new ServiceCycleIdentity(
                        AutomataWorldCollectionPolicies.ServiceId,
                        new LifecycleGeneration(1),
                        new ConfigGeneration(1),
                        new StrategyGeneration(1),
                        new WorldGeneration(1),
                        new CycleId(1)),
                    new BatchId(1),
                    new ActionId(1),
                    0,
                    default));
            Assert.True(result.IsValid);
            store.AdvanceCurrentAndClear();
        }

        return state.LastPublished;
    }

    /// <summary>Pins the published world as the runtime does, then projects it as the worker does.</summary>
    private AutoBuyCycleFrame ProjectAutoBuy()
    {
        var frame = default(AutoBuyCycleFrame);
        var config = Config();
        AutoBuyFrameProjector.Project(ref frame, in config, _world.ReadLatest().Snapshot);
        return frame;
    }

    /// <summary>
    /// Describes what the collection pass prices this structure at. A candidate with no published
    /// price is not captured at all, so every fixture that expects to see a candidate has to name a
    /// cost and a resolvable scaling modifier — the same two things the game authors.
    /// </summary>
    private static void Price(
        global::StructureSO structure,
        global::ResourceSO resource,
        BigDouble amount)
    {
        structure.baseCost.costs.Add(new global::ResourceTuple(resource, amount));
        var scaling = new global::ValueModifierVariable
        {
            value = new global::ValueModifier(
                global::ValueModifier.ValueModifierType.Raw, BigDouble.Zero),
        };
        global::ValueModifierVariable.All.Add(scaling);
        structure.costPerQuantity = new global::ValueModifierRef { variable = scaling };
    }

    private static global::ResourceSO Resource()
    {
        var resource = new global::ResourceSO { uuid = Guid.NewGuid().ToString() };
        global::ResourceSO.All.Add(resource);
        return resource;
    }

    /// <summary>
    /// Collection reads every registry, so a leftover entity from any other test is a candidate here.
    /// Reset on the way in as well as out — a class that only cleaned up after itself would still
    /// inherit whatever ran before it.
    /// </summary>
    private static void ResetRegistries()
    {
        global::ResourceSO.All.Clear();
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::GameManager.currentFrame = 0;
        global::ValueModifierVariable.All.Clear();
    }

    private static SuiteRuntimeConfiguration Config() => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoBuy = new AutoBuyConfiguration
        {
            Mode = AutoBuyOperationMode.Active,
            IncludeStructures = true,
            IncludeUpgrades = false,
        },
    };
}

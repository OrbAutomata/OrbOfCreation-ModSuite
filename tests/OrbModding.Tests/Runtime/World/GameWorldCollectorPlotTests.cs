using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorPlotTests : GameWorldCollectorTestBase
{

    /// <summary>
    /// A plot node's quantities are summed out of its phase instances, not asked for.
    /// </summary>
    /// <remarks>
    /// The game's own <c>GetQuantity()</c> and <c>GetTotalQuantity()</c> are closed to collection:
    /// both reach the instances through <c>GetPhaseInstance()</c>, which lazily builds a cache and
    /// creates a missing instance on the way past — a write, from a pass whose contract is that it
    /// does not write. Three idle, four growing and five resting is twelve in total and three idle,
    /// and with two claimed by a main-phase action and one by an any-phase one the any-phase claim is
    /// absorbed by the nine busy, leaving 3 - 2 - 0 = 1. The three counts are deliberately distinct:
    /// equal ones let a reader that ignores an instance's phase agree with one that does not.
    /// </remarks>
    [Fact]
    public void APlotNodesQuantitiesAreSummedFromItsPhaseInstances()
    {
        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO
        {
            Identity = node,
            actionQuantityUsageMain = new ValueModifierRecord(2d),
            actionQuantityUsageAny = new ValueModifierRecord(1d),
        }
            .With(PlotNodePhases.Idle, 3)
            .With(PlotNodePhases.Growing, 4)
            .With(PlotNodePhases.Resting, 5));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.PlotNodes, node, out var row));
        Assert.Equal(3, row.Reading.IdleQuantity);
        Assert.Equal(12, row.Reading.TotalQuantity);
        Assert.Equal(1, row.RemainingQuantity);
    }

    /// <summary>
    /// A phase instance the node does not author is not part of its total.
    /// </summary>
    /// <remarks>
    /// The game totals by walking <c>phaseInfos</c> — the authored phases — and asking for each
    /// one's instance, so an instance for a phase that is not authored contributes nothing. Summing
    /// the instance list instead would be the obvious implementation and would count it, putting the
    /// suite's total quietly out of step with every calculation the game bases on its own.
    /// </remarks>
    [Fact]
    public void AnInstanceForAPhaseTheNodeDoesNotAuthorIsNotCounted()
    {
        var node = Guid.NewGuid();
        var plot = new PlotNodeSO { Identity = node }.With(PlotNodePhases.Idle, 3);
        plot.phaseInstances.Add(new PlotNodePhaseInstance
        {
            phase = PlotNodePhases.Resting,
            timers = new TimerList { q = 5 },
        });
        PlotNodeSO.All.Add(plot);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.PlotNodes, node, out var row));
        Assert.Equal(3, row.Reading.TotalQuantity);
    }

    /// <summary>
    /// A pair exists for every action a plot offers and every action it is running, and the two are
    /// the same row when they are the same action.
    /// </summary>
    /// <remarks>
    /// The pair is what the readiness question is asked about, and it is asked of plots that offer an
    /// action they are not running and of plots running one they no longer offer. A table keyed on
    /// either side alone loses one of those two. Both sides are counted rather than flagged, so a
    /// consumer that needs the pair to be unambiguous can see that it is not.
    /// </remarks>
    [Fact]
    public void EachPlotAndActionPairIsPublishedOnce()
    {
        var offered = new PlotNodeActionSO();
        var alsoOffered = new PlotNodeActionSO();
        var retired = new PlotNodeActionSO();
        PlotNodeActionSO.All.AddRange(new[] { offered, alsoOffered, retired });

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = node }
            .With(PlotNodePhases.Idle, 7)
            .Offering(offered)
            .Offering(alsoOffered)
            .Offering(offered)
            .Running(offered)
            .Running(retired));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(3, world.PlotActions.Count);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, offered.Identity, out var both));
        Assert.Equal(2, both.Reading.OfferedCount);
        Assert.Equal(1, both.Reading.InstanceCount);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, alsoOffered.Identity, out var idle));
        Assert.Equal(1, idle.Reading.OfferedCount);
        Assert.Equal(0, idle.Reading.InstanceCount);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, retired.Identity, out var orphan));
        Assert.Equal(0, orphan.Reading.OfferedCount);
        Assert.Equal(1, orphan.Reading.InstanceCount);
    }

    /// <summary>
    /// An instance whose reference has not been resolved yet still names its action.
    /// </summary>
    /// <remarks>
    /// <c>IdObjectRef</c> holds a serialized string and a guid it memoises the parse into, and the
    /// guid is empty until something asks. Straight off a save load nothing has, so a reader that
    /// trusted the memoised field alone would see a plot with no running actions at exactly the
    /// moment a consumer most wants to know what is already running.
    /// </remarks>
    [Fact]
    public void AnInstanceIsFoundThroughItsSerializedReferenceWhenTheGuidIsNotMemoisedYet()
    {
        var action = new PlotNodeActionSO();
        PlotNodeActionSO.All.Add(action);

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = node }
            .With(PlotNodePhases.Idle, 4)
            .RunningUnresolved(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.Equal(1, row.Reading.InstanceCount);
    }

    /// <summary>
    /// What one run costs is the authored cost divided by the plot's size, and how many runs fit
    /// depends on which of the plot's two remainders the action draws on.
    /// </summary>
    /// <remarks>
    /// Seven idle and five resting is twelve; one claimed by a main-phase action leaves six idle and
    /// eleven in total. A cost of seven against a size of 200% floors to three — rounding would give
    /// four — so the idle-paying action fits twice and the any-state one three times. Every number
    /// here is chosen so that swapping either remainder, dropping the size division, or rounding
    /// instead of flooring gives a different answer.
    /// </remarks>
    [Fact]
    public void AnActionsCostScalesByPlotSizeAndItsRunsByTheRemainderItDrawsOn()
    {
        var scaled = new PlotNodeActionSO { elementCost = 7, useSizeModForCost = true };
        var anyState = new PlotNodeActionSO { elementCost = 3, useAnyStateForCost = true };
        var idlePaying = new PlotNodeActionSO { elementCost = 3 };
        PlotNodeActionSO.All.AddRange(new[] { scaled, anyState, idlePaying });

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO
        {
            Identity = node,
            sizeMod = new ValueModifierRecord(200d),
            actionQuantityUsageMain = new ValueModifierRecord(1d),
        }
            .With(PlotNodePhases.Idle, 7)
            .With(PlotNodePhases.Resting, 5)
            .Offering(scaled)
            .Offering(anyState)
            .Offering(idlePaying));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, scaled.Identity, out var sized));
        Assert.True(sized.ElementCostKnown);
        Assert.Equal(3, sized.ElementCost);
        Assert.True(sized.HasEnoughForOneInstance);
        Assert.Equal(2, sized.MaximumRemainingInstances);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, anyState.Identity, out var any));
        Assert.Equal(3, any.MaximumRemainingInstances);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, idlePaying.Identity, out var idle));
        Assert.Equal(2, idle.MaximumRemainingInstances);
    }

    /// <summary>
    /// An action that takes its size from other nodes publishes no cost at all.
    /// </summary>
    /// <remarks>
    /// The game multiplies such a cost by a product over those nodes' next-size percentages, which
    /// this suite has not ported. Publishing the unscaled cost instead would be too cheap in the one
    /// direction that matters: a consumer would start a run the plot cannot pay for.
    /// </remarks>
    [Fact]
    public void AnActionThatTakesItsSizeFromOtherNodesPublishesNoCost()
    {
        var action = new PlotNodeActionSO { elementCost = 4, useSizeModForCost = true };
        action.sizeModNodes.Add(new PlotNodeSO());
        PlotNodeActionSO.All.Add(action);

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = node, sizeMod = new ValueModifierRecord(100d) }
            .With(PlotNodePhases.Idle, 9)
            .Offering(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.False(row.ElementCostKnown);
        Assert.False(row.HasEnoughForOneInstance);
        Assert.Equal(0, row.MaximumRemainingInstances);
    }

    /// <summary>
    /// Whether the game has confirmed an action's prerequisites is read from the latch it already set.
    /// </summary>
    /// <remarks>
    /// <c>Prerequisites.Container.Check()</c> stamps a game id and, on success, sets and leaves
    /// <c>available</c>. Collection reads the field. What that proves is one-directional: a set latch
    /// is a verdict the game reached, and an unset one is either a prerequisite the game found unmet
    /// or one it has not looked at, with nothing readable telling the two apart. The reading is named
    /// for that difference, and refuses to act either way.
    /// </remarks>
    [Fact]
    public void AnActionsPrerequisitesAreReadFromTheLatchRatherThanAsked()
    {
        var confirmed = new PlotNodeActionSO();
        confirmed.prerequisites.available = true;
        var unconfirmed = new PlotNodeActionSO();
        PlotNodeActionSO.All.AddRange(new[] { confirmed, unconfirmed });

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = node }
            .With(PlotNodePhases.Idle, 2)
            .Offering(confirmed)
            .Offering(unconfirmed));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(
            WorldPlotActionLookup.TryFind(world.PlotActions, node, confirmed.Identity, out var allowed));
        Assert.True(allowed.Reading.PrerequisitesConfirmed);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.NativeLatchedTrue,
            allowed.Reading.PrerequisiteEvidence);

        Assert.True(
            WorldPlotActionLookup.TryFind(world.PlotActions, node, unconfirmed.Identity, out var blocked));
        Assert.False(blocked.Reading.PrerequisitesConfirmed);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
            blocked.Reading.PrerequisiteEvidence);
    }

    /// <summary>
    /// A plot with nothing left to give still publishes its pairs, with nothing available on them.
    /// </summary>
    /// <remarks>
    /// The game's own remainder is allowed to go negative — its two usage terms are subtracted
    /// asymmetrically — and dividing a negative remainder by a cost is where a faithful port turns
    /// into a consumer being told it may run an action a negative number of times.
    /// </remarks>
    [Fact]
    public void AnOverclaimedPlotOffersNoRunsRatherThanNegativeOnes()
    {
        var action = new PlotNodeActionSO { elementCost = 2 };
        PlotNodeActionSO.All.Add(action);

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO
        {
            Identity = node,
            actionQuantityUsageMain = new ValueModifierRecord(5d),
        }
            .With(PlotNodePhases.Idle, 2)
            .Offering(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.Equal(2, row.ElementCost);
        Assert.False(row.HasEnoughForOneInstance);
        Assert.Equal(0, row.MaximumRemainingInstances);
    }

    /// <summary>
    /// Each of a plot's action instances is a row of its own, so "which one" is answerable.
    /// </summary>
    /// <remarks>
    /// The pair's count says a plot is running an action; it cannot say which of two instances, how
    /// much either is running, or whether either is under way. That is the question the action
    /// boundary walks the plot's list to answer on every submission, and these rows are that walk
    /// taken once. An instance whose reference resolves to nothing keeps a row keyed on no action:
    /// the plot really is holding it, and a table that dropped it would report the plot as holding
    /// one fewer.
    /// </remarks>
    [Fact]
    public void EachOfAPlotsActionInstancesIsARowOfItsOwn()
    {
        var collecting = new PlotNodeActionSO();
        var retired = new PlotNodeActionSO();
        PlotNodeActionSO.All.AddRange(new[] { collecting, retired });

        var node = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = node }
            .With(PlotNodePhases.Idle, 9)
            .Offering(collecting)
            .Running(collecting, quantity: 3, engaged: true)
            .Running(collecting, quantity: 0)
            .Running(retired, quantity: 2)
            .RunningSomethingUnknown());

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, collecting.Identity, out var pair));
        Assert.Equal(2, pair.Reading.InstanceCount);

        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, collecting.Identity, out var start, out var count));
        Assert.Equal(2, count);

        var running = world.PlotActionInstances[start];
        Assert.Equal(0, running.Ordinal);
        Assert.Equal(3, running.Quantity);
        Assert.True(running.Engaged);
        Assert.False(running.Empty);
        Assert.True(running.ReferenceResolved);

        var idle = world.PlotActionInstances[start + 1];
        Assert.Equal(1, idle.Ordinal);
        Assert.Equal(0, idle.Quantity);
        Assert.False(idle.Engaged);
        Assert.True(idle.Empty);

        // An instance of an action the plot no longer offers is still the plot's instance.
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, retired.Identity, out var orphanStart, out var orphanCount));
        Assert.Equal(1, orphanCount);
        Assert.Equal(2, world.PlotActionInstances[orphanStart].Ordinal);

        // The unresolvable one is keyed on no action, which is the only honest key for it.
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, Guid.Empty, out var unknownStart, out var unknownCount));
        Assert.Equal(1, unknownCount);
        Assert.False(world.PlotActionInstances[unknownStart].ReferenceResolved);
        Assert.Equal(3, world.PlotActionInstances[unknownStart].Ordinal);

        // And it is not a pair: a pair with no action on one side is not one.
        Assert.False(WorldPlotActionLookup.TryFind(world.PlotActions, node, Guid.Empty, out _));
    }

    /// <summary>
    /// A plot action publishes the drain its diamond's hover prints: every authored entry, in the
    /// authored order, scaled by the plot's own drain modifier.
    /// </summary>
    /// <remarks>
    /// The modifier is the plot's, so the drain is keyed by the pair rather than by the action, and
    /// it is read once per pair however many instances the plot holds.
    /// </remarks>
    [Fact]
    public void APlotActionPublishesTheDrainItsDiamondPrints()
    {
        var space = new ResourceSO();
        var scroll = new ResourceSO();
        var create = new PlotNodeActionSO();
        create.actionDrain.costs.Add(new ResourceTuple { resource = space, Amount = new BigDouble(40) });
        create.actionDrain.costs.Add(new ResourceTuple { resource = scroll, Amount = new BigDouble(0.5) });
        var destroy = new PlotNodeActionSO();
        PlotNodeActionSO.All.AddRange(new[] { create, destroy });

        var node = Guid.NewGuid();
        var plot = new PlotNodeSO { Identity = node }
            .With(PlotNodePhases.Idle, 1)
            .Offering(create)
            .Offering(destroy)
            .Running(create, quantity: 0)
            .Running(create, quantity: 2)
            .Running(destroy, quantity: 0);
        plot.GetActionInstances()[0].DrainCostMod = new BigDouble(150);
        PlotNodeSO.All.Add(plot);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        var drains = WorldPlotActionDrainLookup.For(world.PlotActionDrains, node, create.Identity).ToArray();
        Assert.Equal(2, drains.Length);
        Assert.Equal(space.Identity, drains[0].ResourceId);
        Assert.Equal(new BigDouble(60), drains[0].Amount);
        Assert.Equal(scroll.Identity, drains[1].ResourceId);
        Assert.Equal(new BigDouble(0.75), drains[1].Amount);
        Assert.Equal(0, WorldPlotActionDrainLookup.For(world.PlotActionDrains, node, destroy.Identity).Length);
        Assert.Equal(2, world.PlotActionDrains.Count);
    }

    /// <summary>
    /// One plot's instances stay that plot's, and a second pass publishes each of them once.
    /// </summary>
    /// <remarks>
    /// The instance buffer is reused like every other, and it has no identity check behind it to
    /// catch a missed reset — a plot's instances would simply appear twice, which reads exactly like
    /// a plot running the action twice.
    /// </remarks>
    [Fact]
    public void CollectingTwicePublishesEachInstanceOnce()
    {
        var action = new PlotNodeActionSO();
        PlotNodeActionSO.All.Add(action);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = first }
            .With(PlotNodePhases.Idle, 4)
            .Offering(action)
            .Running(action, quantity: 1));
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = second }
            .With(PlotNodePhases.Idle, 4)
            .Offering(action)
            .Running(action, quantity: 5));

        var collector = Collector();
        collector.Collect();
        collector.Collect();
        var world = collector.Build();

        Assert.Equal(2, world.PlotActionInstances.Count);
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, second, action.Identity, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(5, world.PlotActionInstances[start].Quantity);
    }

    /// <summary>
    /// A plot whose authoring read throws anything but an expected native failure faults the pass
    /// instead of costing one plot.
    /// </summary>
    [Fact]
    public void APlotWhoseAuthoringThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("autoAction was destroyed");
        PlotNodeSO.All.Add(new PlotNodeSO
        {
            Identity = Guid.NewGuid(),
            autoAction = new PlotNodeActionSO { GetGuidFailure = failure },
        }.With(PlotNodePhases.Idle, 4));

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A plot whose actions read throws anything but an expected native failure faults the pass
    /// instead of costing one plot.
    /// </summary>
    [Fact]
    public void APlotWhoseActionsThrowUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("offered action was destroyed");
        PlotNodeSO.All.Add(new PlotNodeSO { Identity = Guid.NewGuid() }
            .With(PlotNodePhases.Idle, 4)
            .Offering(new PlotNodeActionSO { GetGuidFailure = failure }));

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// An action whose effect blocks throw anything but an expected native failure faults the pass
    /// instead of costing one action.
    /// </summary>
    [Fact]
    public void AnActionWhoseEffectsThrowUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("scaling weight was destroyed");
        var block = new InstantEffectBlock();
        block.effectMods.Add(new ScalingWeightEffectMod
        {
            scalingWeightRef = { scalingWeight = new ScalingWeightSO { GetGuidFailure = failure } },
        });
        PlotNodeActionSO.All.Add(new PlotNodeActionSO { completeEffects = { block } });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A queue whose walk throws part-way publishes none of its slots: the slots already read would
    /// otherwise stand without their queue and undercount what is running.
    /// </summary>
    [Fact]
    public void APlotActionQueueThatThrowsMidWalkLeavesNoSlotsBehind()
    {
        using var faults = new OrbModding.TestSupport.CapturedGameActionFaultLog();
        var failure = new InvalidOperationException("IsEngaged failed");
        var collecting = new PlotNodeActionSO();
        PlotNodeActionSO.All.Add(collecting);
        var orchard = new PlotNodeSO { Identity = Guid.NewGuid() }.With(PlotNodePhases.Idle, 4);
        PlotNodeSO.All.Add(orchard);

        var queue = new PlotNodeActionInstanceListVariable();
        queue.value.Add(new PlotNodeActionInstance { quantity = 3, plotNodeRefObj = orchard, Action = collecting });
        queue.value.Add(new PlotNodeActionInstance { quantity = 1, plotNodeRefObj = orchard, Action = collecting, throwsOnEngaged = true, engagedFailure = failure });
        IdScriptableObject.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, report.For("action queues").Skipped);
        var queues = report.For("action queues");
        Assert.Equal("reading the plot-action queue threw", queues.Failure.Reason.Sentence);
        faults.AssertLogged(queues.Failure.Reason, failure);
        Assert.Contains("Reference " + queues.Failure.Reason.Reference, queues.FirstFailure);
        Assert.False(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out _));
        Assert.Equal(0, world.ActionQueueSlots.Count);
    }

    /// <summary>
    /// A plot-action queue walk that throws anything but an expected native failure faults the pass
    /// instead of costing the queue.
    /// </summary>
    [Fact]
    public void APlotActionQueueWhoseWalkThrowsUnexpectedlyFaultsThePass()
    {
        var collecting = new PlotNodeActionSO();
        PlotNodeActionSO.All.Add(collecting);
        var orchard = new PlotNodeSO { Identity = Guid.NewGuid() }.With(PlotNodePhases.Idle, 4);
        PlotNodeSO.All.Add(orchard);

        var failure = new NullReferenceException("slot was destroyed");
        var queue = new PlotNodeActionInstanceListVariable();
        queue.value.Add(new PlotNodeActionInstance { quantity = 3, plotNodeRefObj = orchard, Action = collecting });
        queue.value.Add(new PlotNodeActionInstance
        {
            quantity = 1,
            plotNodeRefObj = orchard,
            Action = collecting,
            throwsOnEngaged = true,
            engagedFailure = failure,
        });
        IdScriptableObject.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// The plot-action queue publishes its occupancy and what sits in each of its slots.
    /// </summary>
    /// <remarks>
    /// This is the reading Auto Harvest takes one slot at a time at its action boundary, taken once
    /// for every consumer instead. The empty slot still gets a row: "position two is free" and
    /// "position two was not read" are different facts, and a table that only held occupants could
    /// not tell them apart.
    /// </remarks>
    [Fact]
    public void ThePlotActionQueueCarriesItsOccupancyAndWhatSitsInEachSlot()
    {
        var collecting = new PlotNodeActionSO();
        var growing = new PlotNodeActionSO();
        PlotNodeActionSO.All.AddRange(new[] { collecting, growing });

        var orchard = new PlotNodeSO { Identity = Guid.NewGuid() }.With(PlotNodePhases.Idle, 4);
        var hoard = new PlotNodeSO { Identity = Guid.NewGuid() }.With(PlotNodePhases.Idle, 4);
        PlotNodeSO.All.AddRange(new[] { orchard, hoard });

        var queue = new PlotNodeActionInstanceListVariable();
        queue.value.Add(new PlotNodeActionInstance { quantity = 3, Engaged = true, plotNodeRefObj = orchard, Action = collecting });
        queue.value.Add(new PlotNodeActionInstance());
        queue.value.Add(new PlotNodeActionInstance { quantity = 1, plotNodeRefObj = hoard, Action = growing });
        IdScriptableObject.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(3, row.SlotCount);
        Assert.Equal(2, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.True(row.HasEmptySlot);
        Assert.True(row.Consistent);

        // Its capacity is the length of its own list, so there is no variable to point at.
        Assert.Equal(Guid.Empty, row.MaxQueuedItemsId);

        Assert.True(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, queue.Identity, out var start, out var count));
        Assert.Equal(3, count);

        var running = world.ActionQueueSlots[start];
        Assert.Equal(0, running.Index);
        Assert.False(running.Empty);
        Assert.Equal(orchard.Identity, running.PlotNodeId);
        Assert.Equal(collecting.Identity, running.PlotNodeActionId);
        Assert.Equal(3, running.Quantity);
        Assert.True(running.Engaged);

        var free = world.ActionQueueSlots[start + 1];
        Assert.True(free.Empty);
        Assert.Equal(Guid.Empty, free.PlotNodeId);
        Assert.Equal(0, free.Quantity);
        Assert.False(free.Engaged);

        var queued = world.ActionQueueSlots[start + 2];
        Assert.Equal(2, queued.Index);
        Assert.Equal(hoard.Identity, queued.PlotNodeId);
        Assert.Equal(growing.Identity, queued.PlotNodeActionId);
        Assert.False(queued.Engaged);

        Assert.False(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, Guid.NewGuid(), out _, out _));
    }
}

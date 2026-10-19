using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;

namespace OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;

/// <summary>
/// Worlds holding Auto Harvest's two supported pairs, collected the way production collects.
/// </summary>
/// <remarks>
/// The frame is projected from the shared snapshot on the worker, so a test that wants Auto Harvest to
/// decide anything has to publish a world it can decide from. Hand-building the frame instead would
/// agree with whatever the test expected and prove nothing about the projection.
/// </remarks>
internal static class AutoHarvestTestWorlds
{
    /// <summary>
    /// A collected world in which both supported pairs are present and harvestable.
    /// </summary>
    /// <param name="supported">
    /// Whether the pairs carry the uuids Auto Harvest looks up. A world built with other ones is
    /// still fully collected, which is what makes it a different case from an empty one.
    /// </param>
    /// <param name="treasureVisible">Whether the treasure plot is visible.</param>
    /// <param name="instances">
    /// How many runtime instances of the action each plot holds. More than one is what makes a pair
    /// ambiguous to submit into, which is a decision the policy makes rather than a shape the
    /// collector refuses.
    /// </param>
    /// <param name="queued">
    /// Whether each pair also occupies a slot of the game's action queue — "this pair is already
    /// running", expressed where the snapshot can see it.
    /// </param>
    /// <param name="author">
    /// Re-authors the fruit pair's content after it is built as the game ships it. This is how a test
    /// asks what one wrong authored value does to the safety audit, with the treasure pair left as
    /// the control that says the damage was local.
    /// </param>
    internal static GameWorldState Harvestable(
        bool supported = true,
        bool treasureVisible = true,
        int instances = 1,
        bool queued = false,
        Action<PlotNodeSO, PlotNodeActionSO>? author = null)
    {
        using var game = AutoHarvestTestGame.Load(supported, treasureVisible, instances, queued, author);
        return TestWorlds.FromLoadedRegistries();
    }

    /// <summary>
    /// Both pairs loaded into the game's registries until disposed, so the world collector and the
    /// native action boundary read and mutate the same objects.
    /// </summary>
    internal sealed class AutoHarvestTestGame : IDisposable
    {
        private readonly List<PlotNodeSO> _plots;
        private readonly List<PlotNodeActionSO> _actions;
        private readonly List<IdScriptableObject> _identities;

        private AutoHarvestTestGame(
            List<PlotNodeSO> plots,
            List<PlotNodeActionSO> actions,
            List<IdScriptableObject> identities,
            PlotNodeActionInstanceListVariable queue)
        {
            _plots = plots;
            _actions = actions;
            _identities = identities;
            Queue = queue;
        }

        internal PlotNodeActionInstanceListVariable Queue { get; }

        internal PlotNodeSO Plot(AutoHarvestPair pair) => _plots[Index(pair)];

        internal PlotNodeActionSO Action(AutoHarvestPair pair) => _actions[Index(pair)];

        /// <summary>Whether the game's action queue runs this pair now.</summary>
        internal bool IsRunning(AutoHarvestPair pair)
        {
            foreach (var instance in Queue.value)
            {
                if (instance is not null && !instance.IsEmpty() && instance.IsEngaged() &&
                    ReferenceEquals(instance.GetAction(), Action(pair)))
                    return true;
            }
            return false;
        }

        internal static AutoHarvestTestGame Load(
            bool supported = true,
            bool treasureVisible = true,
            int instances = 1,
            bool queued = false,
            Action<PlotNodeSO, PlotNodeActionSO>? author = null)
        {
            var plots = new List<PlotNodeSO>();
            var actions = new List<PlotNodeActionSO>();
            var identities = new List<IdScriptableObject>();
            Add(
                plots,
                actions,
                identities,
                supported ? AutoHarvestKnownIds.FruitTreePlot : Guid.NewGuid().ToString(),
                supported ? AutoHarvestKnownIds.FruitTreeCollect : Guid.NewGuid().ToString(),
                visible: true,
                instances,
                AutoHarvestPairAuthoring.For(AutoHarvestPair.FruitTree),
                author);
            Add(
                plots,
                actions,
                identities,
                supported ? AutoHarvestKnownIds.TreasureTreePlot : Guid.NewGuid().ToString(),
                supported ? AutoHarvestKnownIds.TreasureTreeCollect : Guid.NewGuid().ToString(),
                treasureVisible,
                instances,
                AutoHarvestPairAuthoring.For(AutoHarvestPair.TreasureTree),
                author: null);

            var queue = Queue(plots, actions, queued);
            identities.Add(queue);
            PlotNodeSO.All.AddRange(plots);
            PlotNodeActionSO.All.AddRange(actions);
            foreach (var identity in identities)
                IdScriptableObject.RuntimeLookup[identity.GetGuid()] = identity;
            return new AutoHarvestTestGame(plots, actions, identities, queue);
        }

        public void Dispose()
        {
            foreach (var identity in _identities) IdScriptableObject.RuntimeLookup.Remove(identity.GetGuid());
            foreach (var plot in _plots) PlotNodeSO.All.Remove(plot);
            foreach (var action in _actions) PlotNodeActionSO.All.Remove(action);
        }

        private static int Index(AutoHarvestPair pair) => pair == AutoHarvestPair.FruitTree ? 0 : 1;
    }

    /// <summary>
    /// One pair, authored the way the game ships it: a plot that runs nothing by itself, the three
    /// phases of its cycle, and an action that costs one of the plot and pays one treasure out of
    /// this pair's own pool.
    /// </summary>
    private static void Add(
        List<PlotNodeSO> plots,
        List<PlotNodeActionSO> actions,
        List<IdScriptableObject> identities,
        string plotUuid,
        string actionUuid,
        bool visible,
        int instances,
        AutoHarvestPairAuthoring expected,
        Action<PlotNodeSO, PlotNodeActionSO>? author)
    {
        var action = new PlotNodeActionSO
        {
            elementCost = 1,
            baseTime = expected.ActionSeconds,
            elementCostType = PlotNodeActionSO.CostType.OnExitPhase,
            elementCostExitPhase = PlotNodePhases.Resting,
        };
        action.SetGuid(new Guid(actionUuid));
        action.prerequisites.available = true;
        action.completeEffects.Add(Completion(expected.RewardPoolId, identities));
        var plot = new PlotNodeSO { visible = visible };
        plot.SetGuid(new Guid(plotUuid));
        plot.phaseInfos = PhaseCycle(expected);
        plot.phaseInstances.Add(new PlotNodePhaseInstance(PlotNodePhases.Idle, 4));
        plot.availableActions.Add(action);
        for (var index = 0; index < instances; index++)
        {
            plot.GetActionInstances().Add(new PlotNodeActionInstance(plot, action) { quantity = 1 });
        }

        author?.Invoke(plot, action);
        plots.Add(plot);
        actions.Add(action);
        identities.Add(plot);
        identities.Add(action);
    }

    /// <summary>
    /// The plot's authored cycle: idle until something starts it, growing for its growth time, then
    /// resting once before it grows again.
    /// </summary>
    private static List<PlotNodePhaseInfo> PhaseCycle(AutoHarvestPairAuthoring expected) => new()
    {
        new PlotNodePhaseInfo
        {
            phase = PlotNodePhases.Idle,
            phaseTime = 0.0,
            processType = TimerList.TimerType.Parallel,
            exitPhase = PlotNodePhases.Idle,
        },
        new PlotNodePhaseInfo
        {
            phase = PlotNodePhases.Growing,
            phaseTime = expected.GrowthSeconds,
            processType = TimerList.TimerType.Parallel,
            exitPhase = PlotNodePhases.Idle,
        },
        new PlotNodePhaseInfo
        {
            phase = PlotNodePhases.Resting,
            phaseTime = expected.RestSeconds,
            processType = TimerList.TimerType.Single,
            exitPhase = PlotNodePhases.Growing,
        },
    };

    /// <summary>
    /// What one completed run applies: one block, one modifier scaling by the completion weight, and
    /// one script paying one treasure out of the pair's pool.
    /// </summary>
    private static InstantEffectBlock Completion(Guid rewardPoolId, List<IdScriptableObject> identities)
    {
        var pool = new TreasurePoolSO();
        pool.SetGuid(rewardPoolId);
        var weight = new ScalingWeightSO();
        weight.SetGuid(KnownEntities.CompletionScalingWeight.Uuid);
        identities.Add(pool);
        identities.Add(weight);
        var block = new InstantEffectBlock();
        block.effectMods.Add(new ScalingWeightEffectMod
        {
            scalingWeightRef = { scalingWeight = weight },
        });
        block.effectScripts.Add(new TreasurePoolSO.TreasurePoolInstantEffect
        {
            treasurePool = pool,
            effectType = "EarnTreasure",
            effectValue = 1.0,
            filterScaling = { listType = FilterEffectMod.FilterType.WhiteList },
        });
        return block;
    }

    /// <summary>
    /// The game's plot-action queue, which every world has whether or not anything is running in it.
    /// It keeps one free slot, so a world that says nothing about the queue is one with room.
    /// </summary>
    private static PlotNodeActionInstanceListVariable Queue(
        List<PlotNodeSO> plots,
        List<PlotNodeActionSO> actions,
        bool queued)
    {
        var queue = new PlotNodeActionInstanceListVariable();
        queue.SetGuid(KnownEntities.ActivePlotNodeActions.Uuid);
        if (queued)
        {
            for (var index = 0; index < plots.Count; index++)
            {
                queue.value.Add(new PlotNodeActionInstance(actions[index])
                {
                    quantity = 1,
                    plotNodeRefObj = plots[index],
                });
            }
        }

        queue.value.Add(new PlotNodeActionInstance());
        return queue;
    }
}

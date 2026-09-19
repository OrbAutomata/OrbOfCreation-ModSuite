using System;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// What the game's authored content must say about one harvest pair for Auto Harvest to act on it.
/// </summary>
/// <remarks>
/// The two supported pairs are content this suite was audited against: a plot that runs nothing by
/// itself, a three-phase cycle with these durations, and an action that costs one of the plot and
/// pays out of this pool. Naming the expectation in one place is what lets the audit be a comparison
/// rather than a set of magic numbers spread through it.
/// </remarks>
internal readonly struct AutoHarvestPairAuthoring
{
    private static readonly AutoHarvestPairAuthoring Fruit = new(
        new Guid(AutoHarvestKnownIds.FruitTreePlot),
        new Guid(AutoHarvestKnownIds.FruitTreeCollect),
        new Guid(AutoHarvestKnownIds.FruitTreeRewardPool),
        growthSeconds: 480.0,
        restSeconds: 340.0,
        actionSeconds: 3.0);

    private static readonly AutoHarvestPairAuthoring Treasure = new(
        new Guid(AutoHarvestKnownIds.TreasureTreePlot),
        new Guid(AutoHarvestKnownIds.TreasureTreeCollect),
        new Guid(AutoHarvestKnownIds.TreasureTreeRewardPool),
        growthSeconds: 720.0,
        restSeconds: 360.0,
        actionSeconds: 10.0);

    internal AutoHarvestPairAuthoring(
        Guid plotId,
        Guid actionId,
        Guid rewardPoolId,
        double growthSeconds,
        double restSeconds,
        double actionSeconds)
    {
        PlotId = plotId;
        ActionId = actionId;
        RewardPoolId = rewardPoolId;
        GrowthSeconds = growthSeconds;
        RestSeconds = restSeconds;
        ActionSeconds = actionSeconds;
    }

    internal Guid PlotId { get; }

    internal Guid ActionId { get; }

    /// <summary>The pool one completed run draws its reward from.</summary>
    internal Guid RewardPoolId { get; }

    internal double GrowthSeconds { get; }

    internal double RestSeconds { get; }

    internal double ActionSeconds { get; }

    internal static AutoHarvestPairAuthoring For(AutoHarvestPair pair) => pair switch
    {
        AutoHarvestPair.FruitTree => Fruit,
        AutoHarvestPair.TreasureTree => Treasure,
        _ => throw new ArgumentOutOfRangeException(nameof(pair)),
    };
}

/// <summary>
/// Whether running one harvest action leaves the game's own phase cycle as its author wrote it.
/// </summary>
/// <remarks>
/// <para>
/// This is a whole-graph structural audit of authored content: the plot's phases, the action's cost
/// and its completion effects. It used to run against the live objects at binding time, once per
/// lifecycle epoch, and produced a verdict cached on the binding. The facts it rests on are now
/// collected like every other fact about the world, and the verdict drawn from them is this service's
/// policy — computed on the worker, from the same snapshot the plan was made against, and carried to
/// the boundary with the action. See W40 and W43.
/// </para>
/// <para>
/// Two comparisons are weaker than they were. The audit used to require that the modifier's scaling
/// weight and the script's pool were the very objects the registry resolved; a published row carries
/// identities rather than objects, so both are now uuid comparisons. An object replaced by another
/// carrying the same uuid compares equal here where it did not before — which is what the lifecycle
/// epoch exists to catch, and why R3 made that trade only while the lifecycle hooks stay.
/// </para>
/// <para>
/// Nothing here reflects, and nothing here is retained: the answer is a function of a snapshot and a
/// pair, and a snapshot that does not describe the pair yields <see cref="AutoHarvestActionSafetyState.Unknown"/>
/// rather than a guess. Unknown is a rejection at the boundary, so a degraded collection cannot
/// produce a submission.
/// </para>
/// </remarks>
internal static class AutoHarvestActionSafety
{
    // The world binder hands these axes over as their raw ordinals, so the ordinals live here.
    // Nothing selects the enum members behind them, which is why they are declared as the mirrored
    // contracts auto-harvest.action-cost-type.exit-phase, auto-harvest.phase.*, timer-type.*, and
    // auto-harvest.filter-type.white-list. A build that drops or renames one fails the manifest
    // audit, and one that renumbers a member fails HarvestSafetyEnumContractTests — because every
    // comparison here is fail-closed, the symptom would otherwise be Auto Harvest quietly
    // declining to act rather than anything that looks like a defect.
    private const int CostTypeExitPhase = 1;
    private const int PlotPhaseIdle = 0;
    private const int PlotPhaseGrowing = 1;
    private const int PlotPhaseResting = 2;
    private const int TimerTypeSingle = 0;
    private const int TimerTypeParallel = 1;
    private const int FilterTypeWhiteList = 1;
    private const int ExpectedPhaseCount = 3;
    private const int ExpectedElementCost = 1;
    private const double ExpectedEffectValue = 1.0;
    private const string CompletionBlockTypeName = "InstantEffectBlock";
    private const string CompletionModTypeName = "ScalingWeightEffectMod";
    private const string CompletionScriptTypeName = "TreasurePoolInstantEffect";
    private const string CompletionEffectTypeName = "EarnTreasure";

    private static readonly Guid CompletionScalingWeightId =
        new(AutoHarvestKnownIds.CompletionScalingWeight);

    internal static AutoHarvestActionSafetyState For(
        GameWorldState world,
        in AutoHarvestPairAuthoring expected)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        if (!WorldPlotAuthoringLookup.TryFind(world.PlotAuthoring, expected.PlotId, out var plot) ||
            !WorldLookup.TryFind(world.PlotNodeActions, expected.ActionId, out var action))
        {
            return AutoHarvestActionSafetyState.Unknown;
        }

        // A plot that drives an action of its own is running a cycle this suite did not plan, and a
        // phase table that is not the authored three is a cycle it cannot reason about at all.
        if (plot.AutoActionId != Guid.Empty ||
            plot.PhaseCount != ExpectedPhaseCount ||
            !HasAuthoredPhaseCycle(world, plot.PlotNodeId, in expected))
        {
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        }

        if (action.IsGrowingAction ||
            action.ElementCostType != CostTypeExitPhase ||
            action.ElementCostExitPhase != PlotPhaseResting ||
            action.ElementCost != ExpectedElementCost ||
            action.UseSizeModForCost ||
            action.UseAnyStateForCost ||
            action.ParallelAction ||
            action.UseSpaceUsageForTimeMult ||
            action.IgnoreNodeYield)
        {
            return AutoHarvestActionSafetyState.Destructive;
        }

        if (!AutoHarvestContractValues.IsFiniteNear(action.BaseTime, expected.ActionSeconds) ||
            action.PrerequisiteCount != 0)
        {
            return AutoHarvestActionSafetyState.UnsafeCompletionEffects;
        }

        // Ordered as the live audit ordered it: a drained resource is named as such even when the
        // action also carries a standing effect, because the drain is the more specific fact.
        if (action.ResourceCostCount != 0) return AutoHarvestActionSafetyState.ResourceDrain;

        return action.PersistentEffectCount == 0 &&
            action.CompletionEffectCount == 1 &&
            HasAuthoredCompletion(world, in expected)
            ? AutoHarvestActionSafetyState.NativePhaseCyclePreserving
            : AutoHarvestActionSafetyState.UnsafeCompletionEffects;
    }

    /// <summary>
    /// Whether the plot authors exactly the idle, growing and resting phases the pair was audited
    /// against, each once.
    /// </summary>
    private static bool HasAuthoredPhaseCycle(
        GameWorldState world,
        Guid plotId,
        in AutoHarvestPairAuthoring expected)
    {
        if (!WorldPlotPhaseDescriptorLookup.TryFindRange(
                world.PlotPhaseDescriptors, plotId, out var start, out var count) ||
            count != ExpectedPhaseCount)
        {
            return false;
        }

        var rows = world.PlotPhaseDescriptors.AsSpan();
        var seen = 0;
        for (var index = start; index < start + count; index++)
        {
            var phase = rows[index];
            var valid = phase.Phase switch
            {
                PlotPhaseIdle => AutoHarvestContractValues.IsFiniteNear(phase.PhaseTimeSeconds, 0.0) &&
                    phase.ProcessType == TimerTypeParallel && phase.ExitPhase == PlotPhaseIdle,
                PlotPhaseGrowing =>
                    AutoHarvestContractValues.IsFiniteNear(
                        phase.PhaseTimeSeconds, expected.GrowthSeconds) &&
                    phase.ProcessType == TimerTypeParallel && phase.ExitPhase == PlotPhaseIdle,
                PlotPhaseResting =>
                    AutoHarvestContractValues.IsFiniteNear(
                        phase.PhaseTimeSeconds, expected.RestSeconds) &&
                    phase.ProcessType == TimerTypeSingle && phase.ExitPhase == PlotPhaseGrowing,
                _ => false,
            };
            if (!valid || (seen & (1 << phase.Phase)) != 0) return false;
            seen |= 1 << phase.Phase;
        }

        return seen == 0b111;
    }

    /// <summary>
    /// Whether completing one run applies exactly one block, and that block does exactly one thing:
    /// draw one reward from this pair's own pool, scaled by the weight the suite knows.
    /// </summary>
    private static bool HasAuthoredCompletion(
        GameWorldState world,
        in AutoHarvestPairAuthoring expected)
    {
        if (!WorldEffectBlockLookup.TryFindRange(
                world.EffectBlocks, expected.ActionId, out var start, out var count) ||
            count != 1)
        {
            return false;
        }

        var block = world.EffectBlocks.AsSpan()[start];
        return string.Equals(block.BlockTypeName, CompletionBlockTypeName, StringComparison.Ordinal) &&
            block.PrerequisiteCount == 0 &&
            block.ModCount == 1 &&
            block.ScriptCount == 1 &&
            string.Equals(block.ModTypeName, CompletionModTypeName, StringComparison.Ordinal) &&
            string.Equals(block.ScriptTypeName, CompletionScriptTypeName, StringComparison.Ordinal) &&
            block.ScalingWeightId == CompletionScalingWeightId &&
            block.TreasurePoolId == expected.RewardPoolId &&
            string.Equals(block.EffectTypeName, CompletionEffectTypeName, StringComparison.Ordinal) &&
            AutoHarvestContractValues.IsFiniteNear(block.EffectValue, ExpectedEffectValue) &&
            block.FilterListType == FilterTypeWhiteList &&
            block.FilterContentCount == 0;
    }
}

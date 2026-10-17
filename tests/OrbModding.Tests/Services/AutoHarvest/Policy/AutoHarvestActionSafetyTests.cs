using System;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using OrbModding.Tests.Services.AutoHarvest.Runtime.ServiceCycle;
using Xunit;

namespace OrbModding.Tests.Services.AutoHarvest.Policy;

/// <summary>
/// The structural safety audit, read out of a snapshot the real collector built.
/// </summary>
/// <remarks>
/// <para>
/// Every case here authors the game's own content and then breaks exactly one thing about it, so
/// each term of the audit is proved to be load-bearing rather than merely present. The audit used to
/// read those terms off the live objects; running the cases through the collector is what says the
/// published facts carry the same weight the live reads did.
/// </para>
/// <para>
/// Only the fruit pair is ever re-authored. The treasure pair is the control: a verdict that changed
/// for both would mean the audit was answering about the wrong pair.
/// </para>
/// </remarks>
public sealed class AutoHarvestActionSafetyTests : IDisposable
{
    public AutoHarvestActionSafetyTests() => Clear();

    public void Dispose() => Clear();

    private static void Clear()
    {
        PlotNodeSO.All.Clear();
        PlotNodeActionSO.All.Clear();
    }

    [Fact]
    public void TheContentTheGameShipsPreservesItsOwnPhaseCycle()
    {
        var world = AutoHarvestTestWorlds.Harvestable();

        Assert.Equal(AutoHarvestActionSafetyState.NativePhaseCyclePreserving, Verdict(world, AutoHarvestPair.FruitTree));
        Assert.Equal(AutoHarvestActionSafetyState.NativePhaseCyclePreserving, Verdict(world, AutoHarvestPair.TreasureTree));
    }

    [Fact]
    public void BreakingOnePairLeavesTheOtherPairAudited()
    {
        var world = AutoHarvestTestWorlds.Harvestable(author: (_, action) => action.elementCost = 2);

        Assert.Equal(AutoHarvestActionSafetyState.Destructive, Verdict(world, AutoHarvestPair.FruitTree));
        Assert.Equal(AutoHarvestActionSafetyState.NativePhaseCyclePreserving, Verdict(world, AutoHarvestPair.TreasureTree));
    }

    /// <summary>
    /// A world that describes no plots at all says nothing about safety, and saying nothing is a
    /// rejection at the boundary rather than a pass.
    /// </summary>
    [Fact]
    public void APairTheWorldDoesNotDescribeIsUnknown()
    {
        var world = TestWorlds.FromLoadedRegistries();

        Assert.Equal(AutoHarvestActionSafetyState.Unknown, Verdict(world, AutoHarvestPair.FruitTree));
    }

    /// <summary>
    /// A plot whose authoring was collected while its action's was not is unknown, not unsafe: the
    /// audit has nothing to disagree with.
    /// </summary>
    [Fact]
    public void APlotWhoseActionWasNotCollectedIsUnknown()
    {
        var expected = AutoHarvestPairAuthoring.For(AutoHarvestPair.FruitTree);
        var world = new GameWorldState
        {
            PlotAuthoring = PublicationTable<WorldPlotAuthoring>.Create(
                new[] { new WorldPlotAuthoring(expected.PlotId, Guid.Empty, 3) }),
            PlotPhaseDescriptors = PublicationTable<WorldPlotPhaseDescriptor>.Create(
                PhaseRows(expected)),
        };

        Assert.Equal(AutoHarvestActionSafetyState.Unknown, Verdict(world, AutoHarvestPair.FruitTree));
    }

    [Fact]
    public void APlotThatRunsAnActionOfItsOwnIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, action) => plot.autoAction = action);

    [Fact]
    public void APlotAuthoringOtherThanThreePhasesIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos.RemoveAt(2));

    [Fact]
    public void APhaseThatCouldNotBeReadIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos[1] = null!);

    [Fact]
    public void TheSamePhaseAuthoredTwiceIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos[2].phase = PlotNodePhases.Growing);

    [Fact]
    public void AGrowthTimeOtherThanTheAuditedOneIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos[1].phaseTime += 1.0);

    [Fact]
    public void ARestTimeOtherThanTheAuditedOneIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos[2].phaseTime += 1.0);

    [Fact]
    public void APhaseWhoseTimersRunDifferentlyIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos[2].processType = TimerList.TimerType.Parallel);

    [Fact]
    public void APhaseThatLeavesToAnotherPhaseIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (plot, _) => plot.phaseInfos[1].exitPhase = PlotNodePhases.Resting);

    [Fact]
    public void AnActionThatGrowsThePlotIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.isGrowingAction = true);

    [Fact]
    public void AnActionChargingTheElementCostOnStartIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.elementCostType = PlotNodeActionSO.CostType.OnStart);

    [Fact]
    public void AnActionChargingOnLeavingAnotherPhaseIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.elementCostExitPhase = PlotNodePhases.Idle);

    [Fact]
    public void AnActionCostingMoreThanOneOfThePlotIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.elementCost = 2);

    [Fact]
    public void AnActionScalingItsCostBySizeIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.useSizeModForCost = true);

    [Fact]
    public void AnActionCostingTheSameInAnyStateIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.useAnyStateForCost = true);

    [Fact]
    public void AnActionThatRunsInParallelIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.parallelAction = true);

    [Fact]
    public void AnActionScalingItsTimeBySpaceUsageIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.useSpaceUsageForTimeMult = true);

    [Fact]
    public void AnActionIgnoringTheNodeYieldIsDestructive() =>
        AssertFruit(
            AutoHarvestActionSafetyState.Destructive,
            (_, action) => action.ignoreNodeYield = true);

    [Fact]
    public void AnActionTakingOtherThanTheAuditedTimeIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => action.baseTime += 1.0);

    [Fact]
    public void AnActionBehindAPrerequisiteIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => action.prerequisites.prerequisites.Add(new object()));

    [Fact]
    public void AnActionDrainingAResourceIsAResourceDrain() =>
        AssertFruit(
            AutoHarvestActionSafetyState.ResourceDrain,
            (_, action) => action.actionDrain.costs.Add(
                new ResourceTuple(Resource(Guid.NewGuid()), new BigDouble(40))));

    [Fact]
    public void AnActionApplyingAStandingEffectIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => action.actionEffects.Add(new PersistentEffectBlock()));

    [Fact]
    public void ACompletionApplyingTwoBlocksIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => action.completeEffects.Add(new InstantEffectBlock()));

    [Fact]
    public void ACompletionApplyingNothingIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => action.completeEffects.Clear());

    [Fact]
    public void ACompletionBlockBehindAPrerequisiteIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Block(action).prerequisites.prerequisites.Add(new object()));

    [Fact]
    public void ACompletionApplyingASecondModifierIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Block(action).effectMods.Add(new ScalingWeightEffectMod()));

    [Fact]
    public void ACompletionRunningNoScriptIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Block(action).effectScripts.Clear());

    [Fact]
    public void ACompletionModifierOfAnotherKindIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Block(action).effectMods[0] = new FilterEffectMod());

    [Fact]
    public void ACompletionScriptOfAnotherKindIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Block(action).effectScripts[0] = new UnexpectedInstantEffectScript());

    /// <summary>
    /// The weight comparison is by identity rather than by value: a weight that is not the one the
    /// suite audited fails however plausible its contents.
    /// </summary>
    [Fact]
    public void ACompletionScalingByAnotherWeightIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Mod(action).scalingWeightRef.scalingWeight = Weight(Guid.NewGuid()));

    [Fact]
    public void ACompletionScalingByNoWeightIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Mod(action).scalingWeightRef.scalingWeight = null);

    /// <summary>
    /// The pool is the pair's own. The treasure pair's pool is a real pool of the same shape, which
    /// is what makes it the case worth writing.
    /// </summary>
    [Fact]
    public void ACompletionPayingOutOfTheOtherPairsPoolIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Script(action).treasurePool =
                Pool(AutoHarvestPairAuthoring.For(AutoHarvestPair.TreasureTree).RewardPoolId));

    [Fact]
    public void ACompletionDoingSomethingOtherThanEarningTreasureIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Script(action).effectType = "EarnResource");

    [Fact]
    public void ACompletionEarningMoreThanOneTreasureIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Script(action).effectValue = 2.0);

    [Fact]
    public void ACompletionFilteringByABlackListIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Script(action).filterScaling.listType =
                FilterEffectMod.FilterType.BlackList);

    [Fact]
    public void ACompletionFilteringOnAnyContentAtAllIsUnsafe() =>
        AssertFruit(
            AutoHarvestActionSafetyState.UnsafeCompletionEffects,
            (_, action) => Script(action).filterScaling.listContents.Add(new object()));

    /// <summary>
    /// What the two pairs are, as the game ships them.
    /// </summary>
    /// <remarks>
    /// Spelled out rather than derived, because everything else in this file compares the world
    /// against these numbers — including the fixture that authors the world. One of the two has to
    /// state them, or a changed expectation would move both sides at once and prove nothing.
    /// </remarks>
    [Fact]
    public void TheAuditedPairsAreTheOnesTheGameShips()
    {
        var fruit = AutoHarvestPairAuthoring.For(AutoHarvestPair.FruitTree);
        var treasure = AutoHarvestPairAuthoring.For(AutoHarvestPair.TreasureTree);

        Assert.Equal(new Guid("6782dd13-e229-4385-a1aa-8ed86e6ea1ed"), fruit.PlotId);
        Assert.Equal(new Guid("60ea60a2-44e9-41c2-86d6-3935fae0b647"), fruit.ActionId);
        Assert.Equal(new Guid("b3ab80f0-80c7-41d4-b4c7-f34c3e909104"), fruit.RewardPoolId);
        Assert.Equal(480.0, fruit.GrowthSeconds);
        Assert.Equal(340.0, fruit.RestSeconds);
        Assert.Equal(3.0, fruit.ActionSeconds);

        Assert.Equal(new Guid("2d41cfc1-bffa-43b5-b3a8-5e4d5ad85434"), treasure.PlotId);
        Assert.Equal(new Guid("3eb68f6f-c2f2-405a-88d2-e5c80345aeb4"), treasure.ActionId);
        Assert.Equal(new Guid("1a370ff9-fea7-4a2a-bca7-57fdb2862356"), treasure.RewardPoolId);
        Assert.Equal(720.0, treasure.GrowthSeconds);
        Assert.Equal(360.0, treasure.RestSeconds);
        Assert.Equal(10.0, treasure.ActionSeconds);
    }

    private static void AssertFruit(
        AutoHarvestActionSafetyState expected,
        Action<PlotNodeSO, PlotNodeActionSO> author) =>
        Assert.Equal(
            expected,
            Verdict(AutoHarvestTestWorlds.Harvestable(author: author), AutoHarvestPair.FruitTree));

    private static AutoHarvestActionSafetyState Verdict(GameWorldState world, AutoHarvestPair pair) =>
        AutoHarvestActionSafety.For(world, AutoHarvestPairAuthoring.For(pair));

    private static InstantEffectBlock Block(PlotNodeActionSO action) => action.completeEffects[0];

    private static ScalingWeightEffectMod Mod(PlotNodeActionSO action) =>
        (ScalingWeightEffectMod)Block(action).effectMods[0];

    private static TreasurePoolSO.TreasurePoolInstantEffect Script(PlotNodeActionSO action) =>
        (TreasurePoolSO.TreasurePoolInstantEffect)Block(action).effectScripts[0];

    private static ScalingWeightSO Weight(Guid uuid)
    {
        var weight = new ScalingWeightSO();
        weight.SetGuid(uuid);
        return weight;
    }

    private static ResourceSO Resource(Guid uuid)
    {
        var resource = new ResourceSO();
        resource.SetGuid(uuid);
        return resource;
    }

    private static TreasurePoolSO Pool(Guid uuid)
    {
        var pool = new TreasurePoolSO();
        pool.SetGuid(uuid);
        return pool;
    }

    private static WorldPlotPhaseDescriptor[] PhaseRows(AutoHarvestPairAuthoring expected) =>
        new[]
        {
            new WorldPlotPhaseDescriptor(expected.PlotId, 0, 0, 0.0, 1, 0),
            new WorldPlotPhaseDescriptor(expected.PlotId, 1, 1, expected.GrowthSeconds, 1, 0),
            new WorldPlotPhaseDescriptor(expected.PlotId, 2, 2, expected.RestSeconds, 0, 1),
        };
}

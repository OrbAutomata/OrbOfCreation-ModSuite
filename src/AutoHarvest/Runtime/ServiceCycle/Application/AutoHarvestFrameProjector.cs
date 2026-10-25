using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Projects the pinned world snapshot into an Auto Harvest frame, on the worker thread.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here touches the game, and nothing here is this service's own memory either. The two
/// pairs are named by constants and their facts come from the snapshot; the contract circuit is the
/// action boundary's, and what it means for a decision reaches the worker as a result code it
/// records in its own state. See W45.
/// </para>
/// <para>
/// The structural safety audit is taken here too, from the same snapshot.
/// </para>
/// <para>
/// Binding resolution belongs to the action boundary: the adapter resolves the pair selected for
/// mutation and validates its live lifecycle against the admitted cycle before mutation.
/// </para>
/// <para>
/// It is a static class holding nothing, and the world reaches it as an argument. There is no
/// main-thread cost left to attribute — the runtime times the whole evaluation. See W51.
/// </para>
/// </remarks>
internal static class AutoHarvestFrameProjector
{
    /// <summary>
    /// Builds the frame from the world the runtime pinned for this cycle.
    /// </summary>
    /// <remarks>
    /// Both pairs and the evaluation that follows decide against the same reading of the game.
    /// </remarks>
    internal static AutoHarvestCycleFrame Project(
        in SuiteRuntimeConfiguration config,
        GameWorldState world)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));

        return new AutoHarvestCycleFrame(
            ProjectPair(
                AutoHarvestPair.FruitTree,
                config.AutoHarvest.CollectFruitTrees,
                world),
            ProjectPair(
                AutoHarvestPair.TreasureTree,
                config.AutoHarvest.CollectTreasureTrees,
                world));
    }

    private static AutoHarvestPairCapture ProjectPair(
        AutoHarvestPair pair,
        bool selected,
        GameWorldState world)
    {
        if (!selected) return AutoHarvestPairCapture.NotSelected(pair);

        // No readable plots can mean collection has not run or its read failed. Wait for the next
        // capture; the collector's category diagnostics own the cause. A populated world missing
        // this plot instead leaves its identity unverified for the ordinary policy to decline.
        if (world.PlotNodes.Count == 0) return AutoHarvestPairCapture.Unavailable(pair);

        var expected = AutoHarvestPairAuthoring.For(pair);
        return AutoHarvestPairCapture.Captured(
            pair,
            AutoHarvestWorldFacts.For(world, expected.PlotId, expected.ActionId),
            AutoHarvestActionSafety.For(world, in expected));
    }
}

using System;
using System.Collections;

namespace OrbAutomata;

internal sealed partial class AutoHarvestNativeStateReader
{
    public AutoHarvestSubmissionState CaptureSubmissionState(
        in ResolvedAutoHarvestPair resolved) =>
        CaptureActiveActions(resolved).Project(resolved.Target.Pair);

    public bool IsPairEngaged(in ResolvedAutoHarvestPair resolved)
    {
        var bindings = resolved.Bindings;
        var values = ReadActiveValues(bindings, resolved.Shared.ActiveActions);
        var matches = 0;
        var engaged = false;
        foreach (var instance in values)
        {
            if (instance is null || instance.GetType() != bindings.InstanceType)
                return false;
            if (bindings.InstanceIsEmpty(instance)) continue;
            var plot = bindings.InstancePlot(instance);
            var action = bindings.InstanceAction(instance);
            var observed = ClassifyPair(resolved, plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory) return false;
            if (observed == AutoHarvestObservedPair.Unrelated ||
                observed != (resolved.Target.Pair == AutoHarvestPair.FruitTree
                    ? AutoHarvestObservedPair.FruitTree
                    : AutoHarvestObservedPair.TreasureTree)) continue;
            matches++;
            engaged |= bindings.InstanceIsEngaged(instance);
        }
        return matches == 1 && engaged;
    }

    public AutoHarvestActiveActionSnapshot CaptureActiveActions(
        in ResolvedAutoHarvestPair resolved)
    {
        var bindings = resolved.Bindings;
        var values = ReadActiveValues(bindings, resolved.Shared.ActiveActions);
        var usedEntryCount = bindings.ActiveUsedSpots(resolved.Shared.ActiveActions);
        var nativeHasEmptyEntry = bindings.ActiveHasEmptySpot(resolved.Shared.ActiveActions);
        var empty = 0;
        var supported = 0;
        var fruitMatches = 0;
        var fruitQuantity = 0;
        var fruitEngaged = false;
        var treasureMatches = 0;
        var treasureQuantity = 0;
        var treasureEngaged = false;
        foreach (var instance in values)
        {
#if SERVICE_CYCLE_PROFILE
            _profileOperations.AddListEntry();
#endif
            if (instance is null || instance.GetType() != bindings.InstanceType)
                return AutoHarvestActiveActionSnapshot.Invalid;
            if (bindings.InstanceIsEmpty(instance))
            {
                empty++;
                continue;
            }
            var plot = bindings.InstancePlot(instance);
            var action = bindings.InstanceAction(instance);
            var observed = ClassifyPair(resolved, plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory)
                return AutoHarvestActiveActionSnapshot.Invalid;
            if (observed == AutoHarvestObservedPair.Unrelated) continue;
            supported++;
            if (observed == AutoHarvestObservedPair.FruitTree)
            {
                fruitMatches++;
                fruitQuantity += bindings.InstanceQuantity(instance);
                fruitEngaged |= bindings.InstanceIsEngaged(instance);
            }
            else
            {
                treasureMatches++;
                treasureQuantity += bindings.InstanceQuantity(instance);
                treasureEngaged |= bindings.InstanceIsEngaged(instance);
            }
        }
        if (usedEntryCount < 0 || usedEntryCount != values.Count - empty)
            return AutoHarvestActiveActionSnapshot.Invalid;
        var fruit = new AutoHarvestActivePairState(fruitMatches, fruitQuantity, fruitEngaged);
        var treasure = new AutoHarvestActivePairState(
            treasureMatches,
            treasureQuantity,
            treasureEngaged);
        return new AutoHarvestActiveActionSnapshot(
            true,
            usedEntryCount,
            empty,
            nativeHasEmptyEntry,
            supported,
            fruit,
            treasure);
    }

    private static IList ReadActiveValues(AutoHarvestNativeBindings bindings, object activeActions) =>
        bindings.ActiveValues(activeActions) ??
        throw new InvalidOperationException("active plot actions are unavailable");
}

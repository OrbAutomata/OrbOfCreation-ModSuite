using System;
using static OrbAutomata.AutoHarvestReflectionAccess;

namespace OrbAutomata;

internal sealed partial class AutoHarvestNativeStateReader
{
    public AutoHarvestSubmissionState CaptureSubmissionState(
        in ResolvedAutoHarvestPair resolved) =>
        CaptureActiveActions(resolved).Project(resolved.Target.Pair);

    public bool IsPairEngaged(in ResolvedAutoHarvestPair resolved)
    {
        var contract = resolved.Contract;
        var values = RequireList(
            GetValue(contract.ActiveValues, resolved.Shared.ActiveActions),
            "active plot actions");
        var matches = 0;
        var engaged = false;
        foreach (var instance in values)
        {
            if (instance is null || instance.GetType() != contract.Types.Instance)
                return false;
            if (InvokeBool(contract.InstanceIsEmpty, instance)) continue;
            var plot = Invoke(contract.InstanceGetElement, instance, Array.Empty<object>());
            var action = Invoke(contract.InstanceGetAction, instance, Array.Empty<object>());
            var observed = ClassifyPair(resolved, plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory) return false;
            if (observed == AutoHarvestObservedPair.Unrelated ||
                observed != (resolved.Target.Pair == AutoHarvestPair.FruitTree
                    ? AutoHarvestObservedPair.FruitTree
                    : AutoHarvestObservedPair.TreasureTree)) continue;
            matches++;
            engaged |= InvokeBool(contract.InstanceIsEngaged, instance);
        }
        return matches == 1 && engaged;
    }

    public AutoHarvestActiveActionSnapshot CaptureActiveActions(
        in ResolvedAutoHarvestPair resolved)
    {
        var contract = resolved.Contract;
        var values = RequireList(
            GetValue(contract.ActiveValues, resolved.Shared.ActiveActions),
            "active plot actions");
        var usedEntryCount = InvokeInt(contract.ActiveGetUsedSpots, resolved.Shared.ActiveActions);
        var nativeHasEmptyEntry = InvokeBool(contract.ActiveHasEmptySpot, resolved.Shared.ActiveActions);
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
            if (instance is null || instance.GetType() != contract.Types.Instance)
                return AutoHarvestActiveActionSnapshot.Invalid;
            if (InvokeBool(contract.InstanceIsEmpty, instance))
            {
                empty++;
                continue;
            }
            var plot = Invoke(contract.InstanceGetElement, instance, Array.Empty<object>());
            var action = Invoke(contract.InstanceGetAction, instance, Array.Empty<object>());
            var observed = ClassifyPair(resolved, plot, action);
            if (observed == AutoHarvestObservedPair.Contradictory)
                return AutoHarvestActiveActionSnapshot.Invalid;
            if (observed == AutoHarvestObservedPair.Unrelated) continue;
            supported++;
            if (observed == AutoHarvestObservedPair.FruitTree)
            {
                fruitMatches++;
                fruitQuantity += InvokeInt(contract.InstanceGetActualQuantity, instance);
                fruitEngaged |= InvokeBool(contract.InstanceIsEngaged, instance);
            }
            else
            {
                treasureMatches++;
                treasureQuantity += InvokeInt(contract.InstanceGetActualQuantity, instance);
                treasureEngaged |= InvokeBool(contract.InstanceIsEngaged, instance);
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
}

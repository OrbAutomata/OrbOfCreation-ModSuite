using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Scoped class-2 revalidation for Scroll targeting. Calling the authored target selector is the
/// declared recomputation path; no ambient screen or unrelated cache is touched.
/// </summary>
internal static class AutoItemsScrollTargetPreflight
{
    internal static bool TryHasValidTarget(
        object consumable,
        int plannedLevel,
        AutoItemsNativeBindings native,
        out GameActionReason reason)
    {
        if (plannedLevel <= 0)
        {
            reason = "The planned Scroll level was unavailable.";
            return false;
        }
        if (!TryGetTargeting(consumable, native, out var targeting, out var targetingFailure))
        {
            reason = targetingFailure;
            return false;
        }

        try
        {
            var liveLevel = native.StrongestLevel(consumable);
            if (liveLevel != plannedLevel)
            {
                reason =
                    $"The strongest live Scroll level changed from planned {plannedLevel} " +
                    $"to {liveLevel}.";
                return false;
            }
            var count = native.Strongest(consumable);
            if (count is null || count.GetType() != native.CountType)
            {
                reason = "ConsumableSO.GetStrongest() returned no exact ConsumableCount.";
                return false;
            }
            var scaling = native.CountScaling(consumable, count);
            if (scaling is null || scaling.GetType() != native.ScalingType)
            {
                reason =
                    "ConsumableSO.GetCountScalingInfo(ConsumableCount) returned no exact ScalingInfo.";
                return false;
            }
            if (native.GetRandomList(targeting!, scaling) is not { } collection)
            {
                reason =
                    "Targeting.TargetStructure.GetRandomList(ScalingInfo) returned no target list.";
                return false;
            }
            if (collection.Count == 0)
            {
                reason =
                    "The live Scroll target selector found no valid structure target at its " +
                    $"strongest level {liveLevel}.";
                return false;
            }

            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            reason =
                GameActionFaultLog.Reason(
                    "The live Scroll target could not be checked.", ex, "Auto Items Scroll target");
            return false;
        }
    }

    private static bool TryGetTargeting(
        object consumable,
        AutoItemsNativeBindings native,
        out object? targeting,
        out string reason)
    {
        targeting = null;
        if (consumable.GetType() != native.ConsumableType ||
            native.OnUseEffects(consumable) is not { } blocks)
        {
            reason = "ConsumableSO.onUseEffects was unavailable for the exact live Scroll.";
            return false;
        }

        object? options = null;
        var requests = 0;
        foreach (var block in blocks)
        {
            if (block is null || block.GetType() != native.InstantBlockType ||
                native.EffectScripts(block) is not { } scripts)
            {
                reason =
                    "The live Scroll on-use graph was not a list of exact InstantEffectBlock values.";
                return false;
            }
            foreach (var script in scripts)
            {
                if (script is null || !native.InstantScriptType.IsInstanceOfType(script))
                {
                    reason =
                        "The live Scroll effect script list contained a non-IInstantEffectScript value.";
                    return false;
                }
                if (script.GetType() != native.RequestType) continue;
                requests++;
                options = native.TargetOptions(script);
            }
        }
        if (requests != 1)
        {
            reason =
                $"The live Scroll exposed {requests} exact RequestTargetEffectScript values; " +
                "exactly one is required.";
            return false;
        }
        if (options is null || options.GetType() != native.OptionsType)
        {
            reason =
                "RequestTargetEffectScript.targetOptions was not the exact audited " +
                "Targeting.TargetSelectOptions.";
            return false;
        }

        targeting = native.GetTargeting(options);
        if (targeting is null || targeting.GetType() != native.TargetStructureType)
        {
            reason =
                "Targeting.TargetSelectOptions.GetTargeting() did not return the exact audited " +
                "Targeting.TargetStructure.";
            return false;
        }

        reason = string.Empty;
        return true;
    }
}

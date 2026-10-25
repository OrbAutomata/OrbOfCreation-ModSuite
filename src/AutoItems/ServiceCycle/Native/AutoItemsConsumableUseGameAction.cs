using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single consumable-use GameAction: lifecycle-scoped complete bindings, live preflights,
/// one native transaction, and one native preparation-queue sentinel.
/// </summary>
internal sealed class AutoItemsConsumableUseGameAction : ConsumableUseGameAction<AutoItemsNativeBindings>
{
    private static readonly GameActionCodes Codes = new(
        AutoItemsActionResultCodes.WrongThread,
        AutoItemsActionResultCodes.ContractUnavailable,
        AutoItemsActionResultCodes.MutationPermitUnavailable,
        AutoItemsActionResultCodes.UseNotVerified,
        AutoItemsActionResultCodes.UseNotVerified);

    private readonly TypedRegistryResolver _registryResolver;

    internal AutoItemsConsumableUseGameAction(
        TypedRegistryResolver registryResolver,
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readMutationPermitFailure)
        : base("Auto Items", Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readMutationPermitFailure, AutoItemsNativeBindings.TryCreate)
    {
        _registryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
    }

    internal NativeActionOutcome Submit(in AutoItemsCycleAction action)
    {
        if (!TryEnter(action.CollectedAtEpoch, out var native, out var refusal)) return refusal;
        if (native.IsTargeting())
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.TargetingInProgress,
                "TargetingManager.IsTargeting() reported an active native targeting interaction.");

        var resolution = _registryResolver.Resolve(action.ItemId, native.ConsumableType);
        if (!resolution.IsResolved)
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.ItemUnavailable,
                resolution.FormatCause());
        var item = resolution.Value!;
        if (!HasExpectedFamily(item, action.Family, native, out var reason))
            return NativeActionOutcome.Reject(AutoItemsActionResultCodes.FamilyChanged, reason);
        if (!native.IsVisible(item))
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.NotVisible,
                $"The game is not showing {EntityIdentityFormatter.PlayerName(action.ItemId)} yet.");
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            !native.CanBeRandomized(item))
        {
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.RandomizationUnavailable,
                $"{EntityIdentityFormatter.PlayerName(action.ItemId)} can no longer be randomized.");
        }
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            !AutoItemsScrollTargetPreflight.TryHasValidTarget(
                item,
                action.PlannedLevel,
                native,
                out var targetReason))
        {
            return NativeActionOutcome.Reject(AutoItemsActionResultCodes.TargetUnavailable, targetReason);
        }

        var temporary = AutoItemsConsumableFamilies.IsTemporary(action.Family);
        if (temporary &&
            !TryHasFinitePositiveDuration(item, action.ItemId, native, out reason))
        {
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.TemporaryDurationChanged,
                reason);
        }
        if (temporary &&
            !TryHasSafeTemporaryCosts(item, action.ItemId, native, out reason))
        {
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.TemporaryCostChanged,
                reason);
        }
        if (temporary)
        {
            if (!TryAnyTemporaryUsage(native, out var temporaryUsagePresent, out reason))
                return NativeActionOutcome.FaultBeforeCall(
                    AutoItemsActionResultCodes.ContractUnavailable,
                    reason);
            if (temporaryUsagePresent)
                return NativeActionOutcome.Reject(
                    AutoItemsActionResultCodes.TemporaryEffectPresent,
                    reason);
        }
        if (!native.CanUse())
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.NativeBusy,
                "Inventory.CanUseConsumable() refused because native consumable preparation is busy.");
        if (!native.CanFire(item))
            return NativeActionOutcome.Reject(
                AutoItemsActionResultCodes.CanFireRefused,
                $"The game refuses to use {EntityIdentityFormatter.PlayerName(action.ItemId)} right now.");
        return UseConsumable(action.ItemId, item, native,
            AutoItemsActionResultCodes.MultiBuyUnavailable,
            randomize: action.Family == AutoItemsConsumableFamily.Scroll,
            multiBuyFailureIsFault: true);
    }

    private static bool HasExpectedFamily(
        object item,
        AutoItemsConsumableFamily expected,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!TryReadSupportedFamilyEvidence(
                item,
                native,
                out var families,
                out reason))
        {
            return false;
        }
        if (families.TryResolveExecutionFamily(out var actual) && actual == expected)
        {
            reason = string.Empty;
            return true;
        }

        reason =
            $"Expected live execution family {expected} for the planned consumable, but " +
            $"observed supported memberships [{families.Describe()}] and resolved {actual}.";
        return false;
    }

    private static bool TryHasFinitePositiveDuration(
        object item,
        Guid itemId,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!native.HasDuration(item))
        {
            reason = $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} no longer has ConsumableSO.hasDuration=true.";
            return false;
        }
        var durationBase = native.DurationBase(item);
        if (durationBase <= 0d || double.IsNaN(durationBase) || double.IsInfinity(durationBase))
        {
            reason =
                $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} has non-finite or non-positive " +
                $"ConsumableSO.durationBase={durationBase}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryHasSafeTemporaryCosts(
        object item,
        Guid itemId,
        AutoItemsNativeBindings native,
        out string reason)
    {
        if (!TryHasToxicityOnlyCosts(
                native.ConsumeCost(item),
                "ConsumableSO.consumeCost",
                itemId,
                native,
                requireToxicity: true,
                out reason))
        {
            return false;
        }
        return TryHasToxicityOnlyCosts(
            native.UsageCost(item),
            "ConsumableSO.usageCost",
            itemId,
            native,
            requireToxicity: false,
            out reason);
    }

    private static bool TryHasToxicityOnlyCosts(
        object? costList,
        string category,
        Guid itemId,
        AutoItemsNativeBindings native,
        bool requireToxicity,
        out string reason)
    {
        if (costList is null)
        {
            reason = $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} has null {category}.";
            return false;
        }
        if (native.Costs(costList) is not { } costs)
        {
            reason = $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} has unreadable {category}.costs.";
            return false;
        }

        var hasToxicity = false;
        var index = 0;
        foreach (var entry in costs)
        {
            if (entry is null || entry.GetType() != native.CostEntryType)
            {
                reason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category}.costs[{index}] is not the exact " +
                    "ResourceTuple type.";
                return false;
            }
            var resource = native.CostResource(entry);
            if (resource is null || resource.GetType() != native.ResourceType)
            {
                reason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category}.costs[{index}].resource is not " +
                    "the exact ResourceSO type.";
                return false;
            }
            var resourceId = native.ResourceGuid(resource);
            if (resourceId != KnownEntities.PotionToxicity.Uuid)
            {
                reason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category}.costs[{index}] names extra resource " +
                    $"{EntityIdentityFormatter.PlayerName(resourceId)}; only Potion Toxicity is permitted.";
                return false;
            }
            var amount = native.CostAmount(entry);
            if (BigDouble.IsNaN(amount) ||
                BigDouble.IsInfinity(amount) ||
                amount.CompareTo(BigDouble.Zero) < 0)
            {
                reason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category}.costs[{index}].valueBig is invalid.";
                return false;
            }
            hasToxicity = true;
            index++;
        }

        if (requireToxicity && !hasToxicity)
        {
            reason = $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category} has no Potion Toxicity entry.";
            return false;
        }
        reason = string.Empty;
        return true;
    }

    private static bool TryAnyTemporaryUsage(
        AutoItemsNativeBindings native,
        out bool present,
        out string reason)
    {
        present = false;
        if (native.AllConsumables() is not { } all)
        {
            reason = "ConsumableSO.All was unavailable while checking temporary usage exclusion.";
            return false;
        }

        foreach (var candidate in all)
        {
            if (candidate is null || candidate.GetType() != native.ConsumableType)
            {
                reason =
                    "ConsumableSO.All contained a value that was not the exact ConsumableSO type.";
                return false;
            }
            if (!TryReadSupportedFamilyEvidence(
                    candidate,
                    native,
                    out var families,
                    out reason))
            {
                return false;
            }
            if (families.Count == 0) continue;
            if (!families.TryResolveExecutionFamily(out var family))
            {
                reason =
                    "A live consumable has incoherent supported family memberships [" +
                    families.Describe() + "].";
                return false;
            }
            if (!AutoItemsConsumableFamilies.IsTemporary(family)) continue;
            if (native.GetUsages(candidate) is not { } usages)
            {
                reason =
                    $"Temporary-family consumable family={family}, " +
                    $"supportedFamilies=[{families.Describe()}] did not expose " +
                    "ConsumableSO.consumableUsages as ICollection.";
                return false;
            }
            if (usages.Count <= 0) continue;
            present = true;
            reason =
                "A native temporary-item usage is already pending or active: " +
                $"family={family}, supportedFamilies=[{families.Describe()}], usages={usages.Count}.";
            return true;
        }

        reason = string.Empty;
        return true;
    }

    private static bool TryReadSupportedFamilyEvidence(
        object item,
        AutoItemsNativeBindings native,
        out AutoItemsConsumableFamilySet families,
        out string reason)
    {
        families = new AutoItemsConsumableFamilySet();
        if (native.Families(item) is not { } entries)
        {
            reason = "ConsumableSO.consumableTypes was unavailable on the live item.";
            return false;
        }

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry is null || entry.GetType() != native.FamilyType)
            {
                reason =
                    "ConsumableSO.consumableTypes contained a non-ConsumableTypeSO value.";
                return false;
            }
            var familyId = native.FamilyGuid(entry);
            if (familyId == Guid.Empty)
            {
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            for (var previous = 0; previous < index; previous++)
            {
                if (native.FamilyGuid(entries[previous]!) != familyId) continue;
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            var candidate = AutoItemsConsumableFamilies.FromTypeId(familyId);
            if (candidate == AutoItemsConsumableFamily.Unknown) continue;
            if (families.TryAdd(candidate)) continue;
            reason =
                $"ConsumableSO.consumableTypes repeated supported family {candidate}.";
            return false;
        }
        reason = string.Empty;
        return true;
    }
}

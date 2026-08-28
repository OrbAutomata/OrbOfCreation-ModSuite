using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The single consumable-use GameAction: lifecycle-scoped complete bindings, live preflights,
/// one native transaction, and one native preparation-queue sentinel.
/// </summary>
internal sealed partial class AutoItemsConsumableUseGameAction : IDisposable
{
    private static readonly object[] EnableRandomizationArguments = { true };

    private readonly TypedRegistryResolver _registryResolver;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readMutationPermitFailure;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<string, Type?>? _resolvePlayerType;
    private readonly Func<string, bool>? _includePlayerContract;
    private readonly int _mainThreadId;
    private readonly Dictionary<Guid, string> _temporaryQuarantine = new();
    private AutoItemsNativeBindings? _bindings;
    private ConsumablePlayerNativeBindings? _playerBindings;
    private string _bindingFailure = string.Empty;
    private string _playerBindingFailure = string.Empty;
    private string _quarantineReason = string.Empty;

    internal AutoItemsConsumableUseGameAction(
        TypedRegistryResolver registryResolver,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readMutationPermitFailure,
        Func<long>? readLifecycleEpoch = null,
        Func<string, Type?>? resolvePlayerType = null,
        Func<string, bool>? includePlayerContract = null)
    {
        _registryResolver = registryResolver ??
            throw new ArgumentNullException(nameof(registryResolver));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ??
            throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readMutationPermitFailure = readMutationPermitFailure ??
            throw new ArgumentNullException(nameof(readMutationPermitFailure));
        _readLifecycleEpoch = readLifecycleEpoch ?? (static () => 1);
        _resolvePlayerType = resolvePlayerType;
        _includePlayerContract = includePlayerContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;
    internal bool PlayerBindingsAvailable => _playerBindings is not null;
    internal string PlayerBindingFailure => _playerBindingFailure;
    internal string QuarantineReason => _quarantineReason;

    internal AutoItemsSubmission Submit(in AutoItemsCycleAction action)
    {
        if (_temporaryQuarantine.TryGetValue(action.ItemId, out var exactQuarantineReason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                exactQuarantineReason);
        if (_quarantineReason.Length != 0)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.Quarantined,
                _quarantineReason);
        if (_bindings is not { } native)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Inventory"));
        if (InvokeBool(native.IsTargeting, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TargetingInProgress,
                "TargetingManager.IsTargeting() reported an active native targeting interaction.");

        var resolution = _registryResolver.Resolve(action.ItemId, native.ConsumableType);
        if (!resolution.IsResolved)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ItemUnavailable,
                resolution.Format());
        var item = resolution.Value!;
        if (!HasExpectedFamily(item, action.Family, native, out var reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.FamilyChanged, reason);
        if (!InvokeBool(native.IsVisible, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NotVisible,
                $"The game is not showing {EntityIdentityFormatter.PlayerName(action.ItemId)} yet.");
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            native.CanBeRandomized.GetValue(item) is not true)
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.RandomizationUnavailable,
                $"{EntityIdentityFormatter.PlayerName(action.ItemId)} can no longer be randomized.");
        }
        if (action.Family == AutoItemsConsumableFamily.Scroll &&
            !AutoItemsScrollTargetPreflight.TryHasValidTarget(
                item,
                action.PlannedLevel,
                native,
                out reason))
        {
            return AutoItemsSubmission.Reject(AutoItemsPreflight.TargetUnavailable, reason);
        }

        var temporary = AutoItemsConsumableFamilies.IsTemporary(action.Family);
        if (temporary &&
            !TryHasFinitePositiveDuration(item, action.ItemId, native, out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryDurationChanged,
                reason);
        }
        if (temporary &&
            !TryHasSafeTemporaryCosts(item, action.ItemId, native, out reason))
        {
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryCostChanged,
                reason);
        }
        if (!TryAnyTemporaryUsage(native, out var temporaryUsagePresent, out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.ContractUnavailable,
                reason);
        if (temporaryUsagePresent)
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.TemporaryEffectPresent,
                reason);
        if (!InvokeBool(native.CanUseConsumable, null))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.NativeBusy,
                "Inventory.CanUseConsumable() refused because native consumable preparation is busy.");
        if (!InvokeBool(native.CanFire, item))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.CanFireRefused,
                $"The game refuses to use {EntityIdentityFormatter.PlayerName(action.ItemId)} right now.");
        if (!TryCaptureMutationPermit(out reason))
            return AutoItemsSubmission.Reject(
                AutoItemsPreflight.MutationPermitUnavailable,
                reason);
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out reason))
            return AutoItemsSubmission.Reject(AutoItemsPreflight.MultiBuyUnavailable, reason);

        var itemId = action.ItemId;
        var family = action.Family;
        NativeMutationEvidence<int> evidence;
        using (multiBuy)
        {
            evidence = NativeMutationVerifier.Execute(
                "Auto Items consumable use",
                EntityIdentityFormatter.PlayerName(itemId),
                "the exact item enters the native preparation queue",
                () => Invoke<int>(native.GetQueued, item),
                () => Mutate(item, family, native),
                (before, after) => after == before + 1);
        }

        var callOutcome = new NativeMutationCallOutcome(
            evidence.MutationWasAttempted
                ? action.Family == AutoItemsConsumableFamily.Scroll ? 2 : 1
                : 0,
            evidence.MutationWasAttempted ? 1 : 0,
            evidence.IsVerified ? 1 : 0);
        var preflight = AutoItemsPreflight.Proceeded;
        var failureReason = string.Empty;
        if (evidence.MutationWasAttempted && !evidence.IsVerified)
        {
            if (temporary)
            {
                failureReason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(action.ItemId)} is quarantined for this lifecycle after " +
                    $"an ambiguous consumable mutation: {evidence.Detail}";
                _temporaryQuarantine[action.ItemId] = failureReason;
            }
            else
            {
                _quarantineReason =
                    "Auto Items is quarantined for this lifecycle after an ambiguous consumable " +
                    $"mutation on {EntityIdentityFormatter.PlayerName(action.ItemId)}: {evidence.Detail}";
                failureReason = _quarantineReason;
            }
            preflight = AutoItemsPreflight.Quarantined;
        }
        else if (!evidence.IsVerified)
        {
            preflight = AutoItemsPreflight.ContractUnavailable;
            failureReason =
                "Auto Items could not read the consumable preparation queue for " +
                $"{EntityIdentityFormatter.PlayerName(action.ItemId)}: {evidence.Detail}";
        }
        return new AutoItemsSubmission(
            preflight,
            evidence.Outcome,
            callOutcome,
            evidence.IsVerified
                ? $"{action.Family} {EntityIdentityFormatter.PlayerName(action.ItemId)} entered native preparation."
                : failureReason);
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _playerBindings = null;
        _bindingFailure = string.Empty;
        _playerBindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        _temporaryQuarantine.Clear();
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _playerBindings = null;
        _bindingFailure = string.Empty;
        _playerBindingFailure = string.Empty;
        _quarantineReason = string.Empty;
        _temporaryQuarantine.Clear();
    }

    private void BindLifecycle()
    {
        if (AutoItemsNativeBindings.TryCreate(out var bindings, out var reason))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            BindPlayerLifecycle();
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
        BindPlayerLifecycle();
    }

    private void BindPlayerLifecycle()
    {
        var resolve = _resolvePlayerType ?? ReflectionUtil.FindLoadedType;
        var include = _includePlayerContract ?? (_ => true);
        if (ConsumablePlayerNativeBindings.TryCreate(
                resolve,
                include,
                out var player,
                out var reason))
        {
            _playerBindings = player;
            _playerBindingFailure = string.Empty;
            return;
        }
        _playerBindings = null;
        _playerBindingFailure = reason;
    }

    private bool TryCaptureMutationPermit(out string reason)
    {
        try
        {
            if (_tryCaptureMutationPermit())
            {
                reason = string.Empty;
                return true;
            }
            reason = _readMutationPermitFailure();
            if (string.IsNullOrWhiteSpace(reason))
                reason = "Auto Items no longer owns ConsumableUse and NativeMultiBuyOverride.";
            return false;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            reason =
                "Auto Items could not capture its consumable-use ownership permit: " +
                ex.GetBaseException().Message;
            return false;
        }
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
        if (native.HasDuration.GetValue(item) is not true)
        {
            reason = $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} no longer has ConsumableSO.hasDuration=true.";
            return false;
        }
        if (native.DurationBase.GetValue(item) is not double durationBase)
        {
            reason = $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} did not expose ConsumableSO.durationBase as Double.";
            return false;
        }
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
                native.ConsumeCost.GetValue(item),
                "ConsumableSO.consumeCost",
                itemId,
                native,
                requireToxicity: true,
                out reason))
        {
            return false;
        }
        return TryHasToxicityOnlyCosts(
            native.UsageCost.GetValue(item),
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
        if (native.Costs.GetValue(costList) is not IEnumerable costs)
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
            var resource = native.CostResource.GetValue(entry);
            if (resource is null || resource.GetType() != native.ResourceType)
            {
                reason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category}.costs[{index}].resource is not " +
                    "the exact ResourceSO type.";
                return false;
            }
            var resourceId = Invoke<Guid>(native.ResourceGuid, resource);
            if (resourceId != KnownEntities.PotionToxicity.Uuid)
            {
                reason =
                    $"Temporary item {EntityIdentityFormatter.PlayerName(itemId)} {category}.costs[{index}] names extra resource " +
                    $"{EntityIdentityFormatter.PlayerName(resourceId)}; only Potion Toxicity is permitted.";
                return false;
            }
            if (native.CostAmount.GetValue(entry) is not BigDouble amount ||
                BigDouble.IsNaN(amount) ||
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
        if (native.AllConsumables.GetValue(null) is not IEnumerable all)
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
            if (native.Usages.GetValue(candidate) is not ICollection usages)
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
        if (native.Families.GetValue(item) is not IList entries)
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
            var familyId = Invoke<Guid>(native.FamilyGuid, entry);
            if (familyId == Guid.Empty)
            {
                reason =
                    "ConsumableSO.consumableTypes contained an empty or duplicate stable family UUID.";
                return false;
            }
            for (var previous = 0; previous < index; previous++)
            {
                if (Invoke<Guid>(native.FamilyGuid, entries[previous]!) != familyId) continue;
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

    private static void Mutate(
        object item,
        AutoItemsConsumableFamily family,
        AutoItemsNativeBindings native)
    {
        if (family == AutoItemsConsumableFamily.Scroll)
        {
            native.SetRandomization.Invoke(item, EnableRandomizationArguments);
            if (!InvokeBool(native.IsRandomized, item))
                throw new InvalidOperationException(
                    "ConsumableSO.SetRandomization(true) was not confirmed by IsRandomized().");
        }
        native.SelectAndFire.Invoke(item, Array.Empty<object>());
    }

    private static bool InvokeBool(MethodInfo method, object? target) =>
        Invoke<bool>(method, target);

    private static T Invoke<T>(MethodInfo method, object? target) =>
        method.Invoke(target, Array.Empty<object>()) is T value
            ? value
            : throw new InvalidOperationException(
                $"{method.DeclaringType?.Name}.{method.Name} did not return {typeof(T).Name}.");

}

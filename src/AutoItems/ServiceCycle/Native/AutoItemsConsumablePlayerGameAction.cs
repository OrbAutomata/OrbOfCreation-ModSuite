using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// The player's own consumable presses on the Inventory screen: use, cancel a queued use, discard,
/// set randomization, and move within a list. Each re-resolves the exact consumable and its live
/// gates on Unity's main thread and verifies one outcome.
/// </summary>
internal sealed class AutoItemsConsumablePlayerGameAction
    : NativeGameAction<ConsumablePlayerNativeBindings>
{
    private const string Screen = "Inventory";

    private static readonly GameActionCodes Codes = new(
        ConsumablePlayerActionResultCodes.WrongThread,
        ConsumablePlayerActionResultCodes.ContractUnavailable,
        ConsumablePlayerActionResultCodes.MutationPermitUnavailable,
        ConsumablePlayerActionResultCodes.PostCommitFault,
        ConsumablePlayerActionResultCodes.VerificationFailed);

    private readonly TypedRegistryResolver _registry;

    internal AutoItemsConsumablePlayerGameAction(
        TypedRegistryResolver registry,
        Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
        : base("Consumable", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out ConsumablePlayerNativeBindings? bindings, out string reason) =>
                ConsumablePlayerNativeBindings.TryCreate(resolveType ?? ReflectionUtil.FindLoadedType,
                    includeContract ?? (_ => true), out bindings, out reason))
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    internal NativeActionOutcome Submit(in ConsumablePlayerAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, native =>
        {
            if (!_registry.TryResolveCurrent(request.ConsumableId, native.ConsumableType,
                    out var item, out var reason))
                return NativeActionOutcome.Reject(ConsumablePlayerActionResultCodes.ItemUnavailable, reason);
            return request.Kind switch
            {
                ConsumablePlayerActionKind.Use => Use(in request, item, native),
                ConsumablePlayerActionKind.Cancel => Cancel(in request, item, native),
                ConsumablePlayerActionKind.Discard => Discard(in request, item, native),
                ConsumablePlayerActionKind.SetRandomization => SetRandomization(in request, item, native),
                ConsumablePlayerActionKind.Move => Move(in request, item, native),
                _ => NativeActionOutcome.Reject(
                    ConsumablePlayerActionResultCodes.ContractUnavailable,
                    "Unknown consumable action mode."),
            };
        });
    }

    private NativeActionOutcome Use(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        if (!native.IsVisible(item))
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.NotVisible,
                EntityIdentityFormatter.PlayerName(action.ConsumableId) +
                " is not visible or owned right now.");
        if (native.IsTargeting())
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.TargetingInProgress,
                "A native target request is already pending.");
        if (!native.CanUse())
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.InventoryBusy,
                "Another consumable is still being prepared.");
        if (!native.CanFire(item))
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.CanFireRefused,
                EntityIdentityFormatter.PlayerName(action.ConsumableId) +
                " cannot be used right now.");
        var before = native.GetQueued(item);
        if (!TryPermit(out var refusal)) return refusal;
        if (!NativeMultiBuyScope.TryEnterOne(out var scope, out var reason))
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.MultiBuyUnavailable, reason);

        return Execute(EntityIdentityFormatter.PlayerName(action.ConsumableId),
            "The exact consumable entered the native preparation queue.", before,
            () => native.GetQueued(item),
            () =>
            {
                using (scope) native.SelectAndFire(item);
            },
            static (queued, after) => after == checked(queued + 1));
    }

    private NativeActionOutcome Cancel(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var usages = native.GetUsages(item);
        var selected = native.GetNextUsage(item);
        if (selected is null)
        {
            for (var index = 0; index < usages.Count; index++)
            {
                var candidate = usages[index];
                if (candidate is not null && candidate.GetType() == native.UsageType &&
                    !native.UsageEngaged(candidate))
                {
                    selected = candidate;
                    break;
                }
            }
        }
        if (selected is null || selected.GetType() != native.UsageType)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.NoCancellableUsage,
                "The exact consumable has no native pending usage to cancel.");
        var selectedId = native.UsageGuid(selected);
        var resultInfo = native.UsageResultInfo(selected);
        if (selectedId == Guid.Empty || resultInfo is null)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.NoCancellableUsage,
                "The game is not showing a queued use of this item that could be cancelled.");
        if (!TryPermit(out var refusal)) return refusal;

        return Execute(EntityIdentityFormatter.PlayerName(action.ConsumableId),
            "The exact pending usage was cancelled and removed.", false,
            () => !HasUsage(native, item, selectedId) && native.IsCancelled(resultInfo),
            () => native.CancelUsage(item),
            static (_, landed) => landed);
    }

    private NativeActionOutcome Discard(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var before = native.GetQuantity(item);
        if (before <= 0)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.NothingToDiscard,
                "The exact consumable has no owned amount to discard.");
        var discarded = Math.Min(action.Amount, before);
        if (!TryPermit(out var refusal)) return refusal;

        return Execute(EntityIdentityFormatter.PlayerName(action.ConsumableId),
            "The requested clamped amount left the exact consumable holding.", before,
            () => native.GetQuantity(item),
            () => native.Discard(item, discarded),
            (held, after) => after == checked(held - discarded));
    }

    private NativeActionOutcome SetRandomization(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        if (!native.CanBeRandomized(item))
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.RandomizationUnavailable,
                "The game never offers randomization for this item.");
        var requested = action.Randomized;
        if (native.IsRandomized(item) == requested)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.AlreadyInRequestedState,
                "The consumable already has the requested randomization state.");
        if (!TryPermit(out var refusal)) return refusal;

        return Execute(EntityIdentityFormatter.PlayerName(action.ConsumableId),
            "The exact consumable now has the requested randomization state.", !requested,
            () => native.IsRandomized(item),
            () => native.SetRandomization(item, requested),
            (_, after) => after == requested);
    }

    private NativeActionOutcome Move(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var inventory = native.GetInventory();
        if (inventory is null)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.ListUnavailable,
                "Inventory._instance is unavailable in the current lifecycle.");
        var list = action.List == ConsumablePlayerListKind.Hotbar
            ? native.GetHotbarList(inventory)
            : native.GetInventoryList(inventory);
        if (list is null || list.GetType() != native.ListType)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.ListUnavailable,
                "Inventory did not expose the requested exact ConsumableRefListVariable.");
        var values = native.GetListValues(list);
        var source = -1;
        for (var index = 0; index < values.Count; index++)
        {
            if (!ReferenceEquals(values[index], item)) continue;
            if (source >= 0)
                return NativeActionOutcome.Reject(
                    ConsumablePlayerActionResultCodes.SourceUnavailable,
                    "The exact consumable appears more than once in the requested list.");
            source = index;
        }
        if (source < 0)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.SourceUnavailable,
                "The exact consumable is absent from the requested list.");
        if (action.Destination >= values.Count)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.DestinationOutOfRange,
                "Destination " + action.Destination +
                " is outside the live list length " + values.Count + ".");
        if (source == action.Destination)
            return NativeActionOutcome.Reject(
                ConsumablePlayerActionResultCodes.AlreadyInRequestedState,
                "The exact consumable already occupies the requested destination.");
        if (!TryPermit(out var refusal)) return refusal;
        var destination = action.Destination;
        var hotbar = action.List == ConsumablePlayerListKind.Hotbar;

        return Execute(EntityIdentityFormatter.PlayerName(action.ConsumableId),
            "The exact consumable moved to the requested list position.", false,
            () => ReferenceEquals(native.GetListValues(list)[destination], item),
            () =>
            {
                native.Swap(list, source, destination);
                native.Update(list);
                if (hotbar) native.SetAt(list, destination, item);
            },
            static (_, landed) => landed);
    }

    private static bool HasUsage(
        ConsumablePlayerNativeBindings native, object item, Guid usageId)
    {
        var usages = native.GetUsages(item);
        for (var index = 0; index < usages.Count; index++)
        {
            var usage = usages[index];
            if (usage is not null && usage.GetType() == native.UsageType &&
                native.UsageGuid(usage) == usageId) return true;
        }
        return false;
    }
}

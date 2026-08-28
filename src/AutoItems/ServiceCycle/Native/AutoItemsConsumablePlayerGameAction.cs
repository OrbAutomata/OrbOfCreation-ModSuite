using System;
using System.Collections;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed partial class AutoItemsConsumableUseGameAction
{
    internal ConsumablePlayerSubmission Submit(in ConsumablePlayerAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_playerBindings is not { } native)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Inventory"));

        long liveLifecycle;
        try { liveLifecycle = _readLifecycleEpoch(); }
        catch (Exception ex) when (Expected(ex))
        {
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.LifecycleReplaced,
                "The live lifecycle could not be read: " + ex.GetBaseException().Message);
        }
        if (liveLifecycle != action.LifecycleEpoch)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        var resolution = _registryResolver.Resolve(action.ConsumableId, native.ConsumableType);
        if (!resolution.IsResolved)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.ItemUnavailable, resolution.Format());
        var item = resolution.Value!;
        try
        {
            return action.Kind switch
            {
                ConsumablePlayerActionKind.Use => PlayerUse(in action, item, native),
                ConsumablePlayerActionKind.Cancel => PlayerCancel(in action, item, native),
                ConsumablePlayerActionKind.Discard => PlayerDiscard(in action, item, native),
                ConsumablePlayerActionKind.SetRandomization =>
                    PlayerSetRandomization(in action, item, native),
                ConsumablePlayerActionKind.Move => PlayerMove(in action, item, native),
                _ => ConsumablePlayerSubmission.Reject(
                    in action, ConsumablePlayerPreflight.ContractUnavailable,
                    "Unknown consumable action mode."),
            };
        }
        catch (Exception ex) when (Expected(ex))
        {
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Inventory"));
        }
    }

    private ConsumablePlayerSubmission PlayerUse(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        if (!native.IsVisible(item))
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.NotVisible,
                EntityIdentityFormatter.PlayerName(action.ConsumableId) +
                " is not visible or owned right now.");
        if (native.IsTargeting())
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.TargetingInProgress,
                "A native target request is already pending.");
        if (!native.CanUse())
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.InventoryBusy,
                "Another consumable is still being prepared.");
        if (!native.CanFire(item))
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.CanFireRefused,
                EntityIdentityFormatter.PlayerName(action.ConsumableId) +
                " cannot be used right now.");
        var beforeQueued = native.GetQueued(item);
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        if (!NativeMultiBuyScope.TryEnterOne(out var multiBuy, out var reason))
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.MultiBuyUnavailable, reason);

        using (multiBuy)
        {
            return Execute(
                in action,
                ConsumablePlayerNativeStage.Use,
                1,
                () => native.SelectAndFire(item),
                () => native.GetQueued(item) == checked(beforeQueued + 1),
                "The exact consumable entered the native preparation queue.");
        }
    }

    private ConsumablePlayerSubmission PlayerCancel(
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
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.NoCancellableUsage,
                "The exact consumable has no native pending usage to cancel.");
        var selectedId = native.UsageGuid(selected);
        var resultInfo = native.UsageResultInfo(selected);
        if (selectedId == Guid.Empty || resultInfo is null)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.NoCancellableUsage,
                "The game is not showing a queued use of this item that could be cancelled.");
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;

        return Execute(
            in action,
            ConsumablePlayerNativeStage.Cancel,
            1,
            () => native.CancelUsage(item),
            () => !HasUsage(native, item, selectedId) && native.IsCancelled(resultInfo),
            "The exact pending usage was cancelled and removed.");
    }

    private ConsumablePlayerSubmission PlayerDiscard(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var beforeAmount = native.GetQuantity(item);
        if (beforeAmount <= 0)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.NothingToDiscard,
                "The exact consumable has no owned amount to discard.");
        var discarded = Math.Min(action.Amount, beforeAmount);
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;

        return Execute(
            in action,
            ConsumablePlayerNativeStage.Discard,
            1,
            () => native.Discard(item, discarded),
            () => native.GetQuantity(item) == checked(beforeAmount - discarded),
            "The requested clamped amount left the exact consumable holding.");
    }

    private ConsumablePlayerSubmission PlayerSetRandomization(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        if (!native.CanBeRandomized(item))
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.RandomizationUnavailable,
                "The game never offers randomization for this item.");
        var requested = action.Randomized;
        if (native.IsRandomized(item) == requested)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.AlreadyInRequestedState,
                "The consumable already has the requested randomization state.");
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;

        return Execute(
            in action,
            ConsumablePlayerNativeStage.Randomization,
            1,
            () => native.SetRandomization(item, requested),
            () => native.IsRandomized(item) == requested,
            "The exact consumable now has the requested randomization state.");
    }

    private ConsumablePlayerSubmission PlayerMove(
        in ConsumablePlayerAction action,
        object item,
        ConsumablePlayerNativeBindings native)
    {
        var inventory = native.GetInventory();
        if (inventory is null)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.ListUnavailable,
                "Inventory._instance is unavailable in the current lifecycle.");
        var list = action.List == ConsumablePlayerListKind.Hotbar
            ? native.GetHotbarList(inventory)
            : native.GetInventoryList(inventory);
        if (list is null || list.GetType() != native.ListType)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.ListUnavailable,
                "Inventory did not expose the requested exact ConsumableRefListVariable.");
        var values = native.GetListValues(list);
        var source = -1;
        for (var index = 0; index < values.Count; index++)
        {
            if (!ReferenceEquals(values[index], item)) continue;
            if (source >= 0)
                return ConsumablePlayerSubmission.Reject(
                    in action, ConsumablePlayerPreflight.SourceUnavailable,
                    "The exact consumable appears more than once in the requested list.");
            source = index;
        }
        if (source < 0)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.SourceUnavailable,
                "The exact consumable is absent from the requested list.");
        if (action.Destination >= values.Count)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.DestinationOutOfRange,
                "Destination " + action.Destination +
                " is outside the live list length " + values.Count + ".");
        if (source == action.Destination)
            return ConsumablePlayerSubmission.Reject(
                in action, ConsumablePlayerPreflight.AlreadyInRequestedState,
                "The exact consumable already occupies the requested destination.");
        if (!TryPlayerPermit(in action, out var permitFailure)) return permitFailure;
        var calls = action.List == ConsumablePlayerListKind.Hotbar ? 3 : 2;
        var destination = action.Destination;
        var requestedList = action.List;

        return Execute(
            in action,
            ConsumablePlayerNativeStage.Reorder,
            calls,
            () =>
            {
                native.Swap(list, source, destination);
                native.Update(list);
                if (requestedList == ConsumablePlayerListKind.Hotbar)
                    native.SetAt(list, destination, item);
            },
            () => ReferenceEquals(native.GetListValues(list)[destination], item),
            "The exact consumable moved to the requested list position.");
    }

    private ConsumablePlayerSubmission Execute(
        in ConsumablePlayerAction action,
        ConsumablePlayerNativeStage stage,
        int calls,
        Action mutate,
        Func<bool> landed,
        string success)
    {
        try
        {
            mutate();
            return landed()
                ? Verified(calls, success)
                : PlayerFault(in action, ConsumablePlayerPreflight.VerificationFailed,
                    ConsumablePlayerNativeStage.Verification,
                    NativeMutationOutcome.PostconditionFailed, calls,
                    "The requested transition did not become observable.");
        }
        catch (Exception ex) when (Expected(ex))
        {
            if (LandedBestEffort(landed))
                return Verified(calls,
                    "The requested transition landed before the native exception.");
            return PlayerFault(in action, ConsumablePlayerPreflight.PostCommitFault,
                stage, NativeMutationOutcome.ExecutionThrew, calls,
                "The native pipeline threw before the requested transition was observable: " +
                ex.GetBaseException().Message);
        }
    }

    private bool TryPlayerPermit(
        in ConsumablePlayerAction action,
        out ConsumablePlayerSubmission failure)
    {
        if (TryCaptureMutationPermit(out var reason))
        {
            failure = default;
            return true;
        }
        failure = ConsumablePlayerSubmission.Reject(
            in action, ConsumablePlayerPreflight.MutationPermitUnavailable, reason);
        return false;
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

    private static bool LandedBestEffort(Func<bool> landed)
    {
        try { return landed(); }
        catch (Exception ex) when (Expected(ex)) { return false; }
    }

    private static ConsumablePlayerSubmission Verified(int calls, string reason) =>
        new(ConsumablePlayerPreflight.Proceeded,
            ConsumablePlayerNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(calls, 1, 1),
            reason);

    private static ConsumablePlayerSubmission PlayerFault(
        in ConsumablePlayerAction action,
        ConsumablePlayerPreflight preflight,
        ConsumablePlayerNativeStage stage,
        NativeMutationOutcome outcome,
        int calls,
        string reason) =>
        new(preflight, stage, outcome,
            new NativeMutationCallOutcome(calls, 1, 0),
            "Consumable " + action.Kind +
            " could not prove the requested outcome: " + reason);

    private static bool Expected(Exception ex) =>
        ex is not StackOverflowException and not OutOfMemoryException and not AccessViolationException;
}

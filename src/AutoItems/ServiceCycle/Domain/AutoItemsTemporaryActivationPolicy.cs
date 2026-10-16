using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal enum AutoItemsTemporaryActivationState
{
    None = 0,
    AwaitingActivation = 1,
    Active = 2,
    Completed = 3,
    Quarantined = 4,
}

internal enum AutoItemsTemporaryQuarantineCause
{
    None = 0,
    MultipleUsages = 1,
    PrematureExpiry = 2,
    MissingEngagementEvidence = 3,
}

internal readonly struct AutoItemsTemporaryActivationObservation
{
    internal AutoItemsTemporaryActivationObservation(
        AutoItemsTemporaryActivationState state,
        Guid itemId,
        AutoItemsTemporaryQuarantineCause quarantineCause)
    {
        State = state;
        ItemId = itemId;
        QuarantineCause = quarantineCause;
    }

    internal AutoItemsTemporaryActivationState State { get; }
    internal Guid ItemId { get; }
    internal AutoItemsTemporaryQuarantineCause QuarantineCause { get; }
}

/// <summary>
/// Reconciles one committed temporary ConsumableUse with later immutable world publications.
/// No timer participates: disappearance is evidence only after a later collected frame.
/// </summary>
internal static class AutoItemsTemporaryActivationPolicy
{
    internal static void ReconcileReceipt(
        in BatchReceipt receipt,
        ref AutoItemsCycleState state)
    {
        if (!state.HasPendingReceipt || !receipt.IsPresent) return;
        var planned = state.PendingReceiptAction;
        state.ClearPendingReceipt();
        if (planned.Family == AutoItemsConsumableFamily.Scroll &&
            receipt.ResultCode == AutoItemsActionResultCodes.TargetUnavailable)
        {
            state.RememberTargetlessScroll(planned.ItemId);
            return;
        }
        if (receipt.ActionCount == 1 &&
            receipt.CommittedCount == 1 &&
            AutoItemsConsumableFamilies.IsTemporary(planned.Family))
        {
            state.RecordSubmittedTemporary(in planned);
        }
    }

    internal static AutoItemsTemporaryActivationObservation Observe(
        GameWorldState world,
        ref AutoItemsCycleState state)
    {
        if (world is null) throw new ArgumentNullException(nameof(world));
        var itemId = state.PendingTemporaryItem;
        if (itemId == Guid.Empty)
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.None,
                Guid.Empty,
                AutoItemsTemporaryQuarantineCause.None);

        var usageCount = 0;
        var engaged = false;
        var expired = false;
        if (WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                itemId,
                out var start,
                out var count))
        {
            usageCount = count;
            for (var index = 0; index < count; index++)
            {
                var usage = world.ConsumableUsages[start + index];
                engaged |= usage.Engaged;
                expired |= usage.Expired;
            }
        }

        if (usageCount > 1)
            return Quarantine(
                ref state,
                AutoItemsTemporaryQuarantineCause.MultipleUsages);
        if (expired)
            return Quarantine(
                ref state,
                AutoItemsTemporaryQuarantineCause.PrematureExpiry);
        if (usageCount == 1)
        {
            if (engaged)
            {
                state.MarkTemporaryActivationSeen();
                return new AutoItemsTemporaryActivationObservation(
                    AutoItemsTemporaryActivationState.Active,
                    itemId,
                    AutoItemsTemporaryQuarantineCause.None);
            }
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.AwaitingActivation,
                itemId,
                AutoItemsTemporaryQuarantineCause.None);
        }

        if (world.CollectedAtFrame <= state.TemporarySubmittedFromFrame)
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.AwaitingActivation,
                itemId,
                AutoItemsTemporaryQuarantineCause.None);
        if (WorldLookup.TryFind(world.Consumables, itemId, out var consumable) &&
            (consumable.QueuedQuantity > 0 ||
             consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) > 0))
        {
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.AwaitingActivation,
                itemId,
                AutoItemsTemporaryQuarantineCause.None);
        }

        if (!state.TemporaryActivationSeen)
            return Quarantine(
                ref state,
                AutoItemsTemporaryQuarantineCause.MissingEngagementEvidence);

        state.ClearPendingTemporary();
        return new AutoItemsTemporaryActivationObservation(
            AutoItemsTemporaryActivationState.Completed,
            itemId,
            AutoItemsTemporaryQuarantineCause.None);
    }

    private static AutoItemsTemporaryActivationObservation Quarantine(
        ref AutoItemsCycleState state,
        AutoItemsTemporaryQuarantineCause cause)
    {
        var itemId = state.PendingTemporaryItem;
        state.QuarantinePendingTemporary(cause);
        return new AutoItemsTemporaryActivationObservation(
            AutoItemsTemporaryActivationState.Quarantined,
            itemId,
            cause);
    }
}

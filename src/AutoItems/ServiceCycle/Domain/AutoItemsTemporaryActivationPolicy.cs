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
    Paused = 4,
}

internal enum AutoItemsTemporaryPauseCause
{
    None = 0,
    MultipleUsages = 1,
    MissingEngagementEvidence = 2,
}

internal readonly struct AutoItemsTemporaryActivationObservation
{
    internal AutoItemsTemporaryActivationObservation(
        AutoItemsTemporaryActivationState state,
        Guid itemId,
        AutoItemsTemporaryPauseCause pauseCause)
    {
        State = state;
        ItemId = itemId;
        PauseCause = pauseCause;
    }

    internal AutoItemsTemporaryActivationState State { get; }
    internal Guid ItemId { get; }
    internal AutoItemsTemporaryPauseCause PauseCause { get; }
}

/// <summary>
/// Reconciles one committed temporary ConsumableUse with later immutable world publications.
/// No timer participates: disappearance is evidence only after a later collected frame.
/// </summary>
/// <remarks>
/// A usage Auto Items did not create pauses that item while any usage of it remains, then the
/// item resumes. The player owns those usages; the pause is never a lifecycle latch.
/// </remarks>
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
        var pausedItem = state.PausedTemporaryItem;
        if (pausedItem != Guid.Empty)
        {
            if (WorldConsumableUsageLookup.TryFindRange(world.ConsumableUsages, pausedItem, out _, out _))
                return new AutoItemsTemporaryActivationObservation(
                    AutoItemsTemporaryActivationState.Paused,
                    pausedItem,
                    state.TemporaryPauseCause);
            state.ResumePausedTemporary();
        }

        var itemId = state.PendingTemporaryItem;
        if (itemId == Guid.Empty)
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.None,
                Guid.Empty,
                AutoItemsTemporaryPauseCause.None);

        var usageCount = 0;
        var engaged = false;
        if (WorldConsumableUsageLookup.TryFindRange(
                world.ConsumableUsages,
                itemId,
                out var start,
                out var count))
        {
            usageCount = count;
            for (var index = 0; index < count; index++)
                engaged |= world.ConsumableUsages[start + index].Engaged;
        }

        if (usageCount > 1)
            return Pause(
                ref state,
                AutoItemsTemporaryPauseCause.MultipleUsages);
        if (usageCount == 1)
        {
            if (engaged)
            {
                state.MarkTemporaryActivationSeen();
                return new AutoItemsTemporaryActivationObservation(
                    AutoItemsTemporaryActivationState.Active,
                    itemId,
                    AutoItemsTemporaryPauseCause.None);
            }
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.AwaitingActivation,
                itemId,
                AutoItemsTemporaryPauseCause.None);
        }

        if (world.CollectedAtFrame <= state.TemporarySubmittedFromFrame)
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.AwaitingActivation,
                itemId,
                AutoItemsTemporaryPauseCause.None);
        if (WorldLookup.TryFind(world.Consumables, itemId, out var consumable) &&
            (consumable.QueuedQuantity > 0 ||
             consumable.CurrentPrepTime.CompareTo(BigDouble.Zero) > 0))
        {
            return new AutoItemsTemporaryActivationObservation(
                AutoItemsTemporaryActivationState.AwaitingActivation,
                itemId,
                AutoItemsTemporaryPauseCause.None);
        }

        if (!state.TemporaryActivationSeen)
            return Pause(
                ref state,
                AutoItemsTemporaryPauseCause.MissingEngagementEvidence);

        state.ClearPendingTemporary();
        return new AutoItemsTemporaryActivationObservation(
            AutoItemsTemporaryActivationState.Completed,
            itemId,
            AutoItemsTemporaryPauseCause.None);
    }

    private static AutoItemsTemporaryActivationObservation Pause(
        ref AutoItemsCycleState state,
        AutoItemsTemporaryPauseCause cause)
    {
        var itemId = state.PendingTemporaryItem;
        state.PausePendingTemporary(cause);
        return new AutoItemsTemporaryActivationObservation(
            AutoItemsTemporaryActivationState.Paused,
            itemId,
            cause);
    }
}

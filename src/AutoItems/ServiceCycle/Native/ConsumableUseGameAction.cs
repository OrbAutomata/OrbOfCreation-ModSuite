using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// One native use transaction for automation and player presses. Admission belongs to the caller;
/// the multi-buy scope, press and exact preparation-queue sentinel belong here.
/// </summary>
internal abstract class ConsumableUseGameAction<TBindings> : NativeGameAction<TBindings>
    where TBindings : ConsumableUseNativeBindings
{
    protected ConsumableUseGameAction(
        string feature,
        GameActionCodes codes,
        Func<long> readLifecycleEpoch,
        Func<bool> tryPermit,
        Func<string> readOwnershipFailure,
        Bind bind)
        : base(feature, "Inventory", codes, readLifecycleEpoch, tryPermit, readOwnershipFailure, bind)
    {
    }

    protected NativeActionOutcome UseConsumable(
        Guid itemId,
        object item,
        TBindings native,
        ServiceActionResultCode multiBuyUnavailable,
        bool randomize = false,
        bool multiBuyFailureIsFault = false)
    {
        if (!TryPermit(out var refusal)) return refusal;
        if (!NativeMultiBuyScope.TryEnterOne(out var scope, out var reason))
            return multiBuyFailureIsFault
                ? NativeActionOutcome.FaultBeforeCall(multiBuyUnavailable, reason)
                : NativeActionOutcome.Reject(multiBuyUnavailable, reason);

        using (scope)
        {
            var before = native.GetQueued(item);
            return Execute(EntityIdentityFormatter.PlayerName(itemId),
                "The exact consumable entered the native preparation queue.", before,
                () => native.GetQueued(item),
                () =>
                {
                    try
                    {
                        if (randomize)
                        {
                            native.SetRandomization(item, true);
                            if (!native.IsRandomized(item))
                                throw new InvalidOperationException(
                                    "ConsumableSO.SetRandomization(true) was not confirmed by IsRandomized().");
                        }
                        native.SelectAndFire(item);
                    }
                    finally
                    {
                        // Classify the queue after cleanup, even when restoring multi-buy throws.
                        scope.Dispose();
                    }
                },
                static (queued, after) => after == checked(queued + 1),
                nativeCallsAttempted: randomize ? 2 : 1);
        }
    }
}

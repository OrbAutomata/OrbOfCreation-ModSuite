using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Lifecycle-scoped Unity-main-thread boundary for persistent reset.</summary>
internal sealed class PrestigeGameAction : NativeGameAction<PrestigeNativeBindings>
{
    private const string Screen = "Time > Reset";

    private static readonly GameActionCodes Codes = new(
        PrestigeActionResultCodes.WrongThread,
        PrestigeActionResultCodes.ContractUnavailable,
        PrestigeActionResultCodes.MutationPermitUnavailable,
        PrestigeActionResultCodes.PostCommitFault,
        PrestigeActionResultCodes.VerificationFailed);

    internal PrestigeGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null)
        : base("Prestige", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out PrestigeNativeBindings? bindings, out string reason) =>
                PrestigeNativeBindings.TryCreate(out bindings, out reason, resolveType, includeContract))
    {
    }

    internal NativeActionOutcome Submit(in PrestigeAction action)
    {
        var epoch = action.LifecycleEpoch;
        return Admit(epoch, native => Press(native, epoch));
    }

    private NativeActionOutcome Press(PrestigeNativeBindings native, long epoch)
    {
        var manager = native.Manager();
        if (manager is null || manager.GetType() != native.ManagerType)
            return NativeActionOutcome.Reject(PrestigeActionResultCodes.ContractUnavailable,
                "The reset is not available from here — return to a loaded save and open " +
                "Time > Reset.");
        var complete = native.CycleComplete(manager);
        var fetched = native.ChallengesFetched(manager);
        if (complete is null || fetched is null)
            return NativeActionOutcome.Reject(PrestigeActionResultCodes.ContractUnavailable,
                "The game did not answer whether a reset is allowed right now; open " +
                "Time > Reset and read it again.");
        if (!native.GetBool(complete))
            return NativeActionOutcome.Reject(PrestigeActionResultCodes.WorldCycleIncomplete,
                "Persistent reset is unavailable until the current world cycle is complete.");
        if (!native.GetBool(fetched))
            return NativeActionOutcome.Reject(PrestigeActionResultCodes.ChallengesNotFetched,
                "Persistent reset is unavailable until prestige challenges have been fetched and chosen.");
        if (!TryPermit(out var refusal)) return refusal;
        // PersistentReset() only schedules this transaction behind UIScreenFlash.FadeIn.
        // Tooling has no rendered-modal dependency, so invoke the exact transaction directly.
        return Execute("the persistent reset", "The persistent-reset lifecycle transition is visible.",
            epoch, ReadLifecycleEpoch, () => native.Reset(manager),
            (before, after) => after == checked(before + 1));
    }
}

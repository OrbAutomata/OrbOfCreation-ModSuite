using System;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>The sole mutation boundary for the current native targeting request.</summary>
internal sealed class TargetingGameAction : NativeGameAction<TargetingNativeBindings>
{
    private const string Screen = "Magic > Casting";

    private const string Submitted =
        "The exact target was assigned and its original request left the queue.";

    private static readonly GameActionCodes Codes = new(
        TargetingActionResultCodes.WrongThread,
        TargetingActionResultCodes.ContractUnavailable,
        TargetingActionResultCodes.MutationPermitUnavailable,
        TargetingActionResultCodes.PostCommitFault,
        TargetingActionResultCodes.VerificationFailed);

    internal TargetingGameAction(Func<long> readLifecycleEpoch, Func<bool> tryCaptureMutationPermit,
        Func<string> readOwnershipFailure, Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
        : base("Targeting", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out TargetingNativeBindings? bindings, out string reason) =>
                TargetingNativeBindings.TryCreate(resolveType ?? ReflectionUtil.FindLoadedType,
                    includeContract ?? (_ => true), out bindings, out reason))
    {
    }

    internal TargetingSubmission Submit(in TargetingAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new TargetingSubmission(outcome, Guid.Empty),
            native => Press(in request, native));
    }

    private TargetingSubmission Press(in TargetingAction action, TargetingNativeBindings native)
    {
        if (!native.IsTargeting())
            return TargetingSubmission.Reject(TargetingActionResultCodes.NoPendingRequest,
                "The game has no pending target request.");
        var link = native.GetLink();
        if (link is null || link.GetType() != native.LinkType)
            return TargetingSubmission.Reject(TargetingActionResultCodes.NoPendingRequest,
                "The game is not showing a targeting request right now.");
        return action.Kind switch
        {
            TargetingActionKind.Submit => SubmitTarget(in action, native, link),
            TargetingActionKind.Randomize => Randomize(native, link),
            TargetingActionKind.Cancel => Cancel(native, link),
            _ => TargetingSubmission.Reject(TargetingActionResultCodes.ContractUnavailable, "Unknown targeting mode."),
        };
    }

    private TargetingSubmission SubmitTarget(in TargetingAction action,
        TargetingNativeBindings native, object link)
    {
        var candidates = native.GetAllTargets(link);
        object? candidate = null;
        for (var index = 0; index < candidates.Count; index++)
        {
            var item = candidates[index];
            if (item is not null && item.GetType() == native.StructureType &&
                native.GetGuid(item) == action.TargetId)
            {
                if (candidate is not null)
                    return TargetingSubmission.Reject(TargetingActionResultCodes.TargetUnavailable,
                        "Target UUID is ambiguous in the live eligible-target set.");
                candidate = item;
            }
        }
        if (candidate is null)
            return TargetingSubmission.Reject(TargetingActionResultCodes.TargetUnavailable,
                "Target UUID is absent from the live eligible-target set.");
        if (!native.CheckTarget(link, candidate))
            return TargetingSubmission.Reject(TargetingActionResultCodes.NativeTargetRefused,
                "The game refuses this target.");
        if (!TryPermit(out var refusal)) return new TargetingSubmission(refusal, Guid.Empty);
        var target = candidate;
        var outcome = Execute(EntityIdentityFormatter.PlayerName(action.TargetId), Submitted, false,
            () => SubmittedTo(native, link, target),
            () => native.SubmitTarget(target),
            static (_, landed) => landed);
        return new TargetingSubmission(outcome, outcome.Verified ? action.TargetId : Guid.Empty);
    }

    /// <summary>
    /// The screen's randomize button: the game picks one target, and that pick is submitted as the
    /// player's choice in the same press.
    /// </summary>
    private TargetingSubmission Randomize(TargetingNativeBindings native, object link)
    {
        if (native.GetAllTargets(link).Count == 0)
            return TargetingSubmission.Reject(TargetingActionResultCodes.TargetUnavailable,
                "This targeting request is offering no target to pick.");
        if (!TryPermit(out var refusal)) return new TargetingSubmission(refusal, Guid.Empty);
        object? picked = null;
        var pickedId = Guid.Empty;
        var outcome = Execute(nameof(TargetingActionKind.Randomize), Submitted, false,
            () => picked is not null && SubmittedTo(native, link, picked),
            () =>
            {
                var candidate = native.GetRandom(link);
                if (candidate is null || candidate.GetType() != native.StructureType)
                    return NativeStop.Fault(TargetingActionResultCodes.VerificationFailed,
                        "The game's randomize button did not settle on one target; pick a target " +
                        "by id instead.");
                if (!native.CheckTarget(link, candidate))
                    return NativeStop.Fault(TargetingActionResultCodes.VerificationFailed,
                        "The game's randomize button picked a target it then refused; pick a " +
                        "target by id instead.");
                pickedId = native.GetGuid(candidate);
                picked = candidate;
                native.SubmitTarget(candidate);
                return null;
            },
            static (_, landed) => landed);
        return new TargetingSubmission(outcome, outcome.Verified ? pickedId : Guid.Empty);
    }

    private TargetingSubmission Cancel(TargetingNativeBindings native, object link)
    {
        var resultInfo = native.ReadResultInfo(link);
        if (resultInfo is null)
            return TargetingSubmission.Reject(TargetingActionResultCodes.CancelUnavailable,
                "This targeting request has nothing to cancel.");
        if (!TryPermit(out var refusal)) return new TargetingSubmission(refusal, Guid.Empty);
        return new TargetingSubmission(Execute(nameof(TargetingActionKind.Cancel),
            "The cast was cancelled and its request left the action queue.", false,
            () => native.IsCancelled(resultInfo) && OriginalRequestGone(native, link),
            () => native.Cancel(resultInfo),
            static (_, landed) => landed), Guid.Empty);
    }

    private static bool SubmittedTo(TargetingNativeBindings native, object link, object candidate) =>
        native.HasTarget(link) && ReferenceEquals(native.ReadTarget(link), candidate) &&
        OriginalRequestGone(native, link);

    private static bool OriginalRequestGone(TargetingNativeBindings native, object link) =>
        !native.IsTargeting() || !ReferenceEquals(native.GetLink(), link);
}

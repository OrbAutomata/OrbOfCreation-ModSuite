using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class RitualLifecycleActionResultMapper
{
    internal static ServiceActionResult Map(in RitualLifecycleSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified, submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code,
                ServiceNativeMutationEvidence.Observed(
                    submission.Outcome, submission.CallOutcome));
    }

    internal static ServiceActionResultCode Code(RitualLifecyclePreflight preflight) =>
        preflight switch
        {
            RitualLifecyclePreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            RitualLifecyclePreflight.ContractUnavailable => RitualLifecycleActionResultCodes.ContractUnavailable,
            RitualLifecyclePreflight.WrongThread => RitualLifecycleActionResultCodes.WrongThread,
            RitualLifecyclePreflight.IdentityUnavailable => RitualLifecycleActionResultCodes.IdentityUnavailable,
            RitualLifecyclePreflight.NotDiscovered => RitualLifecycleActionResultCodes.NotDiscovered,
            RitualLifecyclePreflight.AlreadyInRequestedState => RitualLifecycleActionResultCodes.AlreadyInRequestedState,
            RitualLifecyclePreflight.LevelLocked => RitualLifecycleActionResultCodes.LevelLocked,
            RitualLifecyclePreflight.LevelOutOfRange => RitualLifecycleActionResultCodes.LevelOutOfRange,
            RitualLifecyclePreflight.BattleAlreadyActive => RitualLifecycleActionResultCodes.BattleAlreadyActive,
            RitualLifecyclePreflight.NoBattleActive => RitualLifecycleActionResultCodes.NoBattleActive,
            RitualLifecyclePreflight.WrongActiveRitual => RitualLifecycleActionResultCodes.WrongActiveRitual,
            RitualLifecyclePreflight.Unaffordable => RitualLifecycleActionResultCodes.Unaffordable,
            RitualLifecyclePreflight.NoDurationEffect => RitualLifecycleActionResultCodes.NoDurationEffect,
            RitualLifecyclePreflight.MutationPermitUnavailable => RitualLifecycleActionResultCodes.MutationPermitUnavailable,
            RitualLifecyclePreflight.PostCommitFault => RitualLifecycleActionResultCodes.PostCommitFault,
            RitualLifecyclePreflight.VerificationFailed => RitualLifecycleActionResultCodes.VerificationFailed,
            _ => RitualLifecycleActionResultCodes.ContractUnavailable,
        };
}

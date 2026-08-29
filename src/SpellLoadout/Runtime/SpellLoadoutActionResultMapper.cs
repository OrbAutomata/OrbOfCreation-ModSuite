using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellLoadoutActionResultMapper
{
    internal static ServiceActionResult Map(in SpellLoadoutSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(
                code,
                ServiceNativeMutationEvidence.Observed(
                    submission.Outcome,
                    submission.CallOutcome));
    }

    internal static ServiceActionResultCode Code(SpellLoadoutPreflight preflight) => preflight switch
    {
        SpellLoadoutPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        SpellLoadoutPreflight.ContractUnavailable => SpellLoadoutActionResultCodes.ContractUnavailable,
        SpellLoadoutPreflight.WrongThread => SpellLoadoutActionResultCodes.WrongThread,
        SpellLoadoutPreflight.IdentityUnavailable => SpellLoadoutActionResultCodes.IdentityUnavailable,
        SpellLoadoutPreflight.SpellRecharging => SpellLoadoutActionResultCodes.SpellRecharging,
        SpellLoadoutPreflight.CastInProgress => SpellLoadoutActionResultCodes.CastInProgress,
        SpellLoadoutPreflight.ScreenLocked => SpellLoadoutActionResultCodes.ScreenLocked,
        SpellLoadoutPreflight.DestinationOutOfRange => SpellLoadoutActionResultCodes.DestinationOutOfRange,
        SpellLoadoutPreflight.AlreadyInRequestedState => SpellLoadoutActionResultCodes.AlreadyInRequestedState,
        SpellLoadoutPreflight.MutationPermitUnavailable => SpellLoadoutActionResultCodes.MutationPermitUnavailable,
        SpellLoadoutPreflight.PostCommitFault => SpellLoadoutActionResultCodes.PostCommitFault,
        SpellLoadoutPreflight.VerificationFailed => SpellLoadoutActionResultCodes.VerificationFailed,
        _ => SpellLoadoutActionResultCodes.ContractUnavailable,
    };
}

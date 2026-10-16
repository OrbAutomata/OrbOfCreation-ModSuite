using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellCompositionActionResultMapper
{
    internal static ServiceActionResult Map(in SpellCompositionSubmission submission)
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

    internal static ServiceActionResultCode Code(SpellCompositionPreflight preflight) => preflight switch
    {
        SpellCompositionPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        SpellCompositionPreflight.ContractUnavailable => SpellCompositionActionResultCodes.ContractUnavailable,
        SpellCompositionPreflight.WrongThread => SpellCompositionActionResultCodes.WrongThread,
        SpellCompositionPreflight.LevelOutOfRange => SpellCompositionActionResultCodes.LevelOutOfRange,
        SpellCompositionPreflight.AlreadyInRequestedState => SpellCompositionActionResultCodes.AlreadyInRequestedState,
        SpellCompositionPreflight.MutationPermitUnavailable => SpellCompositionActionResultCodes.MutationPermitUnavailable,
        SpellCompositionPreflight.PostCommitFault => SpellCompositionActionResultCodes.PostCommitFault,
        SpellCompositionPreflight.VerificationFailed => SpellCompositionActionResultCodes.VerificationFailed,
        _ => SpellCompositionActionResultCodes.ContractUnavailable,
    };
}

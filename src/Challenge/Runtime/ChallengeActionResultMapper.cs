using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ChallengeActionResultMapper
{
    internal static ServiceActionResult Map(in ChallengeSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified,
                    submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code,
                ServiceNativeMutationEvidence.Observed(submission.Outcome,
                    submission.CallOutcome));
    }

    private static ServiceActionResultCode Code(ChallengePreflight preflight) => preflight switch
    {
        ChallengePreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        ChallengePreflight.ContractUnavailable => ChallengeActionResultCodes.ContractUnavailable,
        ChallengePreflight.WrongThread => ChallengeActionResultCodes.WrongThread,
        ChallengePreflight.IdentityUnavailable => ChallengeActionResultCodes.IdentityUnavailable,
        ChallengePreflight.OfferUnavailable => ChallengeActionResultCodes.OfferUnavailable,
        ChallengePreflight.SelectionFull => ChallengeActionResultCodes.SelectionFull,
        ChallengePreflight.SelectionRestricted => ChallengeActionResultCodes.SelectionRestricted,
        ChallengePreflight.InvalidState => ChallengeActionResultCodes.InvalidState,
        ChallengePreflight.FetchUnavailable => ChallengeActionResultCodes.FetchUnavailable,
        ChallengePreflight.NoRerolls => ChallengeActionResultCodes.NoRerolls,
        ChallengePreflight.MutationPermitUnavailable => ChallengeActionResultCodes.MutationPermitUnavailable,
        ChallengePreflight.PostCommitFault => ChallengeActionResultCodes.PostCommitFault,
        ChallengePreflight.VerificationFailed => ChallengeActionResultCodes.VerificationFailed,
        _ => ChallengeActionResultCodes.ContractUnavailable,
    };
}

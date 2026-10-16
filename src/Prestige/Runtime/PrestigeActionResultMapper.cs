using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class PrestigeActionResultMapper
{
    internal static ServiceActionResult Map(in PrestigeSubmission submission)
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

    private static ServiceActionResultCode Code(PrestigePreflight preflight) => preflight switch
    {
        PrestigePreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        PrestigePreflight.ContractUnavailable => PrestigeActionResultCodes.ContractUnavailable,
        PrestigePreflight.WrongThread => PrestigeActionResultCodes.WrongThread,
        PrestigePreflight.WorldCycleIncomplete => PrestigeActionResultCodes.WorldCycleIncomplete,
        PrestigePreflight.ChallengesNotFetched => PrestigeActionResultCodes.ChallengesNotFetched,
        PrestigePreflight.MutationPermitUnavailable => PrestigeActionResultCodes.MutationPermitUnavailable,
        PrestigePreflight.PostCommitFault => PrestigeActionResultCodes.PostCommitFault,
        PrestigePreflight.VerificationFailed => PrestigeActionResultCodes.VerificationFailed,
        _ => PrestigeActionResultCodes.ContractUnavailable,
    };
}

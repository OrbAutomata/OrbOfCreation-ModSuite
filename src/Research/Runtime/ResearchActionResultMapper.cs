using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ResearchActionResultMapper
{
    internal static ServiceActionResult Map(in ResearchSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified,
                    submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code,
                ServiceNativeMutationEvidence.Observed(submission.Outcome, submission.CallOutcome));
    }

    private static ServiceActionResultCode Code(ResearchPreflight preflight) => preflight switch
    {
        ResearchPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        ResearchPreflight.ContractUnavailable => ResearchActionResultCodes.ContractUnavailable,
        ResearchPreflight.WrongThread => ResearchActionResultCodes.WrongThread,
        ResearchPreflight.IdentityUnavailable => ResearchActionResultCodes.IdentityUnavailable,
        ResearchPreflight.DevelopUnavailable => ResearchActionResultCodes.DevelopUnavailable,
        ResearchPreflight.MultiBuyUnavailable => ResearchActionResultCodes.MultiBuyUnavailable,
        ResearchPreflight.InvalidMode => ResearchActionResultCodes.InvalidMode,
        ResearchPreflight.InvalidState => ResearchActionResultCodes.InvalidState,
        ResearchPreflight.BonusUnavailable => ResearchActionResultCodes.BonusUnavailable,
        ResearchPreflight.MutationPermitUnavailable => ResearchActionResultCodes.MutationPermitUnavailable,
        ResearchPreflight.PostCommitFault => ResearchActionResultCodes.PostCommitFault,
        ResearchPreflight.VerificationFailed => ResearchActionResultCodes.VerificationFailed,
        ResearchPreflight.AmountUnavailable => ResearchActionResultCodes.AmountUnavailable,
        ResearchPreflight.AlreadyMaxed => ResearchActionResultCodes.AlreadyMaxed,
        ResearchPreflight.Unaffordable => ResearchActionResultCodes.Unaffordable,
        ResearchPreflight.RequirementsUnmet => ResearchActionResultCodes.RequirementsUnmet,
        ResearchPreflight.LeewayExhausted => ResearchActionResultCodes.LeewayExhausted,
        ResearchPreflight.AlreadyDeveloping => ResearchActionResultCodes.AlreadyDeveloping,
        _ => ResearchActionResultCodes.ContractUnavailable,
    };
}

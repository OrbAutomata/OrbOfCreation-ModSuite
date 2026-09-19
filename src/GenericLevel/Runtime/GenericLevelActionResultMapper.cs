using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class GenericLevelActionResultMapper
{
    internal static ServiceActionResult Map(in GenericLevelSubmission submission)
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

    internal static ServiceActionResultCode Code(GenericLevelPreflight preflight) => preflight switch
    {
        GenericLevelPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        GenericLevelPreflight.ContractUnavailable => GenericLevelActionResultCodes.ContractUnavailable,
        GenericLevelPreflight.WrongThread => GenericLevelActionResultCodes.WrongThread,
        GenericLevelPreflight.IdentityUnavailable => GenericLevelActionResultCodes.IdentityUnavailable,
        GenericLevelPreflight.WrongDomain => GenericLevelActionResultCodes.WrongDomain,
        GenericLevelPreflight.Undiscovered => GenericLevelActionResultCodes.Undiscovered,
        GenericLevelPreflight.Hidden => GenericLevelActionResultCodes.Hidden,
        GenericLevelPreflight.Unavailable => GenericLevelActionResultCodes.Unavailable,
        GenericLevelPreflight.CannotLevel => GenericLevelActionResultCodes.CannotLevel,
        GenericLevelPreflight.BonusUnavailable => GenericLevelActionResultCodes.BonusUnavailable,
        GenericLevelPreflight.ResourcesHidden => GenericLevelActionResultCodes.ResourcesHidden,
        GenericLevelPreflight.Unaffordable => GenericLevelActionResultCodes.Unaffordable,
        GenericLevelPreflight.MutationPermitUnavailable => GenericLevelActionResultCodes.MutationPermitUnavailable,
        GenericLevelPreflight.PostCommitFault => GenericLevelActionResultCodes.PostCommitFault,
        GenericLevelPreflight.VerificationFailed => GenericLevelActionResultCodes.VerificationFailed,
        _ => GenericLevelActionResultCodes.ContractUnavailable,
    };
}

using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class TargetingActionResultMapper
{
    internal static ServiceActionResult Map(in TargetingSubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(NativeMutationOutcome.Verified, submission.CallOutcome));
        var code = Code(submission.Preflight);
        return submission.CallOutcome.MutationAttempts == 0
            ? ServiceActionResult.Rejected(code)
            : ServiceActionResult.Faulted(code,
                ServiceNativeMutationEvidence.Observed(submission.Outcome, submission.CallOutcome));
    }
    internal static ServiceActionResultCode Code(TargetingPreflight preflight) => preflight switch
    {
        TargetingPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        TargetingPreflight.ContractUnavailable => TargetingActionResultCodes.ContractUnavailable,
        TargetingPreflight.WrongThread => TargetingActionResultCodes.WrongThread,
        TargetingPreflight.NoPendingRequest => TargetingActionResultCodes.NoPendingRequest,
        TargetingPreflight.TargetUnavailable => TargetingActionResultCodes.TargetUnavailable,
        TargetingPreflight.NativeTargetRefused => TargetingActionResultCodes.NativeTargetRefused,
        TargetingPreflight.CancelUnavailable => TargetingActionResultCodes.CancelUnavailable,
        TargetingPreflight.MutationPermitUnavailable => TargetingActionResultCodes.MutationPermitUnavailable,
        TargetingPreflight.PostCommitFault => TargetingActionResultCodes.PostCommitFault,
        TargetingPreflight.VerificationFailed => TargetingActionResultCodes.VerificationFailed,
        _ => TargetingActionResultCodes.ContractUnavailable,
    };
}

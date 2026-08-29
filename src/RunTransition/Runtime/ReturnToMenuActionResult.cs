using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ReturnToMenuActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2086);
    internal static readonly ServiceActionResultCode WrongThread = new(2087);
    internal static readonly ServiceActionResultCode WrongScene = new(2088);
    internal static readonly ServiceActionResultCode TransitionInProgress = new(2089);
    internal static readonly ServiceActionResultCode ControlUnavailable = new(2090);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2091);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2092);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2093);
}

internal static class ReturnToMenuActionResultMapper
{
    internal static ServiceActionResult Map(in ReturnToMenuSubmission submission)
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

    internal static ServiceActionResultCode Code(ReturnToMenuPreflight preflight) =>
        preflight switch
        {
            ReturnToMenuPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            ReturnToMenuPreflight.ContractUnavailable => ReturnToMenuActionResultCodes.ContractUnavailable,
            ReturnToMenuPreflight.WrongThread => ReturnToMenuActionResultCodes.WrongThread,
            ReturnToMenuPreflight.WrongScene => ReturnToMenuActionResultCodes.WrongScene,
            ReturnToMenuPreflight.TransitionInProgress => ReturnToMenuActionResultCodes.TransitionInProgress,
            ReturnToMenuPreflight.ControlUnavailable => ReturnToMenuActionResultCodes.ControlUnavailable,
            ReturnToMenuPreflight.MutationPermitUnavailable => ReturnToMenuActionResultCodes.MutationPermitUnavailable,
            ReturnToMenuPreflight.PostCommitFault => ReturnToMenuActionResultCodes.PostCommitFault,
            ReturnToMenuPreflight.VerificationFailed => ReturnToMenuActionResultCodes.VerificationFailed,
            _ => ReturnToMenuActionResultCodes.ContractUnavailable,
        };
}

using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class CraftingStationActionResultMapper
{
    internal static ServiceActionResult Map(in CraftingStationSubmission submission)
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

    internal static ServiceActionResultCode Code(CraftingStationPreflight preflight) =>
        preflight switch
        {
            CraftingStationPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            CraftingStationPreflight.ContractUnavailable => CraftingStationActionResultCodes.ContractUnavailable,
            CraftingStationPreflight.WrongThread => CraftingStationActionResultCodes.WrongThread,
            CraftingStationPreflight.IdentityUnavailable => CraftingStationActionResultCodes.IdentityUnavailable,
            CraftingStationPreflight.SelectionUnavailable => CraftingStationActionResultCodes.SelectionUnavailable,
            CraftingStationPreflight.SelectionHidden => CraftingStationActionResultCodes.SelectionHidden,
            CraftingStationPreflight.LevelOutOfRange => CraftingStationActionResultCodes.LevelOutOfRange,
            CraftingStationPreflight.NotLoaded => CraftingStationActionResultCodes.NotLoaded,
            CraftingStationPreflight.AlreadyInRequestedState => CraftingStationActionResultCodes.AlreadyInRequestedState,
            CraftingStationPreflight.MutationPermitUnavailable => CraftingStationActionResultCodes.MutationPermitUnavailable,
            CraftingStationPreflight.PostCommitFault => CraftingStationActionResultCodes.PostCommitFault,
            CraftingStationPreflight.VerificationFailed => CraftingStationActionResultCodes.VerificationFailed,
            _ => CraftingStationActionResultCodes.ContractUnavailable,
        };
}

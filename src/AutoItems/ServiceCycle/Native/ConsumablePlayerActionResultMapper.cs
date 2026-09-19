using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ConsumablePlayerActionResultMapper
{
    internal static ServiceActionResult Map(in ConsumablePlayerSubmission submission)
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

    internal static ServiceActionResultCode Code(ConsumablePlayerPreflight preflight) =>
        preflight switch
        {
            ConsumablePlayerPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            ConsumablePlayerPreflight.ContractUnavailable => ConsumablePlayerActionResultCodes.ContractUnavailable,
            ConsumablePlayerPreflight.WrongThread => ConsumablePlayerActionResultCodes.WrongThread,
            ConsumablePlayerPreflight.ItemUnavailable => ConsumablePlayerActionResultCodes.ItemUnavailable,
            ConsumablePlayerPreflight.NotVisible => ConsumablePlayerActionResultCodes.NotVisible,
            ConsumablePlayerPreflight.TargetingInProgress => ConsumablePlayerActionResultCodes.TargetingInProgress,
            ConsumablePlayerPreflight.InventoryBusy => ConsumablePlayerActionResultCodes.InventoryBusy,
            ConsumablePlayerPreflight.CanFireRefused => ConsumablePlayerActionResultCodes.CanFireRefused,
            ConsumablePlayerPreflight.NoCancellableUsage => ConsumablePlayerActionResultCodes.NoCancellableUsage,
            ConsumablePlayerPreflight.NothingToDiscard => ConsumablePlayerActionResultCodes.NothingToDiscard,
            ConsumablePlayerPreflight.RandomizationUnavailable => ConsumablePlayerActionResultCodes.RandomizationUnavailable,
            ConsumablePlayerPreflight.AlreadyInRequestedState => ConsumablePlayerActionResultCodes.AlreadyInRequestedState,
            ConsumablePlayerPreflight.ListUnavailable => ConsumablePlayerActionResultCodes.ListUnavailable,
            ConsumablePlayerPreflight.SourceUnavailable => ConsumablePlayerActionResultCodes.SourceUnavailable,
            ConsumablePlayerPreflight.DestinationOutOfRange => ConsumablePlayerActionResultCodes.DestinationOutOfRange,
            ConsumablePlayerPreflight.MutationPermitUnavailable => ConsumablePlayerActionResultCodes.MutationPermitUnavailable,
            ConsumablePlayerPreflight.MultiBuyUnavailable => ConsumablePlayerActionResultCodes.MultiBuyUnavailable,
            ConsumablePlayerPreflight.PostCommitFault => ConsumablePlayerActionResultCodes.PostCommitFault,
            ConsumablePlayerPreflight.VerificationFailed => ConsumablePlayerActionResultCodes.VerificationFailed,
            _ => ConsumablePlayerActionResultCodes.ContractUnavailable,
        };
}

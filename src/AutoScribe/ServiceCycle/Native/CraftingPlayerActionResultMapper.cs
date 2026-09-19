using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class CraftingPlayerActionResultMapper
{
    internal static ServiceActionResult Map(in CraftingPlayerSubmission submission)
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

    internal static ServiceActionResultCode Code(CraftingPlayerPreflight preflight) =>
        preflight switch
        {
            CraftingPlayerPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            CraftingPlayerPreflight.ContractUnavailable => CraftingPlayerActionResultCodes.ContractUnavailable,
            CraftingPlayerPreflight.WrongThread => CraftingPlayerActionResultCodes.WrongThread,
            CraftingPlayerPreflight.RecipeUnavailable => CraftingPlayerActionResultCodes.RecipeUnavailable,
            CraftingPlayerPreflight.NotVisible => CraftingPlayerActionResultCodes.NotVisible,
            CraftingPlayerPreflight.PageRelationAmbiguous => CraftingPlayerActionResultCodes.PageRelationAmbiguous,
            CraftingPlayerPreflight.InvalidPurchaseAmount => CraftingPlayerActionResultCodes.InvalidPurchaseAmount,
            CraftingPlayerPreflight.QueueFull => CraftingPlayerActionResultCodes.QueueFull,
            CraftingPlayerPreflight.Unaffordable => CraftingPlayerActionResultCodes.Unaffordable,
            CraftingPlayerPreflight.MutationPermitUnavailable => CraftingPlayerActionResultCodes.MutationPermitUnavailable,
            CraftingPlayerPreflight.PostCommitFault => CraftingPlayerActionResultCodes.PostCommitFault,
            CraftingPlayerPreflight.VerificationFailed => CraftingPlayerActionResultCodes.VerificationFailed,
            _ => CraftingPlayerActionResultCodes.ContractUnavailable,
        };
}

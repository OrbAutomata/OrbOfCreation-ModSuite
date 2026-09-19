using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class CraftingInstanceLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2020);
    internal static readonly ServiceActionResultCode WrongThread = new(2021);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2022);
    internal static readonly ServiceActionResultCode NotVisible = new(2023);
    internal static readonly ServiceActionResultCode PageRelationAmbiguous = new(2024);
    internal static readonly ServiceActionResultCode InstanceUnavailable = new(2025);
    internal static readonly ServiceActionResultCode AutomationFull = new(2026);
    internal static readonly ServiceActionResultCode MultiBuyUnavailable = new(2027);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2028);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2029);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2030);
}

internal static class CraftingInstanceLifecycleActionResultMapper
{
    internal static ServiceActionResult Map(in CraftingInstanceLifecycleSubmission submission)
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

    internal static ServiceActionResultCode Code(CraftingInstanceLifecyclePreflight preflight) =>
        preflight switch
        {
            CraftingInstanceLifecyclePreflight.LifecycleReplaced =>
                CommonActionResultCodes.LifecycleReplaced,
            CraftingInstanceLifecyclePreflight.ContractUnavailable =>
                CraftingInstanceLifecycleActionResultCodes.ContractUnavailable,
            CraftingInstanceLifecyclePreflight.WrongThread =>
                CraftingInstanceLifecycleActionResultCodes.WrongThread,
            CraftingInstanceLifecyclePreflight.IdentityUnavailable =>
                CraftingInstanceLifecycleActionResultCodes.IdentityUnavailable,
            CraftingInstanceLifecyclePreflight.NotVisible =>
                CraftingInstanceLifecycleActionResultCodes.NotVisible,
            CraftingInstanceLifecyclePreflight.PageRelationAmbiguous =>
                CraftingInstanceLifecycleActionResultCodes.PageRelationAmbiguous,
            CraftingInstanceLifecyclePreflight.InstanceUnavailable =>
                CraftingInstanceLifecycleActionResultCodes.InstanceUnavailable,
            CraftingInstanceLifecyclePreflight.AutomationFull =>
                CraftingInstanceLifecycleActionResultCodes.AutomationFull,
            CraftingInstanceLifecyclePreflight.MultiBuyUnavailable =>
                CraftingInstanceLifecycleActionResultCodes.MultiBuyUnavailable,
            CraftingInstanceLifecyclePreflight.MutationPermitUnavailable =>
                CraftingInstanceLifecycleActionResultCodes.MutationPermitUnavailable,
            CraftingInstanceLifecyclePreflight.PostCommitFault =>
                CraftingInstanceLifecycleActionResultCodes.PostCommitFault,
            CraftingInstanceLifecyclePreflight.VerificationFailed =>
                CraftingInstanceLifecycleActionResultCodes.VerificationFailed,
            _ => CraftingInstanceLifecycleActionResultCodes.ContractUnavailable,
        };
}

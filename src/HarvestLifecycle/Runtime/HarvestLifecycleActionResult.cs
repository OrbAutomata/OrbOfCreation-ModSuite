using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class HarvestLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2056);
    internal static readonly ServiceActionResultCode WrongThread = new(2057);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2058);
    internal static readonly ServiceActionResultCode NotVisible = new(2059);
    internal static readonly ServiceActionResultCode ElementListFull = new(2060);
    internal static readonly ServiceActionResultCode ElementUsageUnavailable = new(2061);
    internal static readonly ServiceActionResultCode ActionUnavailable = new(2062);
    internal static readonly ServiceActionResultCode ActionListFull = new(2063);
    internal static readonly ServiceActionResultCode AmountUnavailable = new(2064);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2065);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2066);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2067);
    internal static readonly ServiceActionResultCode NotActive = new(2078);
}

internal static class HarvestLifecycleActionResultMapper
{
    internal static ServiceActionResult Map(in HarvestLifecycleSubmission submission)
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

    internal static ServiceActionResultCode Code(HarvestLifecyclePreflight preflight) =>
        preflight switch
        {
            HarvestLifecyclePreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            HarvestLifecyclePreflight.ContractUnavailable => HarvestLifecycleActionResultCodes.ContractUnavailable,
            HarvestLifecyclePreflight.WrongThread => HarvestLifecycleActionResultCodes.WrongThread,
            HarvestLifecyclePreflight.IdentityUnavailable => HarvestLifecycleActionResultCodes.IdentityUnavailable,
            HarvestLifecyclePreflight.NotVisible => HarvestLifecycleActionResultCodes.NotVisible,
            HarvestLifecyclePreflight.ElementListFull => HarvestLifecycleActionResultCodes.ElementListFull,
            HarvestLifecyclePreflight.ElementUsageUnavailable => HarvestLifecycleActionResultCodes.ElementUsageUnavailable,
            HarvestLifecyclePreflight.ActionUnavailable => HarvestLifecycleActionResultCodes.ActionUnavailable,
            HarvestLifecyclePreflight.ActionListFull => HarvestLifecycleActionResultCodes.ActionListFull,
            HarvestLifecyclePreflight.AmountUnavailable => HarvestLifecycleActionResultCodes.AmountUnavailable,
            HarvestLifecyclePreflight.NotActive => HarvestLifecycleActionResultCodes.NotActive,
            HarvestLifecyclePreflight.MutationPermitUnavailable => HarvestLifecycleActionResultCodes.MutationPermitUnavailable,
            HarvestLifecyclePreflight.PostCommitFault => HarvestLifecycleActionResultCodes.PostCommitFault,
            HarvestLifecyclePreflight.VerificationFailed => HarvestLifecycleActionResultCodes.VerificationFailed,
            _ => HarvestLifecycleActionResultCodes.ContractUnavailable,
        };
}

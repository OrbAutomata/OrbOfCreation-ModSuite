using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class PlotLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2068);
    internal static readonly ServiceActionResultCode WrongThread = new(2069);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2070);
    internal static readonly ServiceActionResultCode PlotUnavailable = new(2071);
    internal static readonly ServiceActionResultCode ActionUnavailable = new(2072);
    internal static readonly ServiceActionResultCode ActionListFull = new(2073);
    internal static readonly ServiceActionResultCode QuantityUnavailable = new(2074);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2075);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2076);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2077);
    internal static readonly ServiceActionResultCode NotActive = new(2078);
}

internal static class PlotLifecycleActionResultMapper
{
    internal static ServiceActionResult Map(in PlotLifecycleSubmission submission)
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

    internal static ServiceActionResultCode Code(PlotLifecyclePreflight preflight) =>
        preflight switch
        {
            PlotLifecyclePreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            PlotLifecyclePreflight.ContractUnavailable => PlotLifecycleActionResultCodes.ContractUnavailable,
            PlotLifecyclePreflight.WrongThread => PlotLifecycleActionResultCodes.WrongThread,
            PlotLifecyclePreflight.IdentityUnavailable => PlotLifecycleActionResultCodes.IdentityUnavailable,
            PlotLifecyclePreflight.PlotUnavailable => PlotLifecycleActionResultCodes.PlotUnavailable,
            PlotLifecyclePreflight.ActionUnavailable => PlotLifecycleActionResultCodes.ActionUnavailable,
            PlotLifecyclePreflight.ActionListFull => PlotLifecycleActionResultCodes.ActionListFull,
            PlotLifecyclePreflight.QuantityUnavailable => PlotLifecycleActionResultCodes.QuantityUnavailable,
            PlotLifecyclePreflight.NotActive => PlotLifecycleActionResultCodes.NotActive,
            PlotLifecyclePreflight.MutationPermitUnavailable => PlotLifecycleActionResultCodes.MutationPermitUnavailable,
            PlotLifecyclePreflight.PostCommitFault => PlotLifecycleActionResultCodes.PostCommitFault,
            PlotLifecyclePreflight.VerificationFailed => PlotLifecycleActionResultCodes.VerificationFailed,
            _ => PlotLifecycleActionResultCodes.ContractUnavailable,
        };
}

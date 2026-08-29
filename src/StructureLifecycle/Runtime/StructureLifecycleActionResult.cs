using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class StructureLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2078);
    internal static readonly ServiceActionResultCode WrongThread = new(2079);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2080);
    internal static readonly ServiceActionResultCode NotAvailable = new(2081);
    internal static readonly ServiceActionResultCode AlreadyInState = new(2082);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2083);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2084);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2085);
}

internal static class StructureLifecycleActionResultMapper
{
    internal static ServiceActionResult Map(in StructureLifecycleSubmission submission)
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

    internal static ServiceActionResultCode Code(StructureLifecyclePreflight preflight) =>
        preflight switch
        {
            StructureLifecyclePreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
            StructureLifecyclePreflight.ContractUnavailable => StructureLifecycleActionResultCodes.ContractUnavailable,
            StructureLifecyclePreflight.WrongThread => StructureLifecycleActionResultCodes.WrongThread,
            StructureLifecyclePreflight.IdentityUnavailable => StructureLifecycleActionResultCodes.IdentityUnavailable,
            StructureLifecyclePreflight.NotAvailable => StructureLifecycleActionResultCodes.NotAvailable,
            StructureLifecyclePreflight.AlreadyInState => StructureLifecycleActionResultCodes.AlreadyInState,
            StructureLifecyclePreflight.MutationPermitUnavailable => StructureLifecycleActionResultCodes.MutationPermitUnavailable,
            StructureLifecyclePreflight.PostCommitFault => StructureLifecycleActionResultCodes.PostCommitFault,
            StructureLifecyclePreflight.VerificationFailed => StructureLifecycleActionResultCodes.VerificationFailed,
            _ => StructureLifecycleActionResultCodes.ContractUnavailable,
        };
}

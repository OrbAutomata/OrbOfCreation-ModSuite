using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AlchemyLoadoutActionResultMapper
{
    internal static ServiceActionResult Map(in AlchemyLoadoutSubmission submission)
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

    internal static ServiceActionResultCode Code(AlchemyLoadoutPreflight preflight) => preflight switch
    {
        AlchemyLoadoutPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        AlchemyLoadoutPreflight.ContractUnavailable => AlchemyLoadoutActionResultCodes.ContractUnavailable,
        AlchemyLoadoutPreflight.WrongThread => AlchemyLoadoutActionResultCodes.WrongThread,
        AlchemyLoadoutPreflight.IdentityUnavailable => AlchemyLoadoutActionResultCodes.IdentityUnavailable,
        AlchemyLoadoutPreflight.WrongDomain => AlchemyLoadoutActionResultCodes.WrongDomain,
        AlchemyLoadoutPreflight.NotDiscovered => AlchemyLoadoutActionResultCodes.NotDiscovered,
        AlchemyLoadoutPreflight.AlreadyInRequestedState => AlchemyLoadoutActionResultCodes.AlreadyInRequestedState,
        AlchemyLoadoutPreflight.LoadoutFull => AlchemyLoadoutActionResultCodes.LoadoutFull,
        AlchemyLoadoutPreflight.UsageUnavailable => AlchemyLoadoutActionResultCodes.UsageUnavailable,
        AlchemyLoadoutPreflight.MutationPermitUnavailable => AlchemyLoadoutActionResultCodes.MutationPermitUnavailable,
        AlchemyLoadoutPreflight.PostCommitFault => AlchemyLoadoutActionResultCodes.PostCommitFault,
        AlchemyLoadoutPreflight.VerificationFailed => AlchemyLoadoutActionResultCodes.VerificationFailed,
        _ => AlchemyLoadoutActionResultCodes.ContractUnavailable,
    };
}

using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class GenericDiscoveryActionResultMapper
{
    internal static ServiceActionResult Map(in GenericDiscoverySubmission submission)
    {
        if (submission.Verified)
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    submission.CallOutcome));
        var code = Code(submission.Preflight);
        if (submission.CallOutcome.MutationAttempts == 0)
            return ServiceActionResult.Rejected(code);
        return ServiceActionResult.Faulted(
            code,
            ServiceNativeMutationEvidence.Observed(
                submission.Outcome,
                submission.CallOutcome));
    }

    internal static ServiceActionResultCode Code(GenericDiscoveryPreflight preflight) => preflight switch
    {
        GenericDiscoveryPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        GenericDiscoveryPreflight.ContractUnavailable => GenericDiscoveryActionResultCodes.ContractUnavailable,
        GenericDiscoveryPreflight.WrongThread => GenericDiscoveryActionResultCodes.WrongThread,
        GenericDiscoveryPreflight.IdentityUnavailable => GenericDiscoveryActionResultCodes.IdentityUnavailable,
        GenericDiscoveryPreflight.UnsupportedType => GenericDiscoveryActionResultCodes.UnsupportedType,
        GenericDiscoveryPreflight.NotVisible => GenericDiscoveryActionResultCodes.NotVisible,
        GenericDiscoveryPreflight.AlreadyDiscovered => GenericDiscoveryActionResultCodes.AlreadyDiscovered,
        GenericDiscoveryPreflight.DiscoveryUnavailable => GenericDiscoveryActionResultCodes.DiscoveryUnavailable,
        GenericDiscoveryPreflight.Unaffordable => GenericDiscoveryActionResultCodes.Unaffordable,
        GenericDiscoveryPreflight.MutationPermitUnavailable => GenericDiscoveryActionResultCodes.MutationPermitUnavailable,
        GenericDiscoveryPreflight.PostCommitFault => GenericDiscoveryActionResultCodes.PostCommitFault,
        GenericDiscoveryPreflight.VerificationFailed => GenericDiscoveryActionResultCodes.VerificationFailed,
        GenericDiscoveryPreflight.GlyphRecipeEmpty => GenericDiscoveryActionResultCodes.GlyphRecipeEmpty,
        GenericDiscoveryPreflight.ScreenLocked => GenericDiscoveryActionResultCodes.ScreenLocked,
        _ => GenericDiscoveryActionResultCodes.ContractUnavailable,
    };
}

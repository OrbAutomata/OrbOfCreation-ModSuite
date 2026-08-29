using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class DiscoveryTreeOfferActionResultMapper
{
    internal static ServiceActionResult Map(in DiscoveryTreeOfferSubmission submission)
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

    internal static ServiceActionResultCode Code(DiscoveryTreeOfferPreflight preflight) => preflight switch
    {
        DiscoveryTreeOfferPreflight.LifecycleReplaced => CommonActionResultCodes.LifecycleReplaced,
        DiscoveryTreeOfferPreflight.ContractUnavailable => DiscoveryTreeOfferActionResultCodes.ContractUnavailable,
        DiscoveryTreeOfferPreflight.WrongThread => DiscoveryTreeOfferActionResultCodes.WrongThread,
        DiscoveryTreeOfferPreflight.IdentityUnavailable => DiscoveryTreeOfferActionResultCodes.IdentityUnavailable,
        DiscoveryTreeOfferPreflight.TreeUnavailable => DiscoveryTreeOfferActionResultCodes.TreeUnavailable,
        DiscoveryTreeOfferPreflight.WrongMode => DiscoveryTreeOfferActionResultCodes.WrongMode,
        DiscoveryTreeOfferPreflight.NoDiscoveries => DiscoveryTreeOfferActionResultCodes.NoDiscoveries,
        DiscoveryTreeOfferPreflight.OfferUnavailable => DiscoveryTreeOfferActionResultCodes.OfferUnavailable,
        DiscoveryTreeOfferPreflight.AlreadyDiscovered => DiscoveryTreeOfferActionResultCodes.AlreadyDiscovered,
        DiscoveryTreeOfferPreflight.RerollUnavailable => DiscoveryTreeOfferActionResultCodes.RerollUnavailable,
        DiscoveryTreeOfferPreflight.Unaffordable => DiscoveryTreeOfferActionResultCodes.Unaffordable,
        DiscoveryTreeOfferPreflight.MutationPermitUnavailable => DiscoveryTreeOfferActionResultCodes.MutationPermitUnavailable,
        DiscoveryTreeOfferPreflight.PostCommitFault => DiscoveryTreeOfferActionResultCodes.PostCommitFault,
        DiscoveryTreeOfferPreflight.VerificationFailed => DiscoveryTreeOfferActionResultCodes.VerificationFailed,
        _ => DiscoveryTreeOfferActionResultCodes.ContractUnavailable,
    };
}

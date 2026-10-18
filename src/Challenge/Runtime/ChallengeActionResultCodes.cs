using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ChallengeActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5201, "contract_unavailable");
    internal static ServiceActionResultCode WrongThread => new(5203, "wrong_thread");
    internal static ServiceActionResultCode IdentityUnavailable => new(5204, "identity_unavailable");
    internal static ServiceActionResultCode OfferUnavailable => new(5205, "offer_unavailable");
    internal static ServiceActionResultCode SelectionFull => new(5206, "selection_full");
    internal static ServiceActionResultCode SelectionRestricted => new(5207, "selection_restricted");
    internal static ServiceActionResultCode InvalidState => new(5208, "invalid_state");
    internal static ServiceActionResultCode FetchUnavailable => new(5209, "fetch_unavailable");
    internal static ServiceActionResultCode NoRerolls => new(5210, "no_rerolls");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5211, "action_family_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(5212, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(5213, "verification_failed");
}

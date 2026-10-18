using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class DiscoveryTreeOfferActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(4300, "contract_unavailable");
    internal static ServiceActionResultCode WrongThread => new(4302, "wrong_thread");
    internal static ServiceActionResultCode IdentityUnavailable => new(4303, "identity_unavailable");
    internal static ServiceActionResultCode TreeUnavailable => new(4304, "tree_unavailable");
    internal static ServiceActionResultCode WrongMode => new(4305, "wrong_mode");
    internal static ServiceActionResultCode NoDiscoveries => new(4306, "no_discoveries");
    internal static ServiceActionResultCode OfferUnavailable => new(4307, "offer_unavailable");
    internal static ServiceActionResultCode RerollUnavailable => new(4309, "reroll_unavailable");
    internal static ServiceActionResultCode Unaffordable => new(4310, "unaffordable");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4311, "action_family_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(4312, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(4313, "verification_failed");
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ResearchActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5401, "contract_unavailable");
    internal static ServiceActionResultCode WrongThread => new(5403, "wrong_thread");
    internal static ServiceActionResultCode IdentityUnavailable => new(5404, "identity_unavailable");
    internal static ServiceActionResultCode DevelopUnavailable => new(5405, "develop_unavailable");
    internal static ServiceActionResultCode MultiBuyUnavailable => new(5406, "multi_buy_unavailable");
    internal static ServiceActionResultCode InvalidMode => new(5407, "unsupported_control");
    internal static ServiceActionResultCode InvalidState => new(5408, "invalid_state");
    internal static ServiceActionResultCode BonusUnavailable => new(5409, "bonus_unavailable");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5410, "action_family_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(5411, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(5412, "verification_failed");
    internal static ServiceActionResultCode AmountUnavailable => new(5413, "amount_unavailable");

    // One vocabulary for "why can this research not develop". These are the gates
    // ResearchSO.IsWithinDevelopRange consults, in its order, so the action answers with the same
    // cause the row already publishes instead of collapsing all of them into one amount refusal.
    internal static ServiceActionResultCode AlreadyMaxed => new(5414, "already_maxed");
    internal static ServiceActionResultCode Unaffordable => new(5415, "unaffordable");
    internal static ServiceActionResultCode RequirementsUnmet => new(5416, "requirements_unmet");
    internal static ServiceActionResultCode LeewayExhausted => new(5417, "research_leeway_exhausted");
    internal static ServiceActionResultCode AlreadyDeveloping => new(5418, "already_developing");
    internal static ServiceActionResultCode Locked => new(5419, "research_locked");
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class StructureLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2078, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(2079, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2080, "identity_unavailable");
    internal static readonly ServiceActionResultCode NotAvailable = new(2081, "not_available");
    internal static readonly ServiceActionResultCode AlreadyInState = new(2082, "already_in_requested_state");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2083, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2084, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2085, "verification_failed");
}

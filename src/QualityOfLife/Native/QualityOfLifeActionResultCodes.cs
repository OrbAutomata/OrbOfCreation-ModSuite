using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class QualityOfLifeActionResultCodes
{
    internal static ServiceActionResultCode WrongThread => new(1024, "wrong_thread");
    internal static ServiceActionResultCode ContractUnavailable => new(1025, "contract_unavailable");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(1026, "mutation_permit_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(1027, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(1028, "verification_failed");
    internal static ServiceActionResultCode UnsupportedPassive => new(1029, "unsupported_passive");
    internal static ServiceActionResultCode NotEligible => new(1030, "passive_not_eligible");
    internal static ServiceActionResultCode IdentityUnavailable => new(1031, "identity_unavailable");
    internal static ServiceActionResultCode AlreadyMuted => new(1032, "already_muted");
}

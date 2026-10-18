using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class PrestigeActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5301, "contract_unavailable");
    internal static ServiceActionResultCode WrongThread => new(5303, "wrong_thread");
    internal static ServiceActionResultCode WorldCycleIncomplete => new(5304, "world_cycle_incomplete");
    internal static ServiceActionResultCode ChallengesNotFetched => new(5305, "challenges_not_fetched");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5306, "action_family_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(5307, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(5308, "verification_failed");
}

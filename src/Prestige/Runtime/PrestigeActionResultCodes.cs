using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class PrestigeActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5301);
    internal static ServiceActionResultCode WrongThread => new(5303);
    internal static ServiceActionResultCode WorldCycleIncomplete => new(5304);
    internal static ServiceActionResultCode ChallengesNotFetched => new(5305);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5306);
    internal static ServiceActionResultCode PostCommitFault => new(5307);
    internal static ServiceActionResultCode VerificationFailed => new(5308);
}

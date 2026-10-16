using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ChallengeActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5201);
    internal static ServiceActionResultCode WrongThread => new(5203);
    internal static ServiceActionResultCode IdentityUnavailable => new(5204);
    internal static ServiceActionResultCode OfferUnavailable => new(5205);
    internal static ServiceActionResultCode SelectionFull => new(5206);
    internal static ServiceActionResultCode SelectionRestricted => new(5207);
    internal static ServiceActionResultCode InvalidState => new(5208);
    internal static ServiceActionResultCode FetchUnavailable => new(5209);
    internal static ServiceActionResultCode NoRerolls => new(5210);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5211);
    internal static ServiceActionResultCode PostCommitFault => new(5212);
    internal static ServiceActionResultCode VerificationFailed => new(5213);
}

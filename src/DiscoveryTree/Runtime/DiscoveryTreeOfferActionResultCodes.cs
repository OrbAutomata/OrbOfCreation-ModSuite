using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class DiscoveryTreeOfferActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(4300);
    internal static ServiceActionResultCode WrongThread => new(4302);
    internal static ServiceActionResultCode IdentityUnavailable => new(4303);
    internal static ServiceActionResultCode TreeUnavailable => new(4304);
    internal static ServiceActionResultCode WrongMode => new(4305);
    internal static ServiceActionResultCode NoDiscoveries => new(4306);
    internal static ServiceActionResultCode OfferUnavailable => new(4307);
    internal static ServiceActionResultCode AlreadyDiscovered => new(4308);
    internal static ServiceActionResultCode RerollUnavailable => new(4309);
    internal static ServiceActionResultCode Unaffordable => new(4310);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4311);
    internal static ServiceActionResultCode PostCommitFault => new(4312);
    internal static ServiceActionResultCode VerificationFailed => new(4313);
}

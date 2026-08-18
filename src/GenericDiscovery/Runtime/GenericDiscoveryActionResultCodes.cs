using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class GenericDiscoveryActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5000);
    internal static ServiceActionResultCode WrongThread => new(5002);
    internal static ServiceActionResultCode IdentityUnavailable => new(5003);
    internal static ServiceActionResultCode UnsupportedType => new(5004);
    internal static ServiceActionResultCode NotVisible => new(5005);
    internal static ServiceActionResultCode AlreadyDiscovered => new(5006);
    internal static ServiceActionResultCode DiscoveryUnavailable => new(5007);
    internal static ServiceActionResultCode Unaffordable => new(5008);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5009);
    internal static ServiceActionResultCode PostCommitFault => new(5010);
    internal static ServiceActionResultCode VerificationFailed => new(5011);
    internal static ServiceActionResultCode GlyphRecipeEmpty => new(5013);
    internal static ServiceActionResultCode ScreenLocked => new(5014);
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class CraftingStationActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2031);
    internal static readonly ServiceActionResultCode WrongThread = new(2032);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2033);
    internal static readonly ServiceActionResultCode SelectionUnavailable = new(2034);
    internal static readonly ServiceActionResultCode SelectionHidden = new(2035);
    internal static readonly ServiceActionResultCode LevelOutOfRange = new(2036);
    internal static readonly ServiceActionResultCode NotLoaded = new(2037);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(2038);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2039);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2040);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2041);
}

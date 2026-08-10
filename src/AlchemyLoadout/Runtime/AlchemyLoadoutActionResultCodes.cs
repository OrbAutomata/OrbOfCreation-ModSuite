using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AlchemyLoadoutActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(1960);
    internal static readonly ServiceActionResultCode WrongThread = new(1961);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(1962);
    internal static readonly ServiceActionResultCode WrongDomain = new(1963);
    internal static readonly ServiceActionResultCode NotDiscovered = new(1964);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(1965);
    internal static readonly ServiceActionResultCode LoadoutFull = new(1966);
    internal static readonly ServiceActionResultCode UsageUnavailable = new(1967);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(1970);
    internal static readonly ServiceActionResultCode PostCommitFault = new(1971);
    internal static readonly ServiceActionResultCode VerificationFailed = new(1972);
}

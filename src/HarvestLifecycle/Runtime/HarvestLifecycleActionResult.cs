using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class HarvestLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2056);
    internal static readonly ServiceActionResultCode WrongThread = new(2057);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2058);
    internal static readonly ServiceActionResultCode NotVisible = new(2059);
    internal static readonly ServiceActionResultCode ElementListFull = new(2060);
    internal static readonly ServiceActionResultCode ElementUsageUnavailable = new(2061);
    internal static readonly ServiceActionResultCode ActionUnavailable = new(2062);
    internal static readonly ServiceActionResultCode ActionListFull = new(2063);
    internal static readonly ServiceActionResultCode AmountUnavailable = new(2064);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2065);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2066);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2067);
    internal static readonly ServiceActionResultCode NotActive = new(2078);
}

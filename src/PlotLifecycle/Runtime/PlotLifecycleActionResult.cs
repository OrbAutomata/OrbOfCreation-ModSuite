using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class PlotLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2068);
    internal static readonly ServiceActionResultCode WrongThread = new(2069);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2070);
    internal static readonly ServiceActionResultCode PlotUnavailable = new(2071);
    internal static readonly ServiceActionResultCode ActionUnavailable = new(2072);
    internal static readonly ServiceActionResultCode ActionListFull = new(2073);
    internal static readonly ServiceActionResultCode QuantityUnavailable = new(2074);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2075);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2076);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2077);
    internal static readonly ServiceActionResultCode NotActive = new(2078);
}

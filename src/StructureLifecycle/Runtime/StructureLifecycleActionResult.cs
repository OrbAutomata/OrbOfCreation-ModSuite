using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class StructureLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2078);
    internal static readonly ServiceActionResultCode WrongThread = new(2079);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2080);
    internal static readonly ServiceActionResultCode NotAvailable = new(2081);
    internal static readonly ServiceActionResultCode AlreadyInState = new(2082);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2083);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2084);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2085);
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ReturnToMenuActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2086);
    internal static readonly ServiceActionResultCode WrongThread = new(2087);
    internal static readonly ServiceActionResultCode WrongScene = new(2088);
    internal static readonly ServiceActionResultCode TransitionInProgress = new(2089);
    internal static readonly ServiceActionResultCode ControlUnavailable = new(2090);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2091);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2092);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2093);
}

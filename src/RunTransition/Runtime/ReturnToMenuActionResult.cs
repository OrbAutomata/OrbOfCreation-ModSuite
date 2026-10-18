using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ReturnToMenuActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2086, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(2087, "wrong_thread");
    internal static readonly ServiceActionResultCode WrongScene = new(2088, "wrong_scene");
    internal static readonly ServiceActionResultCode TransitionInProgress = new(2089, "transition_in_progress");
    internal static readonly ServiceActionResultCode ControlUnavailable = new(2090, "control_unavailable");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2091, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2092, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2093, "verification_failed");
}

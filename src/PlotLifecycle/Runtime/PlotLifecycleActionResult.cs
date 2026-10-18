using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class PlotLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2068, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(2069, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2070, "identity_unavailable");
    internal static readonly ServiceActionResultCode PlotUnavailable = new(2071, "not_visible");
    internal static readonly ServiceActionResultCode ActionUnavailable = new(2072, "action_not_available");
    internal static readonly ServiceActionResultCode ActionListFull = new(2073, "plot_action_list_full");
    internal static readonly ServiceActionResultCode QuantityUnavailable = new(2074, "amount_unavailable");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2075, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2076, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2077, "verification_failed");
    internal static readonly ServiceActionResultCode NotActive = new(2078, "not_active");
}

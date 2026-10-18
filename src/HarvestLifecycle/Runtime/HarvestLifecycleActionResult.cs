using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class HarvestLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2056, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(2057, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2058, "identity_unavailable");
    internal static readonly ServiceActionResultCode NotVisible = new(2059, "not_visible");
    internal static readonly ServiceActionResultCode ElementListFull = new(2060, "harvest_list_full");
    internal static readonly ServiceActionResultCode ElementUsageUnavailable = new(2061, "element_capacity_unavailable");
    internal static readonly ServiceActionResultCode ActionUnavailable = new(2062, "action_not_available");
    internal static readonly ServiceActionResultCode ActionListFull = new(2063, "plot_action_list_full");
    internal static readonly ServiceActionResultCode AmountUnavailable = new(2064, "amount_unavailable");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2065, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2066, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2067, "verification_failed");
    internal static readonly ServiceActionResultCode NotActive = new(2078, "not_active");
}

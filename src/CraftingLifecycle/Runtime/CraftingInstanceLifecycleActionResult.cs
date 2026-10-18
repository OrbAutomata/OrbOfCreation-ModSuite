using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class CraftingInstanceLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2020, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(2021, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2022, "identity_unavailable");
    internal static readonly ServiceActionResultCode NotVisible = new(2023, "not_visible");
    internal static readonly ServiceActionResultCode PageRelationAmbiguous = new(2024, "page_relation_ambiguous");
    internal static readonly ServiceActionResultCode InstanceUnavailable = new(2025, "instance_unavailable");
    internal static readonly ServiceActionResultCode AutomationFull = new(2026, "automation_full");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2028, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2029, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2030, "verification_failed");
}

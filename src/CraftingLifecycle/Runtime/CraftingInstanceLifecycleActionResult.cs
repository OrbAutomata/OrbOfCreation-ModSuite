using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class CraftingInstanceLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2020);
    internal static readonly ServiceActionResultCode WrongThread = new(2021);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2022);
    internal static readonly ServiceActionResultCode NotVisible = new(2023);
    internal static readonly ServiceActionResultCode PageRelationAmbiguous = new(2024);
    internal static readonly ServiceActionResultCode InstanceUnavailable = new(2025);
    internal static readonly ServiceActionResultCode AutomationFull = new(2026);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2028);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2029);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2030);
}

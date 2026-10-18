using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class EquipmentLoadoutActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5100, "contract_unavailable");
    internal static ServiceActionResultCode WrongThread => new(5102, "wrong_thread");
    internal static ServiceActionResultCode IdentityUnavailable => new(5103, "identity_unavailable");
    internal static ServiceActionResultCode NotCreated => new(5104, "not_created");
    internal static ServiceActionResultCode AlreadyInRequestedState => new(5105, "already_in_requested_state");
    internal static ServiceActionResultCode LoadoutFull => new(5106, "loadout_full");
    internal static ServiceActionResultCode EquipmentTypeFull => new(5107, "equipment_type_full");
    internal static ServiceActionResultCode UsageUnaffordable => new(5108, "usage_unaffordable");
    internal static ServiceActionResultCode MultiBuyUnavailable => new(5109, "multi_buy_unavailable");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5110, "action_family_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(5111, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(5112, "verification_failed");
    internal static ServiceActionResultCode AmountUnavailable => new(5113, "amount_unavailable");
}

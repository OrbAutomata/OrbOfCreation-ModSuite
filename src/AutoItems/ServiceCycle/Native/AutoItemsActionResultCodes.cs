using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsActionResultCodes
{
    internal static ServiceActionResultCode ActionFamilyUnavailable => new(4096, "action_family_unavailable");
    internal static ServiceActionResultCode ItemUnavailable => new(4097, "item_unavailable");
    internal static ServiceActionResultCode FamilyChanged => new(4098, "family_changed");
    internal static ServiceActionResultCode NativeBusy => new(4099, "native_busy");
    internal static ServiceActionResultCode NotVisible => new(4100, "not_visible");
    internal static ServiceActionResultCode CanFireRefused => new(4101, "can_fire_refused");
    internal static ServiceActionResultCode RandomizationUnavailable => new(4102, "randomization_unavailable");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4103, "action_family_unavailable");
    internal static ServiceActionResultCode TargetUnavailable => new(4104, "target_unavailable");
    internal static ServiceActionResultCode ContractUnavailable => new(4105, "contract_unavailable");
    internal static ServiceActionResultCode MultiBuyUnavailable => new(4106, "multi_buy_unavailable");
    internal static ServiceActionResultCode UseNotVerified => new(4107, "use_not_verified");
    internal static ServiceActionResultCode TemporaryDurationChanged => new(4108, "temporary_duration_changed");
    internal static ServiceActionResultCode TemporaryCostChanged => new(4109, "temporary_cost_changed");
    internal static ServiceActionResultCode TemporaryEffectPresent => new(4110, "temporary_effect_present");
    internal static ServiceActionResultCode TargetingInProgress => new(4111, "targeting_in_progress");
}

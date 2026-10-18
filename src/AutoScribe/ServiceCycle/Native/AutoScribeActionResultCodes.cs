using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoScribeActionResultCodes
{
    internal static ServiceActionResultCode IdentityUnavailable => new(4200, "identity_unavailable");
    internal static ServiceActionResultCode RelationshipMismatch => new(4201, "relationship_mismatch");
    internal static ServiceActionResultCode RecipeUnavailable => new(4202, "recipe_unavailable");
    internal static ServiceActionResultCode TargetUnavailable => new(4203, "target_unavailable");
    internal static ServiceActionResultCode QueueFull => new(4204, "queue_full");
    internal static ServiceActionResultCode CompetingSupply => new(4205, "competing_supply");
    internal static ServiceActionResultCode Unaffordable => new(4206, "unaffordable");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4207, "action_family_unavailable");
    internal static ServiceActionResultCode ContractUnavailable => new(4208, "contract_unavailable");
    internal static ServiceActionResultCode Quarantined => new(4209, "quarantined");
    internal static ServiceActionResultCode PostPaymentFault => new(4210, "post_payment_fault");
    internal static ServiceActionResultCode VerificationFailed => new(4211, "verification_failed");
    internal static ServiceActionResultCode WrongThread => new(4212, "wrong_thread");
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoScribeActionResultCodes
{
    internal static ServiceActionResultCode IdentityUnavailable => new(4200);
    internal static ServiceActionResultCode RelationshipMismatch => new(4201);
    internal static ServiceActionResultCode RecipeUnavailable => new(4202);
    internal static ServiceActionResultCode TargetUnavailable => new(4203);
    internal static ServiceActionResultCode QueueFull => new(4204);
    internal static ServiceActionResultCode CompetingSupply => new(4205);
    internal static ServiceActionResultCode Unaffordable => new(4206);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4207);
    internal static ServiceActionResultCode ContractUnavailable => new(4208);
    internal static ServiceActionResultCode Quarantined => new(4209);
    internal static ServiceActionResultCode PostPaymentFault => new(4210);
    internal static ServiceActionResultCode VerificationFailed => new(4211);
    internal static ServiceActionResultCode WrongThread => new(4212);
}

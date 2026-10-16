using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class EquipmentLoadoutActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5100);
    internal static ServiceActionResultCode WrongThread => new(5102);
    internal static ServiceActionResultCode IdentityUnavailable => new(5103);
    internal static ServiceActionResultCode NotCreated => new(5104);
    internal static ServiceActionResultCode AlreadyInRequestedState => new(5105);
    internal static ServiceActionResultCode LoadoutFull => new(5106);
    internal static ServiceActionResultCode EquipmentTypeFull => new(5107);
    internal static ServiceActionResultCode UsageUnaffordable => new(5108);
    internal static ServiceActionResultCode MultiBuyUnavailable => new(5109);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5110);
    internal static ServiceActionResultCode PostCommitFault => new(5111);
    internal static ServiceActionResultCode VerificationFailed => new(5112);
    internal static ServiceActionResultCode AmountUnavailable => new(5113);
}

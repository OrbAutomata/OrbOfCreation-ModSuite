using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class LoadoutActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2042);
    internal static readonly ServiceActionResultCode WrongThread = new(2043);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2044);
    internal static readonly ServiceActionResultCode WrongTargetType = new(2045);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(2046);
    internal static readonly ServiceActionResultCode SwitchBlocked = new(2047);
    internal static readonly ServiceActionResultCode EntryUnavailable = new(2048);
    internal static readonly ServiceActionResultCode SlotOutOfRange = new(2049);
    internal static readonly ServiceActionResultCode SlotEmpty = new(2050);
    internal static readonly ServiceActionResultCode SlotOccupied = new(2051);
    internal static readonly ServiceActionResultCode NameOutOfRange = new(2052);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2053);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2054);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2055);
    internal static readonly ServiceActionResultCode ActiveSectionEmpty = new(2056);
}

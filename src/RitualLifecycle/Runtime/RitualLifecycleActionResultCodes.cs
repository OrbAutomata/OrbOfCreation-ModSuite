using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class RitualLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(1980);
    internal static readonly ServiceActionResultCode WrongThread = new(1981);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(1982);
    internal static readonly ServiceActionResultCode NotDiscovered = new(1983);
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(1984);
    internal static readonly ServiceActionResultCode LevelLocked = new(1986);
    internal static readonly ServiceActionResultCode LevelOutOfRange = new(1987);
    internal static readonly ServiceActionResultCode BattleAlreadyActive = new(1988);
    internal static readonly ServiceActionResultCode Unaffordable = new(1989);
    internal static readonly ServiceActionResultCode NoDurationEffect = new(1990);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(1991);
    internal static readonly ServiceActionResultCode PostCommitFault = new(1992);
    internal static readonly ServiceActionResultCode VerificationFailed = new(1993);
    internal static readonly ServiceActionResultCode NoBattleActive = new(1994);
    internal static readonly ServiceActionResultCode WrongActiveRitual = new(1995);
}

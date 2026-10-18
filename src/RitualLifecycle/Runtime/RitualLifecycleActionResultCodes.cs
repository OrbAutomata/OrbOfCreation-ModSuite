using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class RitualLifecycleActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(1980, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(1981, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(1982, "identity_unavailable");
    internal static readonly ServiceActionResultCode NotDiscovered = new(1983, "not_discovered");
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(1984, "already_in_requested_state");
    internal static readonly ServiceActionResultCode LevelLocked = new(1986, "level_locked");
    internal static readonly ServiceActionResultCode LevelOutOfRange = new(1987, "level_out_of_range");
    internal static readonly ServiceActionResultCode BattleAlreadyActive = new(1988, "ritual_battle_active");
    internal static readonly ServiceActionResultCode Unaffordable = new(1989, "unaffordable");
    internal static readonly ServiceActionResultCode NoDurationEffect = new(1990, "no_active_duration_reward");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(1991, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(1992, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(1993, "verification_failed");
    internal static readonly ServiceActionResultCode NoBattleActive = new(1994, "no_ritual_battle_active");
    internal static readonly ServiceActionResultCode WrongActiveRitual = new(1995, "wrong_active_ritual");
}

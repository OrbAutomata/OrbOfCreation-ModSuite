using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class LoadoutActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(2042, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(2043, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(2044, "identity_unavailable");
    internal static readonly ServiceActionResultCode WrongTargetType = new(2045, "wrong_loadout_surface");
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(2046, "already_in_requested_state");
    internal static readonly ServiceActionResultCode SwitchBlocked = new(2047, "switch_blocked");
    internal static readonly ServiceActionResultCode EntryUnavailable = new(2048, "saved_entry_unavailable");
    internal static readonly ServiceActionResultCode SlotOutOfRange = new(2049, "slot_out_of_range");
    internal static readonly ServiceActionResultCode SlotEmpty = new(2050, "slot_empty");
    internal static readonly ServiceActionResultCode SlotOccupied = new(2051, "slot_occupied");
    internal static readonly ServiceActionResultCode NameOutOfRange = new(2052, "name_out_of_range");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2053, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2054, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2055, "verification_failed");
    internal static readonly ServiceActionResultCode ActiveSectionEmpty = new(2056, "active_section_empty");
}

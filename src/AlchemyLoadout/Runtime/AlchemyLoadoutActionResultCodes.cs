using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AlchemyLoadoutActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(1960, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(1961, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(1962, "identity_unavailable");
    internal static readonly ServiceActionResultCode WrongDomain = new(1963, "wrong_alchemy_surface");
    internal static readonly ServiceActionResultCode NotDiscovered = new(1964, "not_discovered");
    internal static readonly ServiceActionResultCode AlreadyInRequestedState = new(1965, "already_in_requested_state");
    internal static readonly ServiceActionResultCode LoadoutFull = new(1966, "loadout_full");
    internal static readonly ServiceActionResultCode UsageUnavailable = new(1967, "amount_unavailable");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(1970, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(1971, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(1972, "verification_failed");
}

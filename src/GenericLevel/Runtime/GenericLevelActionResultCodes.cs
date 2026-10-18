using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class GenericLevelActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(1994, "contract_unavailable");
    internal static readonly ServiceActionResultCode WrongThread = new(1995, "wrong_thread");
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(1996, "identity_unavailable");
    internal static readonly ServiceActionResultCode WrongDomain = new(1997, "wrong_level_surface");
    internal static readonly ServiceActionResultCode CannotLevel = new(1998, "cannot_level");
    internal static readonly ServiceActionResultCode BonusUnavailable = new(1999, "bonus_unavailable");
    internal static readonly ServiceActionResultCode ResourcesHidden = new(2000, "resources_hidden");
    internal static readonly ServiceActionResultCode Unaffordable = new(2001, "unaffordable");
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2002, "action_family_unavailable");
    internal static readonly ServiceActionResultCode PostCommitFault = new(2003, "post_commit_fault");
    internal static readonly ServiceActionResultCode VerificationFailed = new(2004, "verification_failed");
    internal static readonly ServiceActionResultCode Undiscovered = new(2081, "undiscovered");
    internal static readonly ServiceActionResultCode Hidden = new(2082, "hidden");
    internal static readonly ServiceActionResultCode Unavailable = new(2083, "not_available");
}

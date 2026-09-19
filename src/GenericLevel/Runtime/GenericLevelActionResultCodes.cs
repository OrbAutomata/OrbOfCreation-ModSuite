using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class GenericLevelActionResultCodes
{
    internal static readonly ServiceActionResultCode ContractUnavailable = new(1994);
    internal static readonly ServiceActionResultCode WrongThread = new(1995);
    internal static readonly ServiceActionResultCode IdentityUnavailable = new(1996);
    internal static readonly ServiceActionResultCode WrongDomain = new(1997);
    internal static readonly ServiceActionResultCode CannotLevel = new(1998);
    internal static readonly ServiceActionResultCode BonusUnavailable = new(1999);
    internal static readonly ServiceActionResultCode ResourcesHidden = new(2000);
    internal static readonly ServiceActionResultCode Unaffordable = new(2001);
    internal static readonly ServiceActionResultCode MutationPermitUnavailable = new(2002);
    internal static readonly ServiceActionResultCode PostCommitFault = new(2003);
    internal static readonly ServiceActionResultCode VerificationFailed = new(2004);
    internal static readonly ServiceActionResultCode Undiscovered = new(2081);
    internal static readonly ServiceActionResultCode Hidden = new(2082);
    internal static readonly ServiceActionResultCode Unavailable = new(2083);
}

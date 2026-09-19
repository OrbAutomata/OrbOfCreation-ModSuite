using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class ResearchActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(5401);
    internal static ServiceActionResultCode WrongThread => new(5403);
    internal static ServiceActionResultCode IdentityUnavailable => new(5404);
    internal static ServiceActionResultCode DevelopUnavailable => new(5405);
    internal static ServiceActionResultCode MultiBuyUnavailable => new(5406);
    internal static ServiceActionResultCode InvalidMode => new(5407);
    internal static ServiceActionResultCode InvalidState => new(5408);
    internal static ServiceActionResultCode BonusUnavailable => new(5409);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(5410);
    internal static ServiceActionResultCode PostCommitFault => new(5411);
    internal static ServiceActionResultCode VerificationFailed => new(5412);
    internal static ServiceActionResultCode AmountUnavailable => new(5413);

    // One vocabulary for "why can this research not develop". These are the gates
    // ResearchSO.IsWithinDevelopRange consults, in its order, so the action answers with the same
    // cause the row already publishes instead of collapsing all of them into one amount refusal.
    internal static ServiceActionResultCode AlreadyMaxed => new(5414);
    internal static ServiceActionResultCode Unaffordable => new(5415);
    internal static ServiceActionResultCode RequirementsUnmet => new(5416);
    internal static ServiceActionResultCode LeewayExhausted => new(5417);
    internal static ServiceActionResultCode AlreadyDeveloping => new(5418);
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellWorkbenchActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(4400);
    internal static ServiceActionResultCode WrongThread => new(4402);
    internal static ServiceActionResultCode IdentityUnavailable => new(4403);
    internal static ServiceActionResultCode SelectionUnavailable => new(4404);
    internal static ServiceActionResultCode WrongSelection => new(4405);
    internal static ServiceActionResultCode AlreadyDiscovered => new(4406);
    internal static ServiceActionResultCode DiscoveryUnavailable => new(4407);
    internal static ServiceActionResultCode RecipeUnavailable => new(4408);
    internal static ServiceActionResultCode Unaffordable => new(4409);
    internal static ServiceActionResultCode LoadoutFull => new(4410);
    internal static ServiceActionResultCode CompositionUnsupported => new(4412);
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4413);
    internal static ServiceActionResultCode PostCommitFault => new(4414);
    internal static ServiceActionResultCode VerificationFailed => new(4415);
    internal static ServiceActionResultCode UsageRequirementsUnavailable => new(4416);
    internal static ServiceActionResultCode UsageUnaffordable => new(4417);
    internal static ServiceActionResultCode UniqueSpellConflict => new(4418);
    internal static ServiceActionResultCode GlyphRequirementsUnavailable => new(4419);
    internal static ServiceActionResultCode StagedWriteFailed => new(4420);
    internal static ServiceActionResultCode AugmentSlotsExceeded => new(4423);
    internal static ServiceActionResultCode ScreenLocked => new(4424);
}

using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class SpellWorkbenchActionResultCodes
{
    internal static ServiceActionResultCode ContractUnavailable => new(4400, "contract_unavailable");
    internal static ServiceActionResultCode WrongThread => new(4402, "wrong_thread");
    internal static ServiceActionResultCode IdentityUnavailable => new(4403, "identity_unavailable");
    internal static ServiceActionResultCode SelectionUnavailable => new(4404, "selection_unavailable");
    internal static ServiceActionResultCode WrongSelection => new(4405, "wrong_selection");
    internal static ServiceActionResultCode AlreadyDiscovered => new(4406, "already_discovered");
    internal static ServiceActionResultCode DiscoveryUnavailable => new(4407, "discovery_unavailable");
    internal static ServiceActionResultCode RecipeUnavailable => new(4408, "recipe_unavailable");
    internal static ServiceActionResultCode Unaffordable => new(4409, "unaffordable");
    internal static ServiceActionResultCode LoadoutFull => new(4410, "loadout_full");
    internal static ServiceActionResultCode CompositionUnsupported => new(4412, "composition_unsupported");
    internal static ServiceActionResultCode MutationPermitUnavailable => new(4413, "action_family_unavailable");
    internal static ServiceActionResultCode PostCommitFault => new(4414, "post_commit_fault");
    internal static ServiceActionResultCode VerificationFailed => new(4415, "verification_failed");
    internal static ServiceActionResultCode UsageRequirementsUnavailable => new(4416, "usage_requirements_unavailable");
    internal static ServiceActionResultCode UsageUnaffordable => new(4417, "usage_budget_unavailable");
    internal static ServiceActionResultCode UniqueSpellConflict => new(4418, "unique_spell_conflict");
    internal static ServiceActionResultCode GlyphRequirementsUnavailable => new(4419, "glyph_requirements_unavailable");
    internal static ServiceActionResultCode StagedWriteFailed => new(4420, "staged_write_failed");
    internal static ServiceActionResultCode AugmentSlotsExceeded => new(4423, "augment_slots_exceeded");
    internal static ServiceActionResultCode ScreenLocked => new(4424, "screen_locked");
    internal static ServiceActionResultCode GlyphUnavailable => new(4425, "glyph_unavailable");
    internal static ServiceActionResultCode GlyphUsagesExceeded => new(4426, "glyph_usages_exceeded");
}

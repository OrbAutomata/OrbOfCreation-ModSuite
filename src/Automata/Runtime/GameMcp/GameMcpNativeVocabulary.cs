#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>
/// The words the surface says instead of the game's raw enum ordinals.
/// </summary>
/// <remarks>
/// <para>
/// A native enum crosses the capture boundary as its underlying integer, because a copied enum
/// would silently re-order the day the game re-orders. That is right for the world; it is wrong for
/// the wire. A live round read <c>castType: 2</c> and <c>rechargeProcessorType: 0</c> in a block
/// whose player word for the same spell sat three lines above it, and the number was the one thing
/// in the block a reader could not use without a lookup table nobody has.
/// </para>
/// <para>
/// Each map is closed and total over the values the pinned build defines, and an ordinal with no
/// word throws rather than reaching a cell. A sixth kind is a game change to model, not a number to
/// pass through — the same rule the worth block's fold kinds have always been held to.
/// </para>
/// </remarks>
internal static class GameMcpNativeVocabulary
{
    /// <summary>
    /// <c>SpellRecipeSO.CastType</c>. Pinned from the game's own predicates rather than from the
    /// declaration order: <c>Spell.IsChanneled()</c> is <c>castType == 1</c>,
    /// <c>Spell.IsToggledSpell()</c> is <c>castType == 1 || castType == 2</c>, and
    /// <c>SpellRecipeSO.HasDuration()</c> is <c>castType &gt; 0</c>.
    /// </summary>
    internal static string CastType(int castType) => castType switch
    {
        0 => "instant",
        1 => "channel",
        2 => "aura",
        _ => throw new InvalidOperationException(Unmapped("SpellRecipeSO.CastType", castType)),
    };

    /// <summary>
    /// <c>Duration.ProcessorType</c> — what a recharge counts down in. Pinned from
    /// <c>Duration.Entry.DurationLabelName()</c>, whose switch answers 0/1/2 with the game's own
    /// labels "Time", "Number of Casts", and "Attributes Developed".
    /// </summary>
    internal static string RechargeProcessorType(int processorType) => processorType switch
    {
        0 => "time",
        1 => "spell-casts",
        2 => "attributes-developed",
        _ => throw new InvalidOperationException(
            Unmapped("Duration.ProcessorType", processorType)),
    };

    /// <summary>
    /// <c>ValueModifier.ValueModifierType</c> — what a modifier does to the number it adjusts, in
    /// the vocabulary <c>docs/game-systems/modifiers.md</c> names the five kinds by. Pinned from
    /// <c>ValueModifier.ToStringValue</c>, whose five-way switch renders 0 as a signed addend, 1 as
    /// a signed percentage, 2 as <c>x</c>, 3 as an inverted-sign percentage, and 4 as <c>^</c>.
    /// </summary>
    internal static string ModifierEffect(int modifierType) => modifierType switch
    {
        (int)GameValueModifierType.Raw => "raw",
        (int)GameValueModifierType.MultiDiminishing => "diminishing",
        (int)GameValueModifierType.MultiStacking => "stacking",
        (int)GameValueModifierType.Reduction => "reduction",
        (int)GameValueModifierType.Exponent => "exponent",
        _ => throw new InvalidOperationException(
            Unmapped("ValueModifier.ValueModifierType", modifierType)),
    };

    /// <summary>
    /// What one authored requirement row actually compares, in words.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>reqType</c> is not one enum. Every condition class carries its own, and the same ordinal
    /// means a different check in each — <c>2</c> is "at least this level" on an upgrade, "at least
    /// this mastery level" on a spell and "any available" on a list. A live round read
    /// <c>reqType | 2</c> in a requirement table and had no way to turn it into a question about
    /// the game.
    /// </para>
    /// <para>
    /// Every map below is pinned from that class's own <c>InternalIsValid</c> switch in the audited
    /// build, so the word describes the comparison the game performs rather than the member name,
    /// which is sometimes a trap: <c>SpellRequirementType</c> spends two ordinals on one check, and
    /// <c>ResearchRequirement</c> reuses <c>UpgradeRequirementType</c> outright.
    /// </para>
    /// <para>
    /// Returns null for the two kinds whose <c>reqType</c> is not a native check ordinal at all:
    /// an unmodelled class, whose row carries <c>-1</c> because nothing was read, and an authored
    /// empty composite, where the suite stores the group's Any/All identity in the same slot. Those
    /// rows say nothing here rather than dressing a sentinel as a comparison.
    /// </para>
    /// </remarks>
    internal static string? RequirementCheck(WorldRequirementConditionKind kind, int reqType) =>
        kind switch
        {
            WorldRequirementConditionKind.Unknown or
            WorldRequirementConditionKind.Literal => null,

            // ResearchRequirement is declared over UpgradeRequirementType, so both read here.
            WorldRequirementConditionKind.Upgrade or
            WorldRequirementConditionKind.Research => reqType switch
            {
                0 => "any-level",
                1 => "at-maximum-level",
                2 => "at-least-level",
                3 => "visible",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.UpgradeRequirementType", reqType)),
            },
            WorldRequirementConditionKind.Structure => reqType switch
            {
                0 => "at-least-quantity",
                1 => "available",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.StructureRequirementType", reqType)),
            },
            WorldRequirementConditionKind.Spell => reqType switch
            {
                0 => "discovered",
                1 => "visible",
                // Two ordinals, one comparison: both branch to the same masteryLevel test.
                2 or 3 => "at-least-mastery-level",
                4 => "at-least-mastery-ready-level",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.SpellRequirementType", reqType)),
            },
            WorldRequirementConditionKind.AlchemyRecipe => reqType switch
            {
                0 => "discovered",
                1 => "visible",
                2 => "at-least-maximum-level",
                3 => "at-least-mastery-level",
                4 => "at-least-advancement-level",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.AlchemyRecipeType", reqType)),
            },
            WorldRequirementConditionKind.Ritual => reqType switch
            {
                0 => "discovered",
                1 => "at-least-reached-level",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.RitualRequirementType", reqType)),
            },
            WorldRequirementConditionKind.Number => reqType switch
            {
                0 => "at-least-value",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.NumberRequirementType", reqType)),
            },
            WorldRequirementConditionKind.Generic => reqType switch
            {
                0 => "visible",
                1 => "at-least-level",
                2 => "discovered",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.GenericRequirementType", reqType)),
            },
            WorldRequirementConditionKind.PrerequisiteLink => reqType switch
            {
                0 => "first-tier-enabled",
                1 => "named-tier-enabled",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.PrerequisiteLinkType", reqType)),
            },
            WorldRequirementConditionKind.List => reqType switch
            {
                0 => "at-least-count",
                1 => "any-visible",
                2 => "any-available",
                _ => throw new InvalidOperationException(
                    Unmapped("Requirements.ListRequirementType", reqType)),
            },
            _ => throw new InvalidOperationException(
                "a requirement condition kind reached the wire as " + kind +
                " with no reqType vocabulary for it; a new condition class is a game change to " +
                "model, not a number to pass through."),
        };

    private static string Unmapped(string nativeEnum, int ordinal) =>
        "a " + nativeEnum + " reached the wire as ordinal " +
        ordinal.ToString(CultureInfo.InvariantCulture) +
        " with no word for it; the mapped values are the whole vocabulary and a new one is a game " +
        "change to model, not a number to pass through.";
}
#endif

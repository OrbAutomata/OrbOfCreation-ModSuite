using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The keyword taxonomies, and the two native facts that make a captured keyword id resolve to the
/// word the player reads.
/// </summary>
/// <remarks>
/// <para>
/// The keyword capture publishes identities, never words. A word arrives from the lifecycle identity
/// catalog, which enumerates <c>IdScriptableObject.RuntimeLookup</c> and records each entry's display
/// name. That resolution therefore stands on one thing: every type asset the word line is built from
/// is itself an <c>IdScriptableObject</c>, so it is in that registry and has a stable uuid to be
/// keyed by. Nothing else in the suite asserts that, and if it stopped being true the keyword table
/// would still publish ids while every word silently became unresolvable.
/// </para>
/// <para>
/// The <c>All</c> registry is the second fact: it is how the vocabulary is enumerable at all — the
/// full authored word list for a taxonomy, including the words no entity currently carries and the
/// global catch-all types that <c>RegisterTypes()</c> joins every member to.
/// </para>
/// <para>
/// <c>ChallengeTypeSO</c> is deliberately in this list even though challenges show no keyword. Its
/// seven assets are effect-targetable with empty display names, so it is exactly the case that proves
/// resolution must read the display name rather than fall back to an asset name: an asset-name
/// fallback would invent seven keywords the game never shows.
/// </para>
/// </remarks>
public sealed class KeywordVocabularyContractTests
{
    [InlineData("StructureTypeSO")]
    [InlineData("ResourceTypeSO")]
    [InlineData("EquipmentTypeSO")]
    [InlineData("SpellTypeSO")]
    [InlineData("ResearchTypeSO")]
    [InlineData("PassiveAbilityTypeSO")]
    [InlineData("ConsumableTypeSO")]
    [InlineData("AlchemyTypeSO")]
    [InlineData("HarvestActionTypeSO")]
    [InlineData("GlyphTypeSO")]
    [InlineData("PlotNodeTypeSO")]
    [InlineData("TimeRuneTypeSO")]
    [InlineData("RitualTypeSO")]
    [InlineData("HarvestTypeSO")]
    [InlineData("CharacterTypeSO")]
    [InlineData("ChallengeTypeSO")]
    [GameAssemblyTheory]
    public void EveryKeywordTaxonomyIsIdentifiedAndEnumerable(string taxonomy)
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.HasType(taxonomy), $"{taxonomy} is no longer declared.");

        var identified = false;
        for (var current = taxonomy; current.Length > 0; current = assembly.GetBaseType(current))
        {
            if (current != "IdScriptableObject") continue;
            identified = true;
            break;
        }

        Assert.True(
            identified,
            $"{taxonomy} no longer derives from IdScriptableObject, so its members are absent from " +
            "RuntimeLookup and every keyword word on them is unresolvable.");
        Assert.Equal(
            "System.Collections.Generic.List`1<" + taxonomy + ">",
            assembly.GetFieldType(taxonomy, "All"));
    }
}

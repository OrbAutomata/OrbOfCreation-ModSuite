using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class SpellWorkbenchContractTests
{
    [GameAssemblyFact]
    public void SpellWorkbenchBindings_PinEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x04000499, assembly.GetFieldToken("SpellManager", "selectedCoreGlyphs"));
        Assert.Equal(0x0400049A, assembly.GetFieldToken("SpellManager", "selectedAugmentGlyphs"));
        Assert.Equal(0x0400049C, assembly.GetFieldToken("SpellManager", "activeSpells"));
        Assert.Equal(0x040004AD, assembly.GetFieldToken("SpellManager", "instance"));
        Assert.Equal(0x04000A32, assembly.GetFieldToken("SpellRecipeSO", "All"));
        Assert.Equal(0x040007DF, assembly.GetFieldToken("Spell", "guidContainer"));
        Assert.Equal(0x04000A6F, assembly.GetFieldToken("AbstractListVariable`1", "value"));

        Assert.Equal(0x06000740, assembly.GetMethodToken("SpellManager", "CreateRecipe"));
        Assert.Equal(0x06000747, assembly.GetMethodToken("SpellManager", "GetSpellFromRecipe"));
        Assert.Equal(0x0600074A, assembly.GetMethodToken("SpellManager", "GetSpellCreateCost"));
        Assert.Equal(0x06000755, assembly.GetMethodToken("SpellManager", "GetUsageCostOfSpell"));
        Assert.Equal(0x06001422, assembly.GetMethodToken("SpellRecipeSO", "CreateEmpty"));
        Assert.Equal(0x0600143E, assembly.GetMethodToken("SpellRecipeSO", "GetSelectedSpellLevel"));
        Assert.Equal(0x06001453, assembly.GetMethodToken("SpellRecipeSO", "HasMetUsageRequirements"));
        Assert.Equal(0x06001442, assembly.GetMethodToken("SpellRecipeSO", "GetDiscoverCost"));
        Assert.Equal(0x06001447, assembly.GetMethodToken("SpellRecipeSO", "GetGlyphRecipe"));
        Assert.Equal(0x0600144F, assembly.GetMethodToken("SpellRecipeSO", "IsCreatable"));
        Assert.Equal(0x06001451, assembly.GetMethodToken("SpellRecipeSO", "CanDiscover"));
        Assert.Equal(0x06000BB6, assembly.GetMethodToken("GlyphSO", "IsAvailable"));
        Assert.Equal(0x06000BB8, assembly.GetMethodToken("GlyphSO", "IsSpellAugment"));
        Assert.Equal(0x06000C05, assembly.GetMethodToken("GlyphSO", "GetCreationCostOfList"));
        Assert.Equal(0x06001569, assembly.GetMethodToken("GenericListVariable`1", "Empty"));
        Assert.Equal(0x06001563, assembly.GetMethodToken("GenericListVariable`1", "Add"));
        Assert.Equal(0x0600155C, assembly.GetMethodToken("EmptyTypeListVariable`1", "HasEmptySpot"));
        Assert.Equal(0x06000FE2, assembly.GetMethodToken("Spell", "SetLevel"));
        Assert.Equal(0x0600100A, assembly.GetMethodToken("Spell", "IsUniqueSpell"));
    }

    [GameAssemblyFact]
    public void SpellCreation_DelegatesToTheResolvedRecipeAndAddsBeforeClearingSelection()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesField(
            "SpellManager", "CreateSpell", "SpellManager", "selectedCoreGlyphs"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellManager", "CreateSpell", "SpellManager", "GetSpellFromRecipe"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellManager", "CreateSpell", "SpellManager", "CreateRecipe"));
        Assert.True(assembly.MethodReferencesField(
            "SpellManager", "CreateRecipe", "SpellManager", "activeSpells"));
        Assert.True(assembly.MethodReferencesField(
            "SpellManager", "CreateRecipe", "SpellManager", "selectedAugmentGlyphs"));

        var create = assembly.MethodReferenceOffset(
            "SpellManager", "CreateRecipe", "SpellRecipeSO", "CreateWith");
        var add = assembly.MethodReferenceOffset(
            "SpellManager", "CreateRecipe", "SpellManager", "AddSpell");
        var clear = ConstructedMemberOffset(
            assembly, "SpellManager", "CreateRecipe", "AbstractListVariable`1<GlyphSO>", "Empty");
        Assert.True(create >= 0, "CreateRecipe must construct from the exact recipe argument.");
        Assert.True(add > create, "The constructed spell must be added to the live loadout.");
        Assert.True(
            clear > add,
            "Selection cleanup must happen only after the new spell is added. Native references: " +
            string.Join("; ", References(assembly, "SpellManager", "CreateRecipe")));
    }

    [GameAssemblyFact]
    public void SpellWorkbenchUi_UsesTheSameResolverCostsAndNativeVerdicts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UICreateSpellButton", "IsGlyphSelectionValid", "SpellRecipeSO", "IsCreatable"));
        Assert.True(assembly.MethodReferencesMethod(
            "UICreateSpellButton", "Render", "SpellManager", "GetSpellCreateCost"));
        Assert.True(assembly.MethodReferencesMethod(
            "UICreateSpellButton", "Render", "ResourceCostList", "HasEnough"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIGlyphList", "SelectGlyph", "IdScriptableObject", "GetGuid"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "Spell", "SetAugmentGlyphs"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "SpellRecipeSO", "GetSelectedSpellLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "Spell", "SetLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "GlyphSO", "MeetsNonLvRequirements"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "UpdateUsage", "SpellManager", "GetUsageCostOfSpell"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "RenderContent", "SpellRecipeSO", "HasMetUsageRequirements"));
        Assert.Contains(
            assembly.GetMethodBodyMemberReferences("UISpellRecipeButton", "RenderContent"),
            reference => reference.MemberName == "HasEmptySpot");
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "ComputeListCompare", "Spell", "IsUniqueSpell"));
    }

    private static string[] References(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Concat(assembly.GetMethodBodyMemberReferences(typeName, methodName))
            .OrderBy(reference => reference.Offset)
            .Select(reference =>
                $"IL_{reference.Offset:X4} 0x{reference.Token:X8} {reference.Kind} " +
                $"{reference.DeclaringType}.{reference.MemberName}")
            .ToArray();

    private static int ConstructedMemberOffset(
        GameAssemblyMetadata assembly,
        string sourceType,
        string sourceMethod,
        string declaringType,
        string memberName) =>
        assembly.GetMethodBodyMemberReferences(sourceType, sourceMethod)
            .Single(reference =>
                reference.DeclaringType == declaringType && reference.MemberName == memberName)
            .Offset;
}

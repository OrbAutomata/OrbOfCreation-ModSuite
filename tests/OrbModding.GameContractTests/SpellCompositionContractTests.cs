using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class SpellCompositionContractTests
{
    [GameAssemblyFact]
    public void SpellCompositionBindings_PinEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0400044B, assembly.GetFieldToken("Player", "_instance"));
        Assert.Equal(0x04000420, assembly.GetFieldToken("Player", "maxSpellOutputLevel"));
        Assert.Equal(0x0400041D, assembly.GetFieldToken("Player", "reserveLevel"));
        Assert.Equal(0x0400041F, assembly.GetFieldToken("Player", "maxReserveLevel"));
        Assert.Equal(0x040004AD, assembly.GetFieldToken("SpellManager", "instance"));
        Assert.Equal(0x0400049C, assembly.GetFieldToken("SpellManager", "activeSpells"));
        Assert.Equal(0x04000A6F, assembly.GetFieldToken("AbstractListVariable`1", "value"));
        Assert.Equal(0x040007DF, assembly.GetFieldToken("Spell", "guidContainer"));
        Assert.Equal(0x040006D9, assembly.GetFieldToken("GlyphSO", "All"));

        Assert.Equal(0x06000690, assembly.GetMethodToken("Player", "GetSpellOutputLevel"));
        Assert.Equal(0x0600068F, assembly.GetMethodToken("Player", "GetReserveLevel"));
        Assert.Equal(0x060015AE, assembly.GetMethodToken("IntVariable", "AsInt"));
        Assert.Equal(0x060015AC, assembly.GetMethodToken("IntVariable", "SetValue"));
        Assert.Equal(0x06001B44, assembly.GetMethodToken("GuidContainer", "get_guid"));
        Assert.Equal(0x06000FA1, assembly.GetMethodToken("Spell", "get_reference"));
        Assert.Equal(0x060007A0, assembly.GetMethodToken("IdScriptableObject", "GetGuid"));
        Assert.Equal(0x06000BB6, assembly.GetMethodToken("GlyphSO", "IsAvailable"));
        Assert.Equal(0x06000BB8, assembly.GetMethodToken("GlyphSO", "IsSpellAugment"));
        Assert.Equal(0x06000BCE, assembly.GetMethodToken("GlyphSO", "GetMaxUsages"));
        Assert.Equal(0x06000C0E, assembly.GetMethodToken("GlyphSO", "MeetsNonLvRequirements"));
        Assert.Equal(0x06000C09, assembly.GetMethodToken("GlyphSO", "GetMasterReqOfList"));
        Assert.Equal(0x06001075, assembly.GetMethodToken("Spell", "GetAugmentGlyphs"));
        Assert.Equal(0x06001049, assembly.GetMethodToken("Spell", "GetQuantityOfGlyph"));
        Assert.Equal(0x06001047, assembly.GetMethodToken("Spell", "GetRecipeMasteryLevel"));
        Assert.Equal(0x06000FAC, assembly.GetMethodToken("Spell", "SetAugmentGlyphs"));
        Assert.Equal(0x060029E8, assembly.GetMethodToken("Stacked.AbstractStackedRecord`2", "Set"));

        Assert.Contains(
            assembly.GetMethods("Stacked.StackedIdRecord`1"),
            method => method.Name == ".ctor" &&
                method.Visibility == "public" &&
                !method.IsStatic &&
                method.ReturnType == "System.Void" &&
                method.ParameterTypes.Count == 0);
    }

    [GameAssemblyFact]
    public void OutputLevel_IsOneGlobalSelectorAndSpellLevelIsDerived()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "GetOutputLevel", "Player", "GetSpellOutputLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "GetLevel", "Spell", "GetOutputLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "GetLevel", "Spell", "GetBaseEffectLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "SetLevel", "Spell", "ComputeCost"));
        Assert.False(assembly.MethodReferencesMethod(
            "Spell", "SetLevel", "Player", "GetSpellOutputLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellInformation", "SetSpellLevel", "Spell", "SetLevel"));
    }

    [GameAssemblyFact]
    public void AugmentCommit_ReplacesExactStackAndRecomputesDerivedState()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var references = References(assembly, "Spell", "SetAugmentGlyphs");
        var replaceIds = Offset(references, "Spell", "augmentGlyphRefs");
        var replaceObjects = Offset(references, "Spell", "augmentGlyphs");
        var load = Offset(references, "SpellRecipeSO", "LoadGlyphs");
        var compute = Offset(references, "Spell", "ComputeCost");

        Assert.True(replaceIds >= 0, "The setter must replace the authored UUID stack.");
        Assert.True(replaceObjects > replaceIds, "The resolved glyph stack must follow the UUID stack.");
        Assert.True(load > replaceObjects, "Recipe-derived state must load from the replacement stack.");
        Assert.True(compute > load, "Costs must be recomputed after replacing the exact stack.");
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "Spell", "SetAugmentGlyphs"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "GlyphSO", "MeetsNonLvRequirements"));
    }

    [GameAssemblyFact]
    public void GlyphPicker_ClampsAgainstNativeMaximumBeforeAttaching()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIGlyphList", "SelectGlyph", "GlyphSO", "GetMaxUsages"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeButton", "AttachSpell", "Spell", "SetAugmentGlyphs"));
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Concat(assembly.GetMethodBodyMemberReferences(typeName, methodName))
            .OrderBy(reference => reference.Offset)
            .ToArray();

    private static int Offset(
        MethodBodyDefinitionReference[] references,
        string declaringType,
        string memberName) =>
        references.Single(reference =>
            reference.DeclaringType == declaringType && reference.MemberName == memberName)
        .Offset;
}

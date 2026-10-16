using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ResearchExplanationContractTests
{
    [GameAssemblyFact]
    public void ResearchExplanation_PinsTheNativePrerequisiteAndCompletionPipeline()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x040008DF, assembly.GetFieldToken("ResearchSO", "levelPrerequisites"));
        Assert.Equal(0x060011D2, assembly.GetMethodToken("ResearchSO", "MeetsLevelRequirements"));
        Assert.Equal(0x060011D4, assembly.GetMethodToken("ResearchSO", "CanDevelop"));
        Assert.Equal(0x060011D5, assembly.GetMethodToken("ResearchSO", "IsWithinDevelopRange"));
        Assert.Equal(0x060011E9, assembly.GetMethodToken("ResearchSO", "IsMaxLevel"));
        Assert.Equal(0x060011EA, assembly.GetMethodToken("ResearchSO", "IsComplete"));
        Assert.Equal(0x060011EF, assembly.GetMethodToken("ResearchSO", "GetBaseLevel"));
        Assert.Equal(0x060011F0, assembly.GetMethodToken("ResearchSO", "GetBonusLevels"));
        Assert.Equal(0x060011F1, assembly.GetMethodToken("ResearchSO", "GetLevel"));

        Assert.True(assembly.MethodReferencesField(
            "ResearchSO", "MeetsLevelRequirements", "ResearchSO", "levelPrerequisites"));
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO", "MeetsLevelRequirements", "ResearchSO", "GetRequirementLevel"));
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO", "CanDevelop", "ResearchSO", "IsWithinDevelopRange"));
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO", "CanDevelop", "ResearchSO", "IsDeveloping"));
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO", "IsComplete", "ResearchSO", "IsMaxLevel"));

        // Completion is based on GetBaseLevel (purchased + base grants), not bonus or total level.
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO", "IsMaxLevel", "ResearchSO", "GetBaseLevel"));
        Assert.False(assembly.MethodReferencesMethod(
            "ResearchSO", "IsMaxLevel", "ResearchSO", "GetBonusLevels"));
        Assert.False(assembly.MethodReferencesMethod(
            "ResearchSO", "IsMaxLevel", "ResearchSO", "GetLevel"));

        // A ResearchRequirement calls the virtual UpgradeableObject.GetLevel slot. ResearchSO's
        // override is total level, so prerequisite leaves deliberately include bonus levels even
        // though completion deliberately does not.
        Assert.Equal(
            0x6F,
            assembly.GetMethodReferenceOpcode(
                "Requirements.ResearchRequirement",
                "InternalIsValid",
                "UpgradeableObject",
                "GetLevel"));
        var baseDispatch = assembly.GetMethodDispatch("UpgradeableObject", "GetLevel");
        var researchDispatch = assembly.GetMethodDispatch("ResearchSO", "GetLevel");
        Assert.True(baseDispatch.IsVirtual);
        Assert.True(researchDispatch.IsVirtual);
        Assert.False(researchDispatch.IsNewSlot);
    }
}

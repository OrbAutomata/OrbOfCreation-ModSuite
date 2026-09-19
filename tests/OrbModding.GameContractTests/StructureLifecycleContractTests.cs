using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class StructureLifecycleContractTests
{
    [GameAssemblyFact]
    public void StructureToggleBindings_PinEveryNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06002765,
            assembly.GetMethodToken("UIStructureList", "ToggleDisableStructure", "StructureSO"));
        Assert.Equal(0x06001787, assembly.GetMethodToken("StructureSO", "ToggleDisabled"));
        Assert.Equal(0x06001788, assembly.GetMethodToken("StructureSO", "DisableStructure"));
        Assert.Equal(0x06001789, assembly.GetMethodToken("StructureSO", "EnableStructure"));
        Assert.Equal(0x0600178F, assembly.GetMethodToken("StructureSO", "ApplyEffects"));
        Assert.Equal(0x06001790, assembly.GetMethodToken("StructureSO", "RemoveEffects"));
        Assert.Equal(0x04000AD5, assembly.GetFieldToken("StructureSO", "disabled"));
    }

    [GameAssemblyFact]
    public void UiToggle_DelegatesToNativeToggleAndEachBranchWritesBeforeEffects()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIStructureList", "ToggleDisableStructure", "StructureSO", "ToggleDisabled"));
        Assert.True(assembly.MethodReferencesField(
            "StructureSO", "ToggleDisabled", "StructureSO", "disabled"));
        Assert.True(assembly.MethodReferencesMethod(
            "StructureSO", "ToggleDisabled", "StructureSO", "DisableStructure"));
        Assert.True(assembly.MethodReferencesMethod(
            "StructureSO", "ToggleDisabled", "StructureSO", "EnableStructure"));

        var disableReferences = assembly.GetMethodBodyDefinitionReferences(
            "StructureSO", "DisableStructure");
        var enableReferences = assembly.GetMethodBodyDefinitionReferences(
            "StructureSO", "EnableStructure");
        Assert.True(Offset(disableReferences, "StructureSO", "disabled") <
            Offset(disableReferences, "StructureSO", "RemoveEffects"));
        Assert.True(Offset(enableReferences, "StructureSO", "disabled") <
            Offset(enableReferences, "StructureSO", "ApplyEffects"));
    }

    private static int Offset(
        System.Collections.Generic.IReadOnlyList<MethodBodyDefinitionReference> references,
        string declaringType,
        string memberName) =>
        references.Single(reference =>
            reference.DeclaringType == declaringType &&
            reference.MemberName == memberName).Offset;
}

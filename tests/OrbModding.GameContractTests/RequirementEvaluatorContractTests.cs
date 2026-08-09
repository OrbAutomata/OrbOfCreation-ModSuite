using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class RequirementEvaluatorContractTests
{
    /// <summary>
    /// The any-available list fold answers from the consumable rows' published visibility, which is
    /// only the same question the game asks while its two gates return the one stored field. Neither
    /// gate is called by this suite, so nothing else would notice them diverging.
    /// </summary>
    [GameAssemblyFact]
    public void AConsumableAnswersAvailableAndVisibleFromTheOneStoredGate()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.Boolean", assembly.GetFieldType("ConsumableSO", "visible"));
        Assert.Equal(
            "System.Boolean",
            Assert.Single(assembly.GetMethods("ConsumableSO", "IsAvailable")).ReturnType);
        Assert.True(assembly.MethodReferencesField(
            "ConsumableSO", "IsAvailable", "ConsumableSO", "visible"));
        Assert.True(assembly.MethodReferencesField(
            "ConsumableSO", "IsVisible", "ConsumableSO", "visible"));

        // The field read is the whole body: no discovery walk, no latch, nothing a read could move.
        Assert.All(
            new[] { "IsAvailable", "IsVisible" },
            gate => Assert.Equal(
                new[] { "ConsumableSO.visible" },
                assembly.GetMethodBodyDefinitionReferences("ConsumableSO", gate)
                    .Concat(assembly.GetMethodBodyMemberReferences("ConsumableSO", gate))
                    .Select(reference => reference.DeclaringType + "." + reference.MemberName)
                    .ToArray()));
    }

    [GameAssemblyFact]
    public void StructureQuantityRequirementUsesPurchasedQuantityNotGrantedOrTotalLevels()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var evaluator = Assert.Single(
            assembly.GetMethods("Requirements.StructureRequirement", "InternalIsValid"));
        Assert.Equal("System.Boolean", evaluator.ReturnType);
        Assert.True(assembly.MethodReferencesField(
            "Requirements.StructureRequirement",
            "InternalIsValid",
            "StructureSO",
            "quantity"));
        Assert.False(assembly.MethodReferencesField(
            "Requirements.StructureRequirement",
            "InternalIsValid",
            "StructureSO",
            "selfBonusLevels"));
    }
}

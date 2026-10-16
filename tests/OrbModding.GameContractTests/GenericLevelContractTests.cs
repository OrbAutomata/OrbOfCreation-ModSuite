using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class GenericLevelContractTests
{
    [GameAssemblyFact]
    public void AuditedLevelInterfacesHaveTheExactSixAndThreeTypeMatrix()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(new[]
        {
            "EquipmentTypeSO", "GlyphSO", "ResearchSO", "ResourceTypeSO",
            "SpellRecipeSO", "TimeRuneSO",
        }, assembly.GetTypesImplementing("ILevelable"));
        Assert.Equal(new[]
        {
            "EquipmentTypeSO", "GlyphSO", "ResourceTypeSO",
        }, assembly.GetTypesImplementing("ILevelableHasFree"));
    }

    [GameAssemblyFact]
    public void VisibleLevelButtonsUseTheInterfaceCallbacksAndUsageAdmission()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06002464, assembly.GetMethodToken("UILevelableItem", "SetupCostButton"));
        Assert.Equal(0x06002465, assembly.GetMethodToken("UILevelableItem", "RenderFreeLevelButton"));
        Assert.Equal(0x06002467, assembly.GetMethodToken("UILevelableItem", "PurchaseLevel"));
        Assert.Equal(0x06002468, assembly.GetMethodToken("UILevelableItem", "PurchaseFreeLevel"));
        Assert.Contains(References(assembly, "UILevelableItem", "PurchaseLevel"),
            reference => reference.DeclaringType == "ILevelable" &&
                         reference.MemberName == "PurchaseLevel");
        Assert.Contains(References(assembly, "UILevelableItem", "PurchaseFreeLevel"),
            reference => reference.DeclaringType == "ILevelableHasFree" &&
                         reference.MemberName == "PurchaseFreeLevel");
        var bonus = References(assembly, "UILevelableItem", "RenderFreeLevelButton");
        Assert.Contains(bonus, reference => reference.DeclaringType == "ResourceCostList" &&
                                            reference.MemberName == "AllResourcesVisible");
        Assert.Contains(bonus, reference => reference.DeclaringType == "ResourceCostList" &&
                                            reference.MemberName == "HasEnough");
    }

    [GameAssemblyFact]
    public void ConcretePaidLevelCallbacksMatchTheAuditedTokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06000B66, assembly.GetMethodToken("EquipmentTypeSO", "PurchaseLevel"));
        Assert.Equal(0x06000B78, assembly.GetMethodToken("EquipmentTypeSO", "GetLevelCost"));
        Assert.Equal(0x06000BBA, assembly.GetMethodToken("GlyphSO", "PurchaseLevel"));
        Assert.Equal(0x06000BCF, assembly.GetMethodToken("GlyphSO", "GetLevelCost"));
        Assert.Equal(0x0600134B, assembly.GetMethodToken("ResourceTypeSO", "PurchaseLevel"));
        Assert.Equal(0x0600134E, assembly.GetMethodToken("ResourceTypeSO", "GetLevelCost"));
        Assert.Equal(0x06001847, assembly.GetMethodToken("TimeRuneSO", "PurchaseLevel"));
        Assert.Equal(0x06001849, assembly.GetMethodToken("TimeRuneSO", "GetLevelCost"));
    }

    [GameAssemblyFact]
    public void ConcreteBonusLevelCallbacksMatchTheAuditedTokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06000B67, assembly.GetMethodToken("EquipmentTypeSO", "PurchaseFreeLevel"));
        Assert.Equal(0x06000B79, assembly.GetMethodToken("EquipmentTypeSO", "GetFreeLevelCost"));
        Assert.Equal(0x06000BBB, assembly.GetMethodToken("GlyphSO", "PurchaseFreeLevel"));
        Assert.Equal(0x06000BD3, assembly.GetMethodToken("GlyphSO", "GetFreeLevelCost"));
        Assert.Equal(0x0600134C, assembly.GetMethodToken("ResourceTypeSO", "PurchaseFreeLevel"));
        Assert.Equal(0x06001334, assembly.GetMethodToken("ResourceTypeSO", "GetFreeLevelCost"));
    }

    [Fact]
    public void ManifestNamesTheCompleteUnifiedLevelBindingSet()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "generic-level.levelable.type-action", "generic-level.free-levelable.type-action",
            "generic-level.equipment-type.type-action", "generic-level.glyph.type-action",
            "generic-level.resource-type.type-action", "generic-level.time-rune.type-action",
            "generic-level.research.type-delegated", "generic-level.spell-recipe.type-delegated",
            "generic-level.cost.type-action", "generic-level.tuple.type-action",
            "generic-level.resource.type-action", "generic-level.equipment-get-level-action",
            "generic-level.equipment-can-level-action", "generic-level.equipment-get-cost-action",
            "generic-level.equipment-purchase-action", "generic-level.glyph-get-level-action",
            "generic-level.glyph-can-level-action", "generic-level.glyph-get-cost-action",
            "generic-level.glyph-purchase-action", "generic-level.resource-type-get-level-action",
            "generic-level.resource-type-can-level-action", "generic-level.resource-type-get-cost-action",
            "generic-level.resource-type-purchase-action", "generic-level.time-rune-get-level-action",
            "generic-level.time-rune-can-level-action", "generic-level.time-rune-get-cost-action",
            "generic-level.time-rune-purchase-action", "generic-level.equipment-get-bonus-level-action",
            "generic-level.equipment-get-bonus-cost-action", "generic-level.equipment-purchase-bonus-action",
            "generic-level.glyph-get-bonus-level-action", "generic-level.glyph-get-bonus-cost-action",
            "generic-level.glyph-purchase-bonus-action", "generic-level.resource-type-get-bonus-level-action",
            "generic-level.resource-type-get-bonus-cost-action", "generic-level.resource-type-purchase-bonus-action",
            "generic-level.cost-has-enough-action", "generic-level.cost-resources-visible-action",
            "generic-level.cost-entries-action", "generic-level.tuple-resource-action",
            "generic-level.tuple-value-action", "generic-level.resource-guid-action",
            "generic-level.resource-has-amount-action",
            "generic-level.equipment-visible-action", "generic-level.equipment-available-action",
            "generic-level.glyph-visible-action", "generic-level.glyph-available-action",
            "generic-level.resource-type-visible-action",
            "generic-level.resource-type-available-action", "generic-level.resource-type-hidden-action",
            "generic-level.time-rune-visible-action", "generic-level.time-rune-available-action",
            "generic-level.time-rune-discovered-action",
        };

        Assert.All(expected, id => Assert.Single(
            manifest.Contracts,
            contract => contract.Id == id));
    }

    private static MethodBodyDefinitionReference[] References(
        GameAssemblyMetadata assembly,
        string type,
        string method) =>
        assembly.GetMethodBodyDefinitionReferences(type, method)
            .Concat(assembly.GetMethodBodyMemberReferences(type, method))
            .OrderBy(reference => reference.Offset)
            .ToArray();
}

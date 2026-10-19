using Xunit;
using static OrbModding.GameContractTests.MethodBodyReferences;

namespace OrbModding.GameContractTests;

public sealed class ResearchLifecycleContractTests
{
    [GameAssemblyFact]
    public void Ui_verbs_delegate_to_the_exact_research_identity_members()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x060025F0, assembly.GetMethodToken("UIResearchItem", "DevelopResearch"));
        Assert.Equal(0x060025F1, assembly.GetMethodToken("UIResearchItem", "PauseResearch"));
        Assert.Equal(0x060025F2, assembly.GetMethodToken("UIResearchItem", "ResumeResearch"));
        Assert.Equal(0x060025F7, assembly.GetMethodToken("UIResearchItem", "CancelDevelopment"));
        Assert.Equal(0x060025F8, assembly.GetMethodToken("UIResearchItem", "AddBonusLevel"));
        Assert.True(assembly.MethodReferencesMethod("UIResearchItem", "DevelopResearch",
            "ResearchSO", "PurchaseLevel"));
        Assert.True(assembly.MethodReferencesMethod("UIResearchItem", "PauseResearch",
            "ResearchSO", "PauseResearch"));
        Assert.True(assembly.MethodReferencesMethod("UIResearchItem", "ResumeResearch",
            "ResearchSO", "ResumeResearch"));
        Assert.True(assembly.MethodReferencesMethod("UIResearchItem", "CancelDevelopment",
            "ResearchSO", "CancelDevelopment"));
        Assert.True(assembly.MethodReferencesMethod("UIResearchItem", "AddBonusLevel",
            "ResearchSO", "SubmitBonusLevel"));
    }

    [GameAssemblyFact]
    public void Purchase_level_dispatches_between_immediate_and_internal_queue_routes()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x060011B4, assembly.GetMethodToken("ResearchSO", "PurchaseLevel"));
        Assert.Equal(0x060011B5, assembly.GetMethodToken("ResearchSO", "Develop"));
        Assert.Equal(0x060011B6, assembly.GetMethodToken("ResearchSO", "QueueDevelopment"));
        Assert.True(assembly.MethodReferencesMethod("ResearchSO", "PurchaseLevel",
            "SettingsManager", "IsResearchQueueMode"));
        Assert.True(assembly.MethodReferencesMethod("ResearchSO", "PurchaseLevel",
            "ResearchSO", "Develop"));
        Assert.True(assembly.MethodReferencesMethod("ResearchSO", "PurchaseLevel",
            "ResearchSO", "QueueDevelopment"));
    }

    [GameAssemblyFact]
    public void Queue_route_accumulates_exact_cost_and_range_before_committing_waiting_levels()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var references = References(assembly, "ResearchSO", "QueueDevelopment");
        Assert.True(Offset(references, "GlobalVariables", "GetMultiBuy") <
                    Offset(references, "ResourceCostList", "Add"));
        Assert.True(Offset(references, "ResourceCostList", "Add") <
                    Offset(references, "ResourceCostList", "HasEnough"));
        Assert.True(Offset(references, "ResourceCostList", "HasEnough") <
                    Offset(references, "ResearchSO", "IsWithinDevelopRangeAt"));
        Assert.True(Offset(references, "ResearchSO", "IsWithinDevelopRangeAt") <
                    Offset(references, "ResearchSO", "ApplyResearchCost"));
    }

    [GameAssemblyFact]
    public void Cancel_and_bonus_keep_the_audited_state_and_capacity_semantics()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x060011B7, assembly.GetMethodToken("ResearchSO", "CancelDevelopment"));
        Assert.Equal(0x060011BA, assembly.GetMethodToken("ResearchSO", "SubmitBonusLevel"));
        Assert.True(assembly.MethodReferencesMethod("ResearchSO", "CancelDevelopment",
            "ResourceFillList", "ClearInvestment"));
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO+<>c", "<CanApplyBonusLevels>b__101_0",
            "ResearchTypeSO", "HasFreeBonusLevelsLeft"));
        Assert.True(assembly.MethodReferencesMethod("ResearchSO", "SubmitBonusLevel",
            "ResearchSO", "ApplySelfBonusLevels"));
    }

    [Fact]
    public void Manifest_names_every_research_touch_only_this_list_guards()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "research-action.globals.type-capture", "research-action.int-variable.type-capture",
            "research-action.multi-buy-capture", "research-action.int-as-int-capture",
            "research-decision.tuple.type-capture", "research-decision.resource.type-capture",
            "research-decision.fill.type-capture", "research-decision.fill-entry.type-capture",
            "research-decision.research-type.type-capture",
            "research-decision.cost-entries-capture", "research-decision.cost-resource-capture",
            "research-decision.cost-value-capture", "research-decision.resource-guid-capture",
            "research-decision.resource-amount-capture",
            "research-decision.fill-resource-capture", "research-decision.fill-quantity-capture",
            "research-decision.research-type-guid-capture",
            "research-decision.type-investment-capture",
        };
        Assert.All(expected, id => Assert.Single(manifest.Contracts, contract => contract.Id == id));
    }
}

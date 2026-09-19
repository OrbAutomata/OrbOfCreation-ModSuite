using System;
using System.Linq;
using Xunit;

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
        var references = assembly.GetMethodBodyDefinitionReferences(
                "ResearchSO", "QueueDevelopment")
            .Concat(assembly.GetMethodBodyMemberReferences("ResearchSO", "QueueDevelopment"))
            .OrderBy(reference => reference.Offset)
            .ToArray();
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
    public void Manifest_names_every_research_action_and_decision_touch()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "research-action.research.type-action", "research-action.cost.type-action",
            "research-action.settings.type-action", "research-action.globals.type-action",
            "research-action.int-variable.type-action", "research-action.level-action",
            "research-action.self-bonus-action", "research-action.active-action",
            "research-action.developing-action", "research-action.max-level-action",
            "research-action.can-develop-action", "research-action.can-bonus-action",
            "research-action.queued-levels-action", "research-action.free-bonus-action",
            "research-action.development-cost-action", "research-action.cost-enough-action",
            "research-action.queue-mode-action", "research-action.multi-buy-action",
            "research-action.int-as-int-action", "research-action.purchase-action",
            "research-action.pause-action", "research-action.resume-action",
            "research-action.cancel-action", "research-action.submit-bonus-action",
            "research-action.has-max-level-action",
            "research-action.development-cost-at-level-action",
            "research-action.within-range-at-action", "research-action.cost-add-action",
            "research-decision.tuple.type-capture", "research-decision.resource.type-capture",
            "research-decision.fill.type-capture", "research-decision.fill-entry.type-capture",
            "research-decision.research-type.type-capture",
            "research-decision.current-time-capture", "research-decision.remaining-time-capture",
            "research-decision.cost-entries-capture", "research-decision.cost-resource-capture",
            "research-decision.cost-value-capture", "research-decision.resource-guid-capture",
            "research-decision.resource-amount-capture", "research-decision.fill-list-capture",
            "research-decision.fill-entries-capture", "research-decision.fill-resource-capture",
            "research-decision.fill-quantity-capture", "research-decision.fill-capacity-capture",
            "research-decision.fill-remaining-capture", "research-decision.research-types-capture",
            "research-decision.research-type-guid-capture",
            "research-decision.remaining-bonus-capture",
            "research-decision.type-investment-capture",
            "research-decision.type-maximum-investment-capture",
        };
        Assert.Equal(51, expected.Length);
        Assert.All(expected, id => Assert.Single(manifest.Contracts, contract => contract.Id == id));
    }

    private static int Offset(MethodBodyDefinitionReference[] references,
        string type, string member) => references.Where(reference =>
            reference.DeclaringType.StartsWith(type, StringComparison.Ordinal) &&
            reference.MemberName == member).Select(reference => reference.Offset)
            .DefaultIfEmpty(-1).Min();
}

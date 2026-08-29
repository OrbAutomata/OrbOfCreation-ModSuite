using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class AutoHarvestContractTests
{
    [GameAssemblyFact]
    public void CandidateAudit_MatchesNativePlotIdentityReadinessCostAndActionSlotSurface()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2<System.Guid,IdScriptableObject>",
            assembly.GetFieldType("IdScriptableObject", "RuntimeLookup"));
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");

        Assert.Equal("System.Collections.Generic.List`1<PlotNodeSO>", assembly.GetFieldType("PlotNodeSO", "All"));
        Assert.Equal(
            "System.Collections.Generic.List`1<PlotNodeActionSO>",
            assembly.GetFieldType("PlotNodeSO", "availableActions"));
        Assert.Equal(
            "System.Collections.Generic.List`1<PlotNodeSO+PlotNodePhaseInstance>",
            assembly.GetFieldType("PlotNodeSO", "phaseInstances"));
        Assert.Equal(
            "System.Collections.Generic.List`1<PlotNodeSO+PlotNodePhaseInfo>",
            assembly.GetFieldType("PlotNodeSO", "phaseInfos"));
        Assert.Equal("PlotNodeActionSO", assembly.GetFieldType("PlotNodeSO", "autoAction"));
        AssertMethod(assembly, "PlotNodeSO", "IsVisible", false, "System.Boolean");
        AssertMethod(
            assembly,
            "PlotNodeSO",
            "GetQuantity",
            false,
            "System.Int32",
            "PlotNodeSO+PlotNodePhases");
        AssertMethod(
            assembly,
            "PlotNodeSO",
            "GetPhaseInstance",
            false,
            "PlotNodeSO+PlotNodePhaseInstance",
            "PlotNodeSO+PlotNodePhases");
        AssertMethod(
            assembly,
            "PlotNodeSO",
            "GetActionInstances",
            false,
            "System.Collections.Generic.List`1<PlotNodeActionInstance>");
        AssertMethod(assembly, "PlotNodeSO", "GetRemainingQuantity", false, "System.Int32");
        AssertMethod(assembly, "PlotNodeSO", "GetTotalQuantity", false, "System.Int32");

        AssertMethod(assembly, "PlotNodeSO+PlotNodePhaseInstance", "GetQuantity", false, "System.Int32");
        AssertMethod(
            assembly,
            "PlotNodeSO+PlotNodePhaseInstance",
            "GetExpiredThisFrame",
            false,
            "System.Int32");

        Assert.Equal(
            "System.Collections.Generic.List`1<PlotNodeActionSO>",
            assembly.GetFieldType("PlotNodeActionSO", "All"));
        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("PlotNodeActionSO", "prerequisites"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("Prerequisites+Container", "available"));
        AssertMethod(assembly, "Prerequisites+Container", "Check", false, "System.Boolean");
        Assert.Equal(
            "PlotNodeActionSO+CostType",
            assembly.GetFieldType("PlotNodeActionSO", "elementCostType"));
        Assert.Equal(
            "PlotNodeSO+PlotNodePhases",
            assembly.GetFieldType("PlotNodeActionSO", "elementCostExitPhase"));
        Assert.Equal("System.Int32", assembly.GetFieldType("PlotNodeActionSO", "elementCost"));
        Assert.Equal("ResourceCostList", assembly.GetFieldType("PlotNodeActionSO", "actionDrain"));
        Assert.Equal(
            "System.Collections.Generic.List`1<PersistentEffectBlock>",
            assembly.GetFieldType("PlotNodeActionSO", "actionEffects"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("PlotNodeActionSO", "parallelAction"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("PlotNodeActionSO", "ignoreNodeYield"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("PlotNodeActionSO", "isGrowingAction"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("PlotNodeActionSO", "useSizeModForCost"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("PlotNodeActionSO", "useAnyStateForCost"));
        Assert.Equal("System.Double", assembly.GetFieldType("PlotNodeActionSO", "baseTime"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("PlotNodeActionSO", "useSpaceUsageForTimeMult"));
        Assert.Equal(
            "System.Collections.Generic.List`1<InstantEffectBlock>",
            assembly.GetFieldType("PlotNodeActionSO", "completeEffects"));
        Assert.True(assembly.HasType("PlotNodeActionSO+CostType"));
        AssertMethod(
            assembly,
            "PlotNodeActionSO",
            "GetElementCost",
            false,
            "System.Int32",
            "PlotNodeSO");
        AssertMethod(assembly, "PlotNodeActionSO", "HasCompleteEffects", false, "System.Boolean");
        AssertMethod(assembly, "PlotNodeActionSO", "IsParallelAction", false, "System.Boolean");

        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            ".ctor",
            false,
            "System.Void",
            "PlotNodeSO",
            "PlotNodeActionSO");
        AssertMethod(assembly, "PlotNodeActionInstance", "GetAction", false, "PlotNodeActionSO");
        AssertMethod(assembly, "PlotNodeActionInstance", "GetElement", false, "PlotNodeSO");
        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "GetResourceCost",
            false,
            "ResourceCostList");
        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "HasEnoughForOneInstance",
            false,
            "System.Boolean");
        AssertMethod(assembly, "PlotNodeActionInstance", "IsVisible", false, "System.Boolean");
        AssertMethod(assembly, "PlotNodeActionInstance", "IsEmpty", false, "System.Boolean");
        AssertMethod(assembly, "PlotNodeActionInstance", "IsEngaged", false, "System.Boolean");
        AssertMethod(assembly, "PlotNodeActionInstance", "GetActualQuantity", false, "System.Int32");
        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "GetMaximumRemInstances",
            false,
            "System.Int32");
        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "GetMaximumInstances",
            false,
            "System.Int32");
        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "IsAtMinimumQuantity",
            false,
            "System.Boolean");
        AssertMethod(assembly, "PlotNodeActionInstance", "Cancel", false, "System.Void");
        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "GetMinimumInstances",
            false,
            "System.Int32");

        AssertMethod(
            assembly,
            "PlotNodeActionInstanceListVariable",
            "FindInstance",
            false,
            "PlotNodeActionInstance",
            "PlotNodeActionInstance");
        AssertMethod(
            assembly,
            "PlotNodeActionInstanceListVariable",
            "HasInstance",
            false,
            "System.Boolean",
            "PlotNodeActionInstance");
        AssertMethod(
            assembly,
            "PlotNodeActionInstanceListVariable",
            "AddInstance",
            false,
            "System.Void",
            "PlotNodeActionInstance",
            "System.Int32");
        AssertMethod(
            assembly,
            "PlotNodeActionInstanceListVariable",
            "RemoveInstance",
            false,
            "System.Void",
            "PlotNodeActionInstance",
            "System.Int32");

        Assert.Equal(
            "System.Collections.Generic.List`1<!0>",
            assembly.GetFieldType("AbstractListVariable`1", "value"));
        Assert.Equal("IntVariable", assembly.GetFieldType("AbstractListVariable`1", "maxSizeVariable"));
        AssertMethod(
            assembly,
            "AbstractListVariable`1",
            "ToList",
            false,
            "System.Collections.Generic.List`1<!0>");
        AssertMethod(assembly, "EmptyTypeListVariable`1", "HasEmptySpot", false, "System.Boolean");
        AssertMethod(assembly, "EmptyTypeListVariable`1", "GetUsedSpots", false, "System.Int32");
        AssertMethod(
            assembly,
            "UIPlotNodeActionList",
            "OnActionClick",
            false,
            "System.Void",
            "PlotNodeActionInstance");
        AssertMethod(
            assembly,
            "UIPlotNodeList",
            "OnNodeClick",
            false,
            "System.Void",
            "PlotNodeSO");
    }

    [GameAssemblyFact]
    public void CompletionAudit_MatchesNativePhaseCycleAndTreasureRewardSurface()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertEnum(assembly, "PlotNodeActionSO+CostType", ("Destroy", 0), ("ExitPhase", 1));
        AssertEnum(assembly, "PlotNodeSO+PlotNodePhases", ("Idle", 0), ("Growing", 1), ("Resting", 2));
        AssertEnum(assembly, "TimerList+TimerType", ("Single", 0), ("Parallel", 1), ("Idle", 2));
        AssertEnum(assembly, "FilterEffectMod+FilterType", ("BlackList", 0), ("WhiteList", 1));

        Assert.Equal(
            "PlotNodeSO+PlotNodePhases",
            assembly.GetFieldType("PlotNodeSO+PlotNodePhaseInfo", "phase"));
        Assert.Equal(
            "System.Double",
            assembly.GetFieldType("PlotNodeSO+PlotNodePhaseInfo", "phaseTime"));
        Assert.Equal(
            "TimerList+TimerType",
            assembly.GetFieldType("PlotNodeSO+PlotNodePhaseInfo", "processType"));
        Assert.Equal(
            "PlotNodeSO+PlotNodePhases",
            assembly.GetFieldType("PlotNodeSO+PlotNodePhaseInfo", "exitPhase"));
        AssertMethod(
            assembly,
            "PlotNodeSO",
            "SpendQuantity",
            false,
            "System.Void",
            "PlotNodeSO+PlotNodePhases",
            "System.Int32",
            "System.Boolean");
        AssertMethod(
            assembly,
            "PlotNodeSO",
            "CreateQuantity",
            false,
            "System.Void",
            "PlotNodeSO+PlotNodePhases",
            "System.Int32");

        AssertMethod(
            assembly,
            "PlotNodeActionInstance",
            "PlayerChangeInstanceQuantity",
            false,
            "System.Void",
            "System.Int32");

        Assert.Equal(
            "ScalingWeightRef",
            assembly.GetFieldType("ScalingWeightEffectMod", "scalingWeightRef"));
        Assert.Equal(
            "ScalingWeightSO",
            assembly.GetFieldType("ScalingWeightRef", "scalingWeight"));

        Assert.Equal(
            "System.Collections.Generic.List`1<Requirements.IRequirementCondition>",
            assembly.GetFieldType("Prerequisites+Container", "prerequisites"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ResourceTuple>",
            assembly.GetFieldType("ResourceCostList", "costs"));
        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("EffectBlock", "prerequisites"));
        Assert.Equal(
            "System.Collections.Generic.List`1<IEffectMod>",
            assembly.GetFieldType("EffectBlock", "effectMods"));
        Assert.Equal(
            "System.Collections.Generic.List`1<IInstantEffectScript>",
            assembly.GetFieldType("InstantEffectBlock", "effectScripts"));

        Assert.Equal(
            "TreasurePoolSO",
            assembly.GetFieldType("TreasurePoolSO+TreasurePoolInstantEffect", "treasurePool"));
        Assert.Equal(
            "System.String",
            assembly.GetFieldType("TreasurePoolSO+TreasurePoolInstantEffect", "effectType"));
        Assert.Equal(
            "System.Double",
            assembly.GetFieldType("TreasurePoolSO+TreasurePoolInstantEffect", "effectValue"));
        Assert.Equal(
            "FilterEffectMod",
            assembly.GetFieldType("TreasurePoolSO+TreasurePoolInstantEffect", "filterScaling"));
        Assert.Equal("FilterEffectMod+FilterType", assembly.GetFieldType("FilterEffectMod", "listType"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ScalingType>",
            assembly.GetFieldType("FilterEffectMod", "listContents"));
        AssertMethod(
            assembly,
            "TreasurePoolSO",
            "EarnPartialTreasure",
            false,
            "System.Collections.Generic.List`1<TreasurePoolSO+TreasurePoolResult>",
            "ScalingInfo",
            "BigDouble");
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameterTypes)
    {
        var matches = assembly.GetMethods(typeName, methodName);
        Assert.Contains(matches, method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameterTypes));
    }

    private static void AssertEnum(
        GameAssemblyMetadata assembly,
        string typeName,
        params (string Name, int Value)[] expected)
    {
        var actual = assembly.GetInt32EnumMembers(typeName);
        Assert.Equal(expected.Length, actual.Count);
        foreach (var member in expected)
            Assert.Equal(member.Value, actual[member.Name]);
    }
}

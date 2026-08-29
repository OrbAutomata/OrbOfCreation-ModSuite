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

    /// <summary>
    /// The container the suite reads to explain a lock is the one the game's own gate asks.
    /// </summary>
    /// <remarks>
    /// Each family keeps a per-level container and an unlock container, and reading the wrong one is
    /// what published <c>Met</c> beside <c>state: locked</c>. Research keeps two unlock containers and
    /// ANDs them, which is why the suite publishes both under one program rather than picking one.
    /// </remarks>
    [GameAssemblyFact]
    public void TheUnlockGateIsTheContainerTheSuiteReadsForIt()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("UpgradeSO", "prerequisites"));
        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("StructureSO", "prerequisites"));
        Assert.Equal(
            "Prerequisites+Container",
            assembly.GetFieldType("ResearchSO", "visibilityPrerequisites"));
        Assert.Equal(
            "Prerequisites+Container",
            assembly.GetFieldType("ResearchSO", "levelVisibilityPrereq"));

        Assert.True(assembly.MethodReferencesField(
            "UpgradeSO", "IsAvailable", "UpgradeSO", "prerequisites"));
        Assert.True(assembly.MethodReferencesField(
            "StructureSO", "IsAvailable", "StructureSO", "prerequisites"));

        // Both, which is why the published program needs a group-ordinal base to hold two containers.
        Assert.True(assembly.MethodReferencesField(
            "ResearchSO", "IsVisible", "ResearchSO", "visibilityPrerequisites"));
        Assert.True(assembly.MethodReferencesField(
            "ResearchSO", "IsVisible", "ResearchSO", "levelVisibilityPrereq"));

        // The unlock container is not the per-level one. Were these the same field the suite would be
        // publishing one authored list twice under two programs.
        Assert.True(assembly.MethodReferencesField(
            "UpgradeSO", "HasMetQueuedLevelRequirements", "UpgradeSO", "prerequisitesPerLevel"));
        Assert.False(assembly.MethodReferencesField(
            "UpgradeSO", "HasMetQueuedLevelRequirements", "UpgradeSO", "prerequisites"));
    }

    /// <summary>
    /// Only the no-argument <c>Check()</c> latches, and only it applies the container's adjustment.
    /// </summary>
    /// <remarks>
    /// Both halves matter to the suite. The latch is why capture reads the container's rows instead of
    /// calling it, and why the differential asks the parameterised overload for the game's own answer.
    /// The adjustment is why an unlock threshold is not simply the authored one:
    /// <c>ResearchSO.Initialize()</c> builds <c>levelVisibilityPrereq</c> as
    /// <c>levelPrerequisites.Filter().SetAdjustValue(-levelVisibilityRange)</c>, and
    /// <c>ConditionValueInstance</c> adds it to every threshold it folds.
    /// </remarks>
    [GameAssemblyFact]
    public void OnlyTheLatchingCheckAppliesTheContainersOwnAdjustment()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("BigDouble", assembly.GetFieldType("Prerequisites+Container", "adjustValue"));

        var latching = assembly
            .GetMethodBodyDefinitionReferences(
                "Prerequisites+Container", "Check", System.Array.Empty<string>())
            .Select(reference => reference.DeclaringType + "." + reference.MemberName)
            .ToArray();
        Assert.Contains("Prerequisites+Container.available", latching);
        Assert.Contains("Prerequisites+Container.adjustValue", latching);
        Assert.Contains("Prerequisites+Container.CheckGameId", latching);
        Assert.Contains("Requirements.ConditionInfo.Adjust", latching);

        var parameterised = assembly
            .GetMethodBodyDefinitionReferences(
                "Prerequisites+Container", "Check", "Requirements.ConditionInfo")
            .Select(reference => reference.DeclaringType + "." + reference.MemberName)
            .ToArray();
        Assert.DoesNotContain("Prerequisites+Container.available", parameterised);
        Assert.DoesNotContain("Prerequisites+Container.adjustValue", parameterised);
        Assert.DoesNotContain("Prerequisites+Container.CheckGameId", parameterised);

        // The suite reproduces the adjustment from the stored field, so nothing outside the container
        // may write it behind the reader's back.
        Assert.All(
            assembly
                .GetFieldUseSites(new[] { ("Prerequisites+Container", "adjustValue") })
                .Where(site => site.Use == "store"),
            site => Assert.Equal("Prerequisites+Container", site.MethodOwner));

        // Where the adjustment lands: on the threshold, after the level scaling, not on the level.
        var instance = assembly
            .GetMethodBodyDefinitionReferences("Requirements.ConditionValueInstance", ".ctor")
            .Select(reference => reference.DeclaringType + "." + reference.MemberName)
            .ToArray();
        Assert.Contains("Requirements.ConditionInfo.adjustValue", instance);
        Assert.Contains("Requirements.LeveledValue.AtCondition", instance);

        // And where it comes from: the one container the game authors an adjustment for.
        Assert.True(assembly.MethodReferencesMethod(
            "ResearchSO", "Initialize", "Prerequisites+Container", "SetAdjustValue"));
        Assert.True(assembly.MethodReferencesField(
            "ResearchSO", "Initialize", "ResearchSO", "levelVisibilityRange"));
        Assert.True(assembly.MethodReferencesField(
            "ResearchSO", "Initialize", "ResearchSO", "levelVisibilityPrereq"));
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

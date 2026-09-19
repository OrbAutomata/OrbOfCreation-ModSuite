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
    /// A resource's ledger bit is a stored field, and the container that moves it is the one the
    /// suite reads to say what would move it.
    /// </summary>
    /// <remarks>
    /// <c>ResourceSO.IsVisible()</c> is the field alone, so nothing a read does can move it;
    /// <c>CheckVisibility()</c> — called from <c>Increment</c>, the ordinary per-frame element tick —
    /// is what latches it, from <c>startVisible</c> AND <c>visiblePrerequisites.Check()</c>. That
    /// container is therefore the whole authored answer to "what puts this in the resource list",
    /// and it is what the unlock block beside the bit publishes.
    /// </remarks>
    [GameAssemblyFact]
    public void AResourcesLedgerBitIsStoredAndItsGateIsTheContainerTheSuiteReads()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.Boolean", assembly.GetFieldType("ResourceSO", "visible"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("ResourceSO", "startVisible"));
        Assert.Equal(
            "Prerequisites+Container",
            assembly.GetFieldType("ResourceSO", "visiblePrerequisites"));

        Assert.Equal(
            new[] { "ResourceSO.visible" },
            assembly.GetMethodBodyDefinitionReferences("ResourceSO", "IsVisible")
                .Concat(assembly.GetMethodBodyMemberReferences("ResourceSO", "IsVisible"))
                .Select(reference => reference.DeclaringType + "." + reference.MemberName)
                .ToArray());
        Assert.True(assembly.MethodReferencesField(
            "ResourceSO", "CheckVisibility", "ResourceSO", "startVisible"));
        Assert.True(assembly.MethodReferencesField(
            "ResourceSO", "CheckVisibility", "ResourceSO", "visiblePrerequisites"));
        Assert.True(assembly.MethodReferencesField(
            "ResourceSO", "CheckVisibility", "ResourceSO", "visible"));
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

    /// <summary>
    /// A resource condition compares three stored fields and calls nothing, and the order its jump
    /// table visits them in is the order the mirrored ordinals assign them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The trap this pins is the middle one: the quantity comparison reads
    /// <c>lifetimeQuantity</c> — everything ever gained — and not <c>quantity</c>. Had it read
    /// holdings, a requirement would un-meet itself the moment the resource was spent, and a save
    /// that had never spent any would have agreed with either reading.
    /// </para>
    /// <para>
    /// The fields are asserted as a sequence because the sequence is the mapping. The jump table has
    /// no offset, so the first body reference belongs to ordinal nought, and
    /// <c>RequirementEnumContractTests</c> pins which member that is.
    /// </para>
    /// </remarks>
    [GameAssemblyFact]
    public void AResourceRequirementComparesStoredFieldsInTheOrderItsOrdinalsAssign()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var evaluator = Assert.Single(
            assembly.GetMethods("Requirements.ResourceRequirement", "InternalIsValid"));
        Assert.Equal("System.Boolean", evaluator.ReturnType);
        Assert.Equal(
            new[] { "Requirements.ConditionValueInstance" },
            evaluator.ParameterTypes.ToArray());

        Assert.Equal(
            new[]
            {
                "ResourceSO.visible",
                "ResourceSO.lifetimeQuantity",
                "ResourceSO.maxQuantity",
            },
            assembly.GetMethodBodyDefinitionReferences(
                    "Requirements.ResourceRequirement", "InternalIsValid")
                .Where(reference => reference.DeclaringType == "ResourceSO")
                .Select(reference => reference.DeclaringType + "." + reference.MemberName)
                .ToArray());

        Assert.False(assembly.MethodReferencesField(
            "Requirements.ResourceRequirement", "InternalIsValid", "ResourceSO", "quantity"));

        // The ceiling is a modifier record, and the comparison the suite mirrors is the one that
        // reaches its value: `Reading.Capacity` is captured from that same record.
        Assert.Equal("ValueModifierRecord", assembly.GetFieldType("ResourceSO", "maxQuantity"));
        Assert.Equal("BigDouble", assembly.GetFieldType("ResourceSO", "lifetimeQuantity"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("ResourceSO", "visible"));
    }
}

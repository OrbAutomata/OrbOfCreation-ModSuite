using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldAlchemyLevelEconomyTests : IDisposable
{
    private static readonly Guid RecipeId = new("bef8a8d2-ae64-46ef-9e45-56b53f5d7611");
    private static readonly Guid ResourceId = new("9ce63351-5040-4808-b31a-b22a947f09e1");
    public WorldAlchemyLevelEconomyTests() => OrbModding.GameStubs.GameStubState.Reset();
    public void Dispose() => OrbModding.GameStubs.GameStubState.Reset();

    [Fact]
    public void ShippingReaderCapturesActualOrdinaryDrainAndQuotesNextLevelWithoutChangingTheDial()
    {
        var (recipe, instance, manager) = Seed();
        instance.resourceDrain.necessaryRatio = new BigDouble(.25);
        instance.resourceDrain.usageRatio = new BigDouble(.5);
        var frame = Collect();
        var first = Quote(frame);
        var repeated = Quote(frame);

        Assert.True(first.Complete, first.Reason);
        Assert.True(first.NextComplete, first.NextReason);
        Assert.False(recipe.usagePrerequisites.available);
        Assert.True(first.HasActiveWork);
        Assert.Equal(5d, Assert.Single(first.Current.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(40d, Assert.Single(first.Next.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(first.Next[0].Amount, repeated.Next[0].Amount);
        Assert.Equal(1, Player.Current.alchemyOutputLevel.AsInt());
        Assert.Equal(0, Player.Current.alchemyOutputLevel.SetCalls);
        Assert.Same(Player.Current.alchemyOutputLevel, recipe.coreType.selectedLevel);
        Assert.Single(manager.activeAlchemy.value);
    }

    [Fact]
    public void ActualDrainMustMatchTheCapturedFormulaBeforeProspectiveEvidenceIsTrusted()
    {
        var (_, instance, _) = Seed();
        instance.resourceDrain.current.costs[0] = new ResourceTuple(
            instance.resourceDrain.current.costs[0].resource, new BigDouble(19));

        var quote = Quote(Collect());

        Assert.True(quote.Complete, quote.Reason);
        Assert.False(quote.NextComplete);
        Assert.Contains("has not settled", quote.NextReason);
        Assert.Equal(19d, quote.Current[0].Amount.ToDouble());
        Assert.Equal(0, quote.Next.Count);
    }

    [Fact]
    public void ConceptAssignmentsAndIndependentOrdinaryLevelVariablesDoNotEnterThisDialsQuote()
    {
        var (_, _, manager) = Seed();
        var concept = new AlchemyRecipeSO(Guid.NewGuid().ToString(), "Concept", Array.Empty<AlchemyTypeSO>())
        { coreType = new AlchemyTypeSO(Guid.NewGuid().ToString()) };
        var brewing = new AlchemyRecipeSO(Guid.NewGuid().ToString(), "Independent brewing", Array.Empty<AlchemyTypeSO>())
        { coreType = new AlchemyTypeSO(KnownEntities.Brewing.Uuid.ToString()) { selectedLevel = new IntVariable { Value = 8 } } };
        foreach (var recipe in new[] { concept, brewing })
        {
            manager.allAlchemy.value.Add(recipe);
            var instance = new AlchemyInstance(recipe) { quantity = 100, queuedQuantity = 100 };
            instance.resourceDrain.current.costs.Add(new ResourceTuple(new ResourceSO { uuid = ResourceId.ToString() }, new BigDouble(9999)));
            manager.activeAlchemy.value.Add(instance);
        }

        var quote = Quote(Collect());

        Assert.True(quote.Complete, quote.Reason);
        Assert.Equal(20d, quote.Current[0].Amount.ToDouble());
        Assert.Equal(40d, quote.Next[0].Amount.ToDouble());
    }

    [Fact]
    public void PendingQuantityChangesMakeTheWholeAffectedQuoteUnavailable()
    {
        var (_, instance, _) = Seed();
        instance.queuedQuantity = 3;

        var quote = Quote(Collect());

        Assert.False(quote.Complete);
        Assert.Contains("quantity change has not settled", quote.Reason);
    }

    [Fact]
    public void MissingOptionalEconomyContractDoesNotEraseTheExistingLoadout()
    {
        Seed();
        var frame = new GameWorldCycleFrame();
        var reader = new WorldAlchemyLoadoutReader(name => name == "InstanceScalingRef" ? null : Resolve(name));

        var report = reader.Collect(new HashSet<Guid>(), frame);
        var quote = Quote(frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(1, frame.AlchemyLoadout.Count);
        Assert.True(quote.Complete, quote.Reason);
        Assert.Equal(20d, quote.Current[0].Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("prospective-level contracts are unavailable", quote.NextReason);
    }

    [Fact]
    public void UnusableProspectiveOperandKeepsTheSettledActualDrain()
    {
        var (recipe, _, _) = Seed();
        recipe.instanceScaling.scaling.instanceScaling = null!;
        using var faults = new OrbModding.TestSupport.CapturedGameActionFaultLog();

        var quote = Quote(Collect());

        Assert.True(quote.Complete, quote.Reason);
        Assert.True(quote.HasActiveWork);
        Assert.Equal(20d, quote.Current[0].Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("prospective level economy threw", quote.NextReason);
        Assert.Equal(0, quote.Next.Count);
    }

    [Fact]
    public void ChangedDialAndMissingResourceEvidenceBothFailClosed()
    {
        Seed();
        var frame = Collect();
        var changedDial = frame.LevelEconomy.Build(Resources(),
            new WorldSpellWorkbench(0, 0, false, alchemyLevel: 2, maximumAlchemyLevel: 4),
            PublicationTable<WorldSpellTypeResonance>.Empty, requirementWorld: Requirements()).Alchemy;
        var missingResource = frame.LevelEconomy.Build(PublicationTable<WorldResource>.Empty,
            new WorldSpellWorkbench(0, 0, false, alchemyLevel: 1, maximumAlchemyLevel: 4),
            PublicationTable<WorldSpellTypeResonance>.Empty, requirementWorld: Requirements()).Alchemy;

        Assert.True(changedDial.Complete, changedDial.Reason);
        Assert.False(changedDial.NextComplete);
        Assert.Contains("disagree", changedDial.NextReason);
        Assert.False(missingResource.Complete);
        Assert.Contains("unreadable resource", missingResource.Reason);
    }

    [Fact]
    public void UnknownUsageProgramKeepsAllActualDrainButDisablesTheFutureQuote()
    {
        var (first, _, manager) = Seed();
        var second = new AlchemyRecipeSO(Guid.NewGuid().ToString(), "Second ordinary recipe", new[] { first.coreType })
        {
            coreType = first.coreType, drainCostMod = first.drainCostMod, speed = first.speed,
            freeUsageSlots = first.freeUsageSlots, maxUsageSlots = first.maxUsageSlots,
            overdriveSpeed = first.overdriveSpeed, overdriveDrainCostMod = first.overdriveDrainCostMod,
            drainCostLevelMod = first.drainCostLevelMod, instanceScaling = first.instanceScaling,
            drainCost = first.drainCost,
        };
        var secondInstance = new AlchemyInstance(second) { quantity = 2, queuedQuantity = 2 };
        secondInstance.resourceDrain.current.costs.Add(new ResourceTuple(
            first.drainCost.costs[0].resource, new BigDouble(20)));
        manager.allAlchemy.value.Add(second);
        manager.activeAlchemy.value.Add(secondInstance);
        var frame = Collect();
        var quote = frame.LevelEconomy.Build(Resources(),
            new WorldSpellWorkbench(0, 0, false, alchemyLevel: 1, maximumAlchemyLevel: 4),
            PublicationTable<WorldSpellTypeResonance>.Empty, requirementWorld: Requirements(null)).Alchemy;

        Assert.True(quote.Complete, quote.Reason);
        Assert.Equal(40d, quote.Current[0].Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("usage prerequisite cannot be evaluated", quote.NextReason);
        Assert.Equal(0, quote.Next.Count);
    }

    [Fact]
    public void CapturedUnmetUsageProgramAppliesBothNativePenaltiesOnTheWorker()
    {
        var (recipe, instance, _) = Seed();
        recipe.coreType.reqSpeedPenalty = new ValueModifier(ValueModifier.ValueModifierType.Raw, BigDouble.One);
        recipe.coreType.reqCostPenalty = new ValueModifier(ValueModifier.ValueModifierType.Raw, BigDouble.One);
        instance.resourceDrain.current.costs[0] = new ResourceTuple(
            instance.resourceDrain.current.costs[0].resource, new BigDouble(80));
        var frame = Collect();
        var quote = frame.LevelEconomy.Build(Resources(),
            new WorldSpellWorkbench(0, 0, false, alchemyLevel: 1, maximumAlchemyLevel: 4),
            PublicationTable<WorldSpellTypeResonance>.Empty, requirementWorld: Requirements(false)).Alchemy;

        Assert.True(quote.Complete, quote.Reason);
        Assert.True(quote.NextComplete, quote.NextReason);
        Assert.Equal(80d, quote.Current[0].Amount.ToDouble());
        Assert.Equal(160d, quote.Next[0].Amount.ToDouble());
        Assert.False(recipe.usagePrerequisites.available);
    }

    private static GameWorldCycleFrame Collect()
    {
        var frame = new GameWorldCycleFrame();
        var report = new WorldAlchemyLoadoutReader(Resolve).Collect(new HashSet<Guid>(), frame);
        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        return frame;
    }

    private static WorldLevelDomainQuote Quote(GameWorldCycleFrame frame) => frame.LevelEconomy.Build(Resources(),
        new WorldSpellWorkbench(0, 0, false, alchemyLevel: 1, maximumAlchemyLevel: 4),
        PublicationTable<WorldSpellTypeResonance>.Empty, requirementWorld: Requirements()).Alchemy;

    private static GameWorldState Requirements(bool? met = true)
    {
        var requirement = new WorldEntityRequirement(RecipeId, WorldRequirementOwnerKind.AlchemyRecipe,
            0, 0, -1, 0, WorldRequirementNodeKind.Leaf, WorldRequirementOperator.None,
            met.HasValue ? WorldRequirementConditionKind.Literal : WorldRequirementConditionKind.Unknown,
            met.HasValue ? "literal" : "OpaqueRequirement", Guid.Empty, met == true ? 1 : 0,
            0, default, default, WorldRequirementProgramKind.Usage);
        return new GameWorldState { EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(new[] { requirement }) };
    }

    private static PublicationTable<WorldResource> Resources()
    {
        var reading = WorldSamples.Resource(ResourceId, quantity: 1000, capacity: 10000, rate: 50);
        var resource = new WorldResourceDeriver(default).Derive(in reading);
        return PublicationTable<WorldResource>.Create(new[] { resource });
    }

    private static (AlchemyRecipeSO Recipe, AlchemyInstance Instance, AlchemyManager Manager) Seed()
    {
        Player.Current = new Player();
        var manager = new AlchemyManager();
        AlchemyManager.instance = manager;
        var core = new AlchemyTypeSO(KnownEntities.Alchemy.Uuid.ToString())
        { selectedLevel = Player.Current.alchemyOutputLevel };
        var recipe = new AlchemyRecipeSO(RecipeId.ToString(), "Ordinary recipe", new[] { core })
        {
            coreType = core,
            drainCostMod = new ValueModifierRecord(new BigDouble(100)),
            speed = new ValueModifierRecord(new BigDouble(100)),
            freeUsageSlots = new ValueModifierRecord(new BigDouble(2)),
            maxUsageSlots = new ValueModifierRecord(new BigDouble(5)),
            overdriveSpeed = new ValueModifierRecord(new BigDouble(100)),
            overdriveDrainCostMod = new ValueModifierRecord(new BigDouble(100)),
            drainCostLevelMod = new NativeLevelList(),
        };
        var instanceCost = new ValueModifierList();
        instanceCost.modifiers.Add(new ValueModifier(ValueModifier.ValueModifierType.Raw, new BigDouble(1)));
        recipe.instanceScaling.scaling.instanceScaling.Set(ScalingType.CostMod, instanceCost);
        var resource = new ResourceSO { uuid = ResourceId.ToString() };
        recipe.drainCost.costs.Add(new ResourceTuple(resource, new BigDouble(10)));
        var instance = new AlchemyInstance(recipe) { quantity = 2, queuedQuantity = 2 };
        instance.resourceDrain.current.costs.Add(new ResourceTuple(resource, new BigDouble(20)));
        manager.allAlchemy.value.Add(recipe);
        manager.activeAlchemy.value.Add(instance);
        return (recipe, instance, manager);
    }

    // Models ModifierListRef.Standard: the native getter, rather than its empty backing variable,
    // supplies the authored list. Prospective scaling remains shipping suite math.
    private sealed class NativeLevelList : ModifierListRef
    {
        private readonly ValueModifierList _native = new();
        internal NativeLevelList() => _native.modifiers.Add(
            new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, new BigDouble(2)));
        public override ValueModifierList GetValue() => _native;
    }

    private static Type? Resolve(string name) => typeof(AlchemyRecipeSO).Assembly.GetType(name, throwOnError: false);
}

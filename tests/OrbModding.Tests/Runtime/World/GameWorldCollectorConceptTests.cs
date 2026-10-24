using System;
using OrbAutomata;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorConceptTests : GameWorldCollectorTestBase
{
    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void ConceptRecipesInstancesAndDrainVectorsArePublishedTogether()
    {
        var resource = Guid.NewGuid();
        var coreType = new AlchemyTypeSO { Identity = Guid.NewGuid() };
        var recipe = new AlchemyRecipeSO
        {
            Identity = Guid.NewGuid(),
            coreType = coreType,
            alchemyTypes = { coreType },
            isCompletionRecipe = true,
            isAdvancementRecipe = true,
            completionTime = 16d,
            recipeTime = new BigDouble(2d),
            speed = new ValueModifierRecord(200d),
            timeReqMod = new ValueModifierRecord(25d),
            timeScalingMod = new ValueModifierRecord(80d),
            CachedCompletionTime = new BigDouble(4d),
            CachedRequiredXp = default,
            maxUsageSlots = new ValueModifierRecord(4d),
            ExperienceContainer = new ExperienceContainer
            {
                CachedRequiredXp = new BigDouble(12d),
            },
            drainCost = new ResourceCostList().With(resource, 7d),
        };
        AlchemyRecipeSO.All.Add(recipe);

        var recipes = new AlchemyRecipeListVariable();
        recipes.value.Add(recipe);
        var instances = new AlchemyInstanceListVariable();
        instances.value.Add(new AlchemyInstance(recipe)
        {
            quantity = 2,
            queuedQuantity = 3,
            resourceDrain = new ResourceDrain
            {
                currentRatio = new BigDouble(0.75d),
                usageRatio = new BigDouble(0.75d),
                current = new ResourceCostList().With(resource, 11d),
            },
        });
        IdScriptableObject.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldConceptRecipeLookup.TryFind(world.ConceptRecipes, recipe.Identity, out var concept));
        Assert.Equal(coreType.Identity, concept.CoreTypeId);
        Assert.True(concept.CanAddNow);
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, recipe.Identity, out var alchemyRecipe));
        Assert.Equal(coreType.Identity, alchemyRecipe.CoreTypeId);
        Assert.Equal(16d, alchemyRecipe.CompletionTime);
        Assert.Equal(2d, alchemyRecipe.RecipeTime.ToDouble());
        Assert.Equal(200d, alchemyRecipe.Speed.ToDouble());
        Assert.Equal(25d, alchemyRecipe.TimeReqMod.ToDouble());
        Assert.Equal(80d, alchemyRecipe.TimeScalingMod.ToDouble());
        Assert.Equal(4d, alchemyRecipe.CachedCompletionTime.ToDouble());
        Assert.Equal(0d, recipe.CachedRequiredXp.ToDouble());
        Assert.Equal(12d, alchemyRecipe.RequiredExperience.ToDouble());

        Assert.True(WorldAlchemyInstanceLookup.TryFind(world.AlchemyInstances, recipe.Identity, out var instance));
        Assert.Equal(2, instance.Quantity);
        Assert.Equal(3, instance.QueuedQuantity);
        Assert.False(instance.IsSettled);
        Assert.True(instance.DrainReadable);
        Assert.Equal(0.75d, instance.DrainRatio.ToDouble());

        Assert.True(WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts,
            recipe.Identity,
            WorldAlchemyCostKind.RecipeDrain,
            out var recipeStart,
            out var recipeCount));
        Assert.Equal(1, recipeCount);
        Assert.Equal(resource, world.AlchemyCosts[recipeStart].ResourceId);
        Assert.Equal(7d, world.AlchemyCosts[recipeStart].Amount.ToDouble());

        Assert.True(WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts,
            recipe.Identity,
            WorldAlchemyCostKind.CurrentDrain,
            out var currentStart,
            out var currentCount));
        Assert.Equal(1, currentCount);
        Assert.Equal(11d, world.AlchemyCosts[currentStart].Amount.ToDouble());

        Assert.False(WorldAlchemyCostLookup.TryFindRange(
            world.AlchemyCosts,
            recipe.Identity,
            WorldAlchemyCostKind.Bandwidth,
            out _,
            out var bandwidthCount));
        Assert.Equal(0, bandwidthCount);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void AnEmptyConceptCapacitySlotIsCleanlyIgnored()
    {
        var recipe = new AlchemyRecipeSO();
        var recipes = new AlchemyRecipeListVariable();
        recipes.value.Add(recipe);
        var instances = new AlchemyInstanceListVariable();
        instances.value.Add(new AlchemyInstance(null!));
        IdScriptableObject.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        var collector = Collector();
        var report = collector.Collect();
        var concepts = report.For("concept instances");

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(concepts.IsClean);
        Assert.Equal(1, concepts.Sampled);
        Assert.Equal(0, concepts.Skipped);
        Assert.Equal(0, collector.Build().AlchemyInstances.Count);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void ARealConceptBesideAnEmptyCapacitySlotPublishesOnlyTheRealInstance()
    {
        var recipe = new AlchemyRecipeSO();
        var recipes = new AlchemyRecipeListVariable();
        recipes.value.Add(recipe);
        var instances = new AlchemyInstanceListVariable();
        instances.value.Add(new AlchemyInstance(recipe) { quantity = 2, queuedQuantity = 3 });
        instances.value.Add(new AlchemyInstance(null!));
        IdScriptableObject.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(0, report.For("concept instances").Skipped);
        Assert.Equal(1, world.AlchemyInstances.Count);
        Assert.True(WorldAlchemyInstanceLookup.TryFind(world.AlchemyInstances, recipe.Identity, out var instance));
        Assert.Equal(2, instance.Quantity);
        Assert.Equal(3, instance.QueuedQuantity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "AutoConceptReliability")]
    public void NullAndWrongRuntimeTypeConceptElementsStayOnTheUnexpectedTypeBranch(bool wrongRuntimeType)
    {
        var recipe = new AlchemyRecipeSO();
        var recipes = new AlchemyRecipeListVariable();
        // No scoped recipe means this pass reaches the collector's row check without first
        // asking the native admission search to inspect the malformed list.
        var instances = new AlchemyInstanceListVariable();
        instances.value.Add(wrongRuntimeType ? new UnexpectedAlchemyInstance(recipe) : null!);
        IdScriptableObject.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        var collector = Collector();
        var report = collector.Collect();
        var concepts = report.For("concept instances");

        Assert.False(report.IsComplete);
        Assert.Equal(0, concepts.Sampled);
        Assert.Equal(1, concepts.Skipped);
        Assert.Equal("active instance 0 had an unexpected native type", concepts.FirstFailure);
        Assert.Equal(0, collector.Build().AlchemyInstances.Count);
    }


    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void ALiteralNullDuringNativeConceptAdmissionFaultsTheWholeWorldPass()
    {
        var type = new AlchemyTypeSO();
        var recipe = new AlchemyRecipeSO { coreType = type, alchemyTypes = { type } };
        var other = new AlchemyRecipeSO { coreType = type, alchemyTypes = { type } };
        var recipes = new AlchemyRecipeListVariable();
        recipes.value.Add(recipe);
        var instances = new AlchemyInstanceListVariable();
        instances.value.Add(new AlchemyInstance(other) { quantity = 1, queuedQuantity = 1 });
        instances.RebuildCounts();
        instances.value.Add(null!);
        IdScriptableObject.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        // These getters read the native count cache; they do not walk the malformed list.
        Assert.Equal(1, instances.GetNumOfType(type));
        Assert.Equal(15, instances.GetNumEmptyTypelessSlots());

        var failure = Assert.Throws<NullReferenceException>(() => Collector().Collect());
        Assert.Contains(nameof(AlchemyInstanceListVariable.CanAddInstance), failure.StackTrace);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void ConceptRankingUsesTheNestedRequiredExperienceWhenTheOrphanAliasesAreDefault()
    {
        var alphaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var betaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var coreType = new AlchemyTypeSO { Identity = Guid.NewGuid() };
        var alpha = RankedRecipe(alphaId, coreType, masteryXp: 90d, requiredExperience: 100d);
        var beta = RankedRecipe(betaId, coreType, masteryXp: 10d, requiredExperience: 100d);
        AlchemyRecipeSO.All.AddRange(new[] { alpha, beta });
        var recipes = new AlchemyRecipeListVariable();
        recipes.value.AddRange(new[] { alpha, beta });
        IdScriptableObject.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] =
            new AlchemyInstanceListVariable();

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(0d, alpha.CachedRequiredXp.ToDouble());
        Assert.Equal(0d, beta.CachedRequiredXp.ToDouble());
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, alphaId, out var alphaRow));
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, betaId, out var betaRow));
        Assert.Equal(100d, alphaRow.RequiredExperience.ToDouble());
        Assert.Equal(100d, betaRow.RequiredExperience.ToDouble());

        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            AutoConcept = new AutoConceptConfiguration
            {
                Mode = AutoConceptOperationMode.Active,
                SlotManagement = AutoConceptSlotManagementMode.RotateAll,
                TrainingPeriodSeconds = 60,
                MinimumDrainRatio = 0.25f,
            },
        };
        var state = AutoConceptCycleState.Create(new LifecycleGeneration(1));
        var actions = new ReusableActionStore<AutoConceptCycleAction>();
        actions.BeginWrite();
        var writer = new ServiceActionWriter<AutoConceptCycleAction>(actions);
        var identity = new ServiceCycleIdentity(
            new ServiceId("auto-concept"),
            new LifecycleGeneration(1),
            new ConfigGeneration(1),
            StrategyGeneration.Initial,
            new WorldGeneration(1),
            new CycleId(1));
        var context = new ServiceCycleContext(
            identity,
            default,
            new MonotonicTimestamp(1));

        AutoConceptCycleEvaluator.Evaluate(
            world,
            in config,
            in context,
            ref state,
            writer,
            out _);

        Assert.Equal(1, actions.Count);
        Assert.Equal(betaId, actions.GetCurrent().RecipeId);
    }

    private static AlchemyRecipeSO RankedRecipe(
        Guid id,
        AlchemyTypeSO coreType,
        double masteryXp,
        double requiredExperience) =>
        new()
        {
            Identity = id,
            discovered = true,
            coreType = coreType,
            alchemyTypes = { coreType },
            masteryXp = new BigDouble(masteryXp),
            maxUsageSlots = new ValueModifierRecord(1d),
            CachedRequiredXp = default,
            ExperienceContainer = new ExperienceContainer
            {
                CachedRequiredXp = new BigDouble(requiredExperience),
            },
        };

    [Fact]
    public void AlchemyRecipeResolvesNativeUsageSentinelBeforeAutoConceptPlanning()
    {
        var coreType = new AlchemyTypeSO
        {
            Identity = Guid.NewGuid(),
            maxUsageByMastery = true,
        };
        var recipe = new AlchemyRecipeSO
        {
            Identity = Guid.NewGuid(),
            coreType = coreType,
            alchemyTypes = { coreType },
            discovered = true,
            masteryLevel = 4,
            maxUsageSlots = new ValueModifierRecord(-1d),
        };
        AlchemyRecipeSO.All.Add(recipe);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, recipe.Identity, out var row));
        Assert.Equal(-1d, recipe.maxUsageSlots.GetValue().ToDouble());
        Assert.Equal(5d, row.ResolvedMaxUsageSlots.ToDouble());
    }

    [Fact]
    public void AlchemyTypePublishesTheSpeedAndCostItsRecipesRunAtWhileTheirUsageRequirementIsUnmet()
    {
        var type = new AlchemyTypeSO
        {
            Identity = Guid.NewGuid(),
            reqSpeedPenalty = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 0.25d, 0),
            reqCostPenalty = new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 16d, 0),
        };
        AlchemyTypeSO.All.Add(type);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, type.Identity, out var row));
        Assert.Equal("x0.250", row.RequirementSpeedPenalty.ToStringValue());
        Assert.Equal("x16", row.RequirementCostPenalty.ToStringValue());
    }

    [Fact]
    public void OrdinaryAlchemyPublishesTheOrderedClickDecisionAndUsageHoldings()
    {
        var resource = new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = 80d,
            maxQuantity = new ValueModifierRecord(100d),
            Visible = true,
        };
        ResourceSO.All.Add(resource);
        var type = new AlchemyTypeSO { Identity = KnownEntities.Alchemy.Uuid };
        AlchemyTypeSO.All.Add(type);
        var recipe = new AlchemyRecipeSO
        {
            Identity = Guid.NewGuid(),
            coreType = type,
            alchemyTypes = { type },
            discovered = true,
            freeUsageSlots = new ValueModifierRecord(1d),
            maxUsageSlots = new ValueModifierRecord(5d),
            usageCost = new ResourceCostList()
                .With(resource, new BigDouble(5d)),
        };
        AlchemyRecipeSO.All.Add(recipe);
        AlchemyManager.instance!.allAlchemy.value.Add(recipe);
        AlchemyManager.instance!.activeAlchemy.value.Add(new AlchemyInstance(recipe)
        {
            quantity = 1,
            queuedQuantity = 2,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldAlchemyLoadoutLookup.TryFind(
            world.AlchemyLoadout, recipe.Identity, out var decision));
        Assert.Equal(0, decision.Position);
        Assert.Equal(1, decision.Amount);
        Assert.Equal(2, decision.TargetAmount);
        Assert.Equal(3, decision.MaximumAdd);
        Assert.Equal(2, decision.TargetAmount);
        Assert.True(WorldAlchemyLoadoutLookup.TryFindCostRange(
            world.AlchemyUsageCosts, recipe.Identity, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(resource.Identity, world.AlchemyUsageCosts[start].ResourceId);
        Assert.Equal(5d, world.AlchemyUsageCosts[start].Amount.ToDouble());
    }
}

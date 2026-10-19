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
        var coreType = new FakeAlchemyType { Identity = Guid.NewGuid() };
        var recipe = new FakeAlchemyRecipe
        {
            Identity = Guid.NewGuid(),
            coreType = coreType,
            isCompletionRecipe = true,
            isAdvancementRecipe = true,
            completionTime = 16d,
            recipeTime = new BigDouble(2d),
            speed = new FakeModifierRecord(200d),
            timeReqMod = new FakeModifierRecord(25d),
            timeScalingMod = new FakeModifierRecord(80d),
            cachedCompletionTime = new BigDouble(4d),
            cachedRequiredXp = default,
            maxUsageSlots = new FakeModifierRecord(4d),
            experienceContainer = new FakeExperienceContainer
            {
                cachedRequiredXp = new BigDouble(12d),
            },
            drainCost = new FakeSpellCostList().With(resource, 7d),
        };
        FakeAlchemyRecipe.All.Add(recipe);

        var recipes = new FakeAlchemyRecipeList();
        recipes.value.Add(recipe);
        var instances = new FakeAlchemyInstanceList();
        instances.value.Add(new FakeAlchemyInstance(recipe)
        {
            quantity = 2,
            queuedQuantity = 3,
            resourceDrain = new FakeAlchemyDrain
            {
                currentRatio = new BigDouble(0.75d),
                usageRatio = new BigDouble(0.75d),
                current = new FakeSpellCostList().With(resource, 11d),
            },
        });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

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
        Assert.Equal(0d, recipe.cachedRequiredXp.ToDouble());
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
        var recipe = new FakeAlchemyRecipe();
        var recipes = new FakeAlchemyRecipeList();
        recipes.value.Add(recipe);
        var instances = new FakeAlchemyInstanceList();
        instances.value.Add(new FakeAlchemyInstance(null));
        FakeIdRegistry.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

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
        var recipe = new FakeAlchemyRecipe();
        var recipes = new FakeAlchemyRecipeList();
        recipes.value.Add(recipe);
        var instances = new FakeAlchemyInstanceList();
        instances.value.Add(new FakeAlchemyInstance(recipe) { quantity = 2, queuedQuantity = 3 });
        instances.value.Add(new FakeAlchemyInstance(null));
        FakeIdRegistry.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

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
        var recipe = new FakeAlchemyRecipe();
        var recipes = new FakeAlchemyRecipeList();
        recipes.value.Add(recipe);
        var instances = new FakeAlchemyInstanceList();
        instances.value.Add(wrongRuntimeType ? new FakeUnexpectedAlchemyInstance(recipe) : null!);
        FakeIdRegistry.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] = instances;

        var collector = Collector();
        var report = collector.Collect();
        var concepts = report.For("concept instances");

        Assert.False(report.IsComplete);
        Assert.Equal(1, concepts.Sampled);
        Assert.Equal(1, concepts.Skipped);
        Assert.Equal("active instance 0 had an unexpected native type", concepts.FirstFailure);
        Assert.Equal(0, collector.Build().AlchemyInstances.Count);
    }

    [Fact]
    [Trait("Category", "AutoConceptReliability")]
    public void ConceptRankingUsesTheNestedRequiredExperienceWhenTheOrphanAliasesAreDefault()
    {
        var alphaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var betaId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var coreType = new FakeAlchemyType { Identity = Guid.NewGuid() };
        var alpha = RankedRecipe(alphaId, coreType, masteryXp: 90d, requiredExperience: 100d);
        var beta = RankedRecipe(betaId, coreType, masteryXp: 10d, requiredExperience: 100d);
        FakeAlchemyRecipe.All.AddRange(new[] { alpha, beta });
        var recipes = new FakeAlchemyRecipeList();
        recipes.value.AddRange(new[] { alpha, beta });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ConceptRecipes.Uuid] = recipes;
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveConcepts.Uuid] =
            new FakeAlchemyInstanceList();

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(0d, alpha.cachedRequiredXp.ToDouble());
        Assert.Equal(0d, beta.cachedRequiredXp.ToDouble());
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

    private static FakeAlchemyRecipe RankedRecipe(
        Guid id,
        FakeAlchemyType coreType,
        double masteryXp,
        double requiredExperience) =>
        new()
        {
            Identity = id,
            discovered = true,
            coreType = coreType,
            masteryXp = new BigDouble(masteryXp),
            maxUsageSlots = new FakeModifierRecord(1d),
            cachedRequiredXp = default,
            experienceContainer = new FakeExperienceContainer
            {
                cachedRequiredXp = new BigDouble(requiredExperience),
            },
        };

    [Fact]
    public void AlchemyRecipeResolvesNativeUsageSentinelBeforeAutoConceptPlanning()
    {
        var coreType = new FakeAlchemyType
        {
            Identity = Guid.NewGuid(),
            maxUsageByMastery = true,
        };
        var recipe = new FakeAlchemyRecipe
        {
            Identity = Guid.NewGuid(),
            coreType = coreType,
            discovered = true,
            masteryLevel = 4,
            maxUsageSlots = new FakeModifierRecord(-1d),
        };
        FakeAlchemyRecipe.All.Add(recipe);

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
        var type = new FakeAlchemyType
        {
            Identity = Guid.NewGuid(),
            reqSpeedPenalty = new FakeValueModifier(FakeModifierKind.MultiStacking, 0.25d, 0),
            reqCostPenalty = new FakeValueModifier(FakeModifierKind.MultiStacking, 16d, 0),
        };
        FakeAlchemyType.All.Add(type);

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
        var resource = new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = 80d,
            maxQuantity = new FakeModifierRecord(100d),
            Visible = true,
        };
        FakeResource.All.Add(resource);
        var type = new FakeAlchemyType { Identity = KnownEntities.Alchemy.Uuid };
        FakeAlchemyType.All.Add(type);
        var recipe = new FakeAlchemyRecipe
        {
            Identity = Guid.NewGuid(),
            coreType = type,
            discovered = true,
            freeUsageSlots = new FakeModifierRecord(1d),
            maxUsageSlots = new FakeModifierRecord(5d),
            usageCost = new FakeCraftingResourceCostList()
                .With(resource, new BigDouble(5d)),
        };
        FakeAlchemyRecipe.All.Add(recipe);
        FakeAlchemyManager.instance.activeAlchemy.value.Add(new FakeAlchemyInstance(recipe)
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

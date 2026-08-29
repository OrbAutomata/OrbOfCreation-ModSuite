using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldCraftingDecisionReaderTests : IDisposable
{
    public WorldCraftingDecisionReaderTests()
    {
        CraftingRecipeSO.All.Clear();
        UnityEngine.Resources.Objects.Clear();
    }

    public void Dispose()
    {
        CraftingRecipeSO.All.Clear();
        UnityEngine.Resources.Objects.Clear();
    }

    [Fact]
    public void DirectRecipePublishesExactNextCostHoldingAndAffordability()
    {
        var resource = Resource(amount: 19);
        var recipe = Recipe();
        recipe.useQuantityAsLevel = true;
        recipe.StartingQuantity = new BigDouble(2, 0);
        recipe.recipeCost.costs.Add(new ResourceTuple(resource, new BigDouble(4, 0)));
        CraftingRecipeSO.All.Add(recipe);
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        var report = reader.Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(1, report.Sampled);
        Assert.Equal(0, report.Skipped);
        ref readonly var decision = ref frame.CraftingDecisions[0];
        Assert.Equal(recipe.GetGuid(), decision.RecipeId);
        Assert.Equal(WorldCraftingPipeline.Direct, decision.Pipeline);
        Assert.Equal(new BigDouble(2, 0), decision.PurchaseAmount);
        Assert.True(decision.CanStart);
        Assert.Equal("ready", decision.ReasonCode);
        ref readonly var cost = ref frame.CraftingDecisionCosts[0];
        Assert.Equal(resource.GetGuid(), cost.ResourceId);
        Assert.Equal(new BigDouble(8, 0), cost.Cost);
        Assert.Equal(new BigDouble(19, 0), cost.Amount);
        Assert.True(cost.Affordable);
    }

    [Fact]
    public void StackModePublishesQueueIdentityOccupancyAndExistingQuantity()
    {
        var recipe = Recipe();
        CraftingRecipeSO.All.Add(recipe);
        var page = Page(recipe, mode: 0, maximum: 1);
        page.craftingQueueInstances.value.Add(
            new CraftingInstance(recipe, new BigDouble(6, 0)));
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        var report = reader.Collect(new HashSet<Guid>(), frame);

        Assert.Equal(1, report.Sampled);
        ref readonly var decision = ref frame.CraftingDecisions[0];
        Assert.Equal(WorldCraftingPipeline.QueueStack, decision.Pipeline);
        Assert.Equal(new BigDouble(6, 0), decision.QueuedAmount);
        Assert.Equal(page.craftingQueueInstances.GetGuid(), decision.QueueId);
        Assert.Equal(1, decision.QueueUsed);
        Assert.Equal(1, decision.QueueMaximum);
        Assert.True(decision.CanStart);
    }

    [Fact]
    public void Page_recipe_publishes_manual_and_automated_instance_decisions()
    {
        var recipe = Recipe();
        CraftingRecipeSO.All.Add(recipe);
        var page = Page(recipe, mode: 0, maximum: 2);
        page.craftingQueueInstances.value.Add(
            new CraftingInstance(recipe, new BigDouble(4)));
        var automatic = new CraftingInstance(recipe, new BigDouble(3)).SetAuto(true);
        automatic.SetAutomationQuantity(3);
        page.craftingAutomationInstances.value.Add(automatic);
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        var report = reader.Collect(new HashSet<Guid>(), frame);

        Assert.Equal(1, report.Sampled);
        ref readonly var decision = ref frame.CraftingDecisions[0];
        Assert.True(decision.CanCancelManual);
        Assert.True(decision.CanCancelAutomation);
        Assert.True(decision.CanAutomate);
        Assert.Equal(3, decision.AutomationRepetitions);
        Assert.Equal(1, decision.AutomationUsed);
        Assert.Equal(2, decision.AutomationMaximum);
        Assert.Equal(page.craftingQueueInstances.GetGuid(), decision.QueueId);
        Assert.Equal(page.craftingAutomationInstances.GetGuid(), decision.AutomationQueueId);
        Assert.NotEqual(decision.QueueId, decision.AutomationQueueId);
        Assert.Equal(2, frame.CraftingQueueEntries.Count);
        ref readonly var manualEntry = ref frame.CraftingQueueEntries[0];
        Assert.Equal(page.craftingQueueInstances.GetGuid(), manualEntry.QueueId);
        Assert.Equal(0, manualEntry.Slot);
        Assert.Equal(recipe.GetGuid(), manualEntry.RecipeId);
        Assert.Equal(new BigDouble(4), manualEntry.Amount);
        Assert.False(manualEntry.Automatic);
        Assert.Equal(0, manualEntry.Repetitions);
        ref readonly var automaticEntry = ref frame.CraftingQueueEntries[1];
        Assert.Equal(page.craftingAutomationInstances.GetGuid(), automaticEntry.QueueId);
        Assert.Equal(recipe.GetGuid(), automaticEntry.RecipeId);
        Assert.Equal(new BigDouble(3), automaticEntry.Amount);
        Assert.True(automaticEntry.Automatic);
        Assert.Equal(3, automaticEntry.Repetitions);
    }

    [Fact]
    public void An_undiscovered_recipe_is_refused_for_being_undiscovered_not_for_a_full_queue()
    {
        var recipe = Recipe();
        recipe.visible = false;
        CraftingRecipeSO.All.Add(recipe);
        Page(recipe, mode: 0, maximum: 2);
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        reader.Collect(new HashSet<Guid>(), frame);

        ref readonly var decision = ref frame.CraftingDecisions[0];
        Assert.False(decision.CanAutomate);
        Assert.Equal(0, decision.AutomationUsed);
        Assert.Equal(2, decision.AutomationMaximum);
        Assert.Equal("hidden_or_undiscovered", decision.AutomationReasonCode);
    }

    [Fact]
    public void A_full_automation_queue_is_the_only_thing_that_reads_as_automation_full()
    {
        var recipe = Recipe();
        CraftingRecipeSO.All.Add(recipe);
        var other = Recipe();
        var page = Page(recipe, mode: 0, maximum: 1);
        page.craftingAutomationInstances.value.Add(
            new CraftingInstance(other, new BigDouble(1)).SetAuto(true));
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        reader.Collect(new HashSet<Guid>(), frame);

        ref readonly var decision = ref frame.CraftingDecisions[0];
        Assert.False(decision.CanAutomate);
        Assert.Equal(1, decision.AutomationUsed);
        Assert.Equal(1, decision.AutomationMaximum);
        Assert.Equal("automation_full", decision.AutomationReasonCode);
    }

    [Fact]
    public void AuthoredPageRoutingIsPinnedForLifecycleAndRecapturedAfterEpochChanges()
    {
        var recipe = Recipe();
        recipe.timeToComplete = 2d;
        CraftingRecipeSO.All.Add(recipe);
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        reader.Collect(new HashSet<Guid>(), frame);
        Assert.Equal(WorldCraftingPipeline.Unknown, frame.CraftingDecisions[0].Pipeline);

        Page(recipe, mode: 1, maximum: 2);
        reader.Collect(new HashSet<Guid>(), frame);
        Assert.Equal(WorldCraftingPipeline.Unknown, frame.CraftingDecisions[0].Pipeline);

        frame.CollectedAtEpoch = 8;
        reader.Collect(new HashSet<Guid>(), frame);
        Assert.Equal(WorldCraftingPipeline.QueueNew, frame.CraftingDecisions[0].Pipeline);
    }

    [Fact]
    public void QueueRoleContradictionFailsTheQueueContentReadInsteadOfPublishingAGuess()
    {
        var recipe = Recipe();
        CraftingRecipeSO.All.Add(recipe);
        var page = Page(recipe, mode: 0, maximum: 2);
        page.craftingQueueInstances.value.Add(
            new CraftingInstance(recipe, BigDouble.One));
        page.craftingQueueInstances.value.Add(
            new CraftingInstance(recipe, BigDouble.One).SetAuto(true));
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        var report = Reader().Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Contains("contradicted its manual or automatic list", report.FirstFailure);
        Assert.Equal(0, frame.CraftingQueueEntries.Count);
    }

    [Fact]
    public void DuplicatePageRelationSkipsOnlyImplicatedRecipe()
    {
        var implicated = Recipe();
        var unaffected = Recipe();
        CraftingRecipeSO.All.Add(implicated);
        CraftingRecipeSO.All.Add(unaffected);
        Page(implicated, mode: 0, maximum: 2);
        Page(implicated, mode: 1, maximum: 2);
        var reader = Reader();
        var frame = new GameWorldCycleFrame { CollectedAtEpoch = 7 };

        var report = reader.Collect(new HashSet<Guid>(), frame);

        Assert.Equal(1, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.Contains("more than one crafting page", report.FirstFailure);
        Assert.Equal(unaffected.GetGuid(), frame.CraftingDecisions[0].RecipeId);
    }

    private static WorldCraftingDecisionReader Reader() =>
        new(name => typeof(CraftingRecipeSO).Assembly.GetType(name, throwOnError: false));

    private static CraftingRecipeSO Recipe() =>
        new()
        {
            uuid = Guid.NewGuid(),
            visible = true,
            BuyAllowed = true,
            TotalCost = new ResourceCostList { affordable = true },
            MainType = new CraftingRecipeTypeSO { isLevelType = false },
        };

    private static ResourceSO Resource(int amount)
    {
        var resource = new ResourceSO
        {
            quantity = new BigDouble(amount, 0),
            quality = new ValueModifierRecord(new BigDouble(100, 0)),
        };
        resource.SetGuid(Guid.NewGuid());
        return resource;
    }

    private static UICraftingPage Page(CraftingRecipeSO recipe, int mode, int maximum)
    {
        var page = new UICraftingPage
        {
            mainCraftType = recipe.MainType,
            craftMode = new IntVariable { Value = mode },
            craftingQueueInstances = new CraftingInstanceListVariable
            {
                uuid = Guid.NewGuid(),
                Maximum = maximum,
            },
            craftingAutomationInstances = new CraftingInstanceListVariable
            {
                uuid = Guid.NewGuid(),
                Maximum = maximum,
                isAutoList = true,
            },
        };
        page.availableRecipes.value.Add(recipe);
        UnityEngine.Resources.Objects.Add(page);
        return page;
    }
}

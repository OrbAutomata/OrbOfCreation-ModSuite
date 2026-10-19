using System;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorCraftingTests : GameWorldCollectorTestBase
{
    [Fact]
    public void CraftingRecipesPublishNativeVerdictsAndConcreteRecipeEdgesTogether()
    {
        var recipeId = Guid.Parse("b1b7d331-587a-4b4c-87cf-4a8f57c8256b");
        var type = new FakeCraftingRecipeType { Identity = Guid.NewGuid() };
        var input = new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = 80d,
            quality = new FakeModifierRecord(100d),
            maxQuantity = new FakeModifierRecord(100d),
            usage = new FakeModifierRecord(4d),
            drain = new FakeModifierRecord(1.5d),
            bandwidthResource = true,
            Visible = true,
        };
        var generated = new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = 12d,
            quality = new FakeModifierRecord(100d),
            maxQuantity = new FakeModifierRecord(50d),
            Visible = true,
        };
        var sigil = new FakeConsumable { Identity = Guid.NewGuid() };
        var completion = new FakeScribeInstantBlock();
        completion.effectScripts.Add(new FakeScribeConsumableGainEffect { consumable = sigil });
        var recipe = new FakeScribeRecipe
        {
            Identity = recipeId,
            visible = false,
            canBuy = false,
            startingQuantity = new BigDouble(2d),
            useQuantityAsLevel = true,
            timeToComplete = 9.5d,
            recipeCost = new FakeCraftingResourceCostList().With(input, new BigDouble(3d)),
            generatedResources = new FakeCraftingResourceCostList
            {
                withinCapacity = false,
            }.With(generated, new BigDouble(7d)),
        };
        recipe.craftingTypes.Add(type);
        recipe.completeEffects.Add(completion);
        recipe.engagementEffects.Add(new FakeCraftingEngagementBlock
        {
            necessaryDrainRatio = new BigDouble(0.75d),
        });
        FakeCraftingRecipeType.All.Add(type);
        FakeResource.All.Add(input);
        FakeResource.All.Add(generated);
        FakeConsumable.All.Add(sigil);
        FakeScribeRecipe.All.Add(recipe);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.CraftingRecipes, recipeId, out var row));
        Assert.False(row.Reading.Visible);
        Assert.Equal("hidden_or_undiscovered", row.Reading.VisibilityReasonCode);
        Assert.False(row.Reading.CanBuyAtStartingQuantity);
        Assert.Equal("native_can_buy_refused", row.Reading.NativePurchaseReasonCode);
        Assert.Equal(2d, row.Reading.StartingQuantity.ToDouble());
        Assert.True(row.Reading.UseQuantityAsLevel);
        Assert.Equal(9.5d, row.Reading.TimeToComplete);
        Assert.False(row.Reading.OutputWithinCapacity);
        Assert.Equal("output_capacity_blocked", row.Reading.OutputCapacityReasonCode);

        var typeLink = Assert.Single(row.Types.AsSpan().ToArray());
        Assert.Equal(type.Identity, typeLink.TypeId);

        var resourceRows = row.Resources.AsSpan().ToArray();
        Assert.Equal(2, resourceRows.Length);
        var cost = Assert.Single(resourceRows, item =>
            item.Kind == WorldCraftingRecipeResourceKind.AuthoredInput);
        Assert.Equal(input.Identity, cost.ResourceId);
        Assert.Equal(3d, cost.Amount.ToDouble());
        Assert.True(cost.ResourceStateAvailable);
        Assert.True(cost.Visible);
        Assert.True(cost.BandwidthResource);
        Assert.Equal(80d, cost.TrueQuantity.ToDouble());
        Assert.True(cost.IsCapped);
        Assert.Equal(100d, cost.Capacity.ToDouble());
        Assert.Equal(4d, cost.Usage.ToDouble());
        Assert.Equal(1.5d, cost.Drain.ToDouble());
        var output = Assert.Single(resourceRows, item =>
            item.Kind == WorldCraftingRecipeResourceKind.GeneratedOutput);
        Assert.Equal(generated.Identity, output.ResourceId);
        Assert.Equal(7d, output.Amount.ToDouble());

        var consumable = Assert.Single(row.ConsumableOutputs.AsSpan().ToArray());
        Assert.Equal(sigil.Identity, consumable.ConsumableId);
        Assert.Equal("native_effect_scaling", consumable.QuantitySource);
        var drain = Assert.Single(row.DrainBlocks.AsSpan().ToArray());
        Assert.Equal(0.75d, drain.NecessaryRatio.ToDouble());
        Assert.True(drain.Blocked);
        Assert.Equal("engagement_drain_limited", drain.ReasonCode);
    }

    /// <summary>
    /// A recipe whose authored read throws anything but an expected native failure faults the pass
    /// instead of costing one recipe.
    /// </summary>
    [Fact]
    public void ACraftingRecipeWhoseAuthoringThrowsUnexpectedlyFaultsThePass()
    {
        var type = new FakeCraftingRecipeType { Identity = Guid.NewGuid() };
        var failure = new NullReferenceException("generated resource was destroyed");
        var output = new FakeResource { GetGuidFailure = failure };
        var recipe = new FakeScribeRecipe
        {
            generatedResources = new FakeCraftingResourceCostList().With(output, BigDouble.One),
        };
        recipe.craftingTypes.Add(type);
        FakeCraftingRecipeType.All.Add(type);
        FakeScribeRecipe.All.Add(recipe);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A recipe whose live read throws anything but an expected native failure faults the pass
    /// instead of costing one recipe.
    /// </summary>
    [Fact]
    public void ACraftingRecipeWhoseLiveStateThrowsUnexpectedlyFaultsThePass()
    {
        var type = new FakeCraftingRecipeType { Identity = Guid.NewGuid() };
        var failure = new NullReferenceException("starting quantity was destroyed");
        var recipe = new FakeScribeRecipe { StartingQuantityFailure = failure };
        recipe.craftingTypes.Add(type);
        FakeCraftingRecipeType.All.Add(type);
        FakeScribeRecipe.All.Add(recipe);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }
}

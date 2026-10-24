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
        var type = new CraftingRecipeTypeSO { Identity = Guid.NewGuid() };
        var input = new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = 80d,
            quality = new ValueModifierRecord(100d),
            maxQuantity = new ValueModifierRecord(100d),
            usage = new ValueModifierRecord(4d),
            drain = new ValueModifierRecord(1.5d),
            bandwidthResource = true,
            Visible = true,
        };
        var generated = new ResourceSO
        {
            Identity = Guid.NewGuid(),
            Quantity = 12d,
            quality = new ValueModifierRecord(100d),
            maxQuantity = new ValueModifierRecord(50d),
            Visible = true,
        };
        var sigil = new ConsumableSO { Identity = Guid.NewGuid() };
        var completion = new InstantEffectBlock();
        completion.effectScripts.Add(new ConsumableSO.ConsumableGainEffect { consumable = sigil });
        var recipe = new CraftingRecipeSO
        {
            Identity = recipeId,
            visible = false,
            BuyAllowed = false,
            StartingQuantity = new BigDouble(2d),
            useQuantityAsLevel = true,
            timeToComplete = 9.5d,
            recipeCost = new ResourceCostList().With(input, new BigDouble(3d)),
            generatedResources = new ResourceCostList
            {
                withinCapacity = false,
            }.With(generated, new BigDouble(7d)),
        };
        recipe.craftingTypes.Add(type);
        recipe.completeEffects.Add(completion);
        recipe.engagementEffects.Add(new PersistentEffectBlock
        {
            NecessaryDrainRatio = new BigDouble(0.75d),
        });
        CraftingRecipeTypeSO.All.Add(type);
        ResourceSO.All.Add(input);
        ResourceSO.All.Add(generated);
        ConsumableSO.All.Add(sigil);
        CraftingRecipeSO.All.Add(recipe);

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
        var type = new CraftingRecipeTypeSO { Identity = Guid.NewGuid() };
        var failure = new NullReferenceException("generated resource was destroyed");
        var output = new ResourceSO { GetGuidFailure = failure };
        var recipe = new CraftingRecipeSO
        {
            generatedResources = new ResourceCostList().With(output, BigDouble.One),
        };
        recipe.craftingTypes.Add(type);
        CraftingRecipeTypeSO.All.Add(type);
        CraftingRecipeSO.All.Add(recipe);

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
        var type = new CraftingRecipeTypeSO { Identity = Guid.NewGuid() };
        var failure = new NullReferenceException("starting quantity was destroyed");
        var recipe = new CraftingRecipeSO { StartingQuantityFailure = failure };
        recipe.craftingTypes.Add(type);
        CraftingRecipeTypeSO.All.Add(type);
        CraftingRecipeSO.All.Add(recipe);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }
}

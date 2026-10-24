using System;
using Xunit;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorDiscoveryTests : GameWorldCollectorTestBase
{
    [Fact]
    public void GenericDiscoveryPublishesTheAuthoredGlyphAndResourceRecipeOnceWithItsDecision()
    {
        var glyphComponent = new global::GlyphSO();
        glyphComponent.SetGuid(Guid.NewGuid());
        var resourceComponent = new global::ResourceSO();
        resourceComponent.SetGuid(Guid.NewGuid());
        var output = new GlyphSO
        {
            Identity = Guid.NewGuid(),
            NativeAvailable = true,
            NativeDiscoverVisible = true,
            NativeCanDiscover = true,
        };
        output.glyphRecipe.Add(glyphComponent);
        output.resourceRecipe.Add(resourceComponent);
        GlyphSO.All.Add(output);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        var row = Assert.Single(world.AugmentGlyphs.AsSpan().ToArray());
        Assert.Equal(glyphComponent.GetGuid(), Assert.Single(row.Discovery.GlyphRecipe.AsSpan().ToArray()));
        Assert.Equal(resourceComponent.GetGuid(), Assert.Single(row.Discovery.ResourceRecipe.AsSpan().ToArray()));
    }

    [Fact]
    public void DiscoveryTreesPublishExactDecisionCostsAndOrderedOfferIdentities()
    {
        var currency = new ResourceSO { Quantity = new BigDouble(563, 22) };
        var idle = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            rerollsLeft = 1,
            HasRemainingDiscovery = true,
            nextItemCost = new ResourceCostList { affordable = true },
            TotalDiscoveredCount = 3,
            allDiscoverableItems = { new DiscoveryTestItemSO(), new DiscoveryTestItemSO(), new DiscoveryTestItemSO(), new DiscoveryTestItemSO() },
            mainDiscoverableItemPool = { new DiscoveryTestItemSO(), new DiscoveryTestItemSO() },
        };
        idle.nextItemCost.costs.Add(
            new ResourceTuple(currency, new BigDouble(11, 23)));

        var firstOffer = Guid.NewGuid();
        var secondOffer = Guid.NewGuid();
        var choice = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Choice,
            rerollsLeft = 2,
            HasRemainingDiscovery = true,
        };
        choice.currentChoiceIds.Add(new GuidContainer(firstOffer));
        choice.currentChoiceIds.Add(new GuidContainer(secondOffer));
        DiscoveryTreeSO.All.Add(idle);
        DiscoveryTreeSO.All.Add(choice);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.DiscoveryTrees, idle.Identity, out var idleRow));
        Assert.True(idleRow.Visible);
        Assert.True(idleRow.NextItemAffordable);
        Assert.Equal(1, idleRow.NextItemCosts.Count);
        Assert.Equal(currency.Identity, idleRow.NextItemCosts[0].ResourceId);
        Assert.Equal(new BigDouble(11, 23), idleRow.NextItemCosts[0].Price);
        Assert.Equal(new BigDouble(563, 22), idleRow.NextItemCosts[0].Held);

        // The cached count is a count of the tree's own list, so the list's size travels with it:
        // three of four is progress a caller can read, three on its own is not.
        Assert.Equal(3, idleRow.TotalDiscoveredCount);
        Assert.Equal(4, idleRow.TotalDiscoverableCount);
        Assert.Equal(2, idleRow.PoolDiscoverableCount);

        Assert.True(WorldLookup.TryFind(world.DiscoveryTrees, choice.Identity, out var choiceRow));
        Assert.Equal(2, choiceRow.CurrentOfferIds.Count);
        Assert.Equal(firstOffer, choiceRow.CurrentOfferIds[0]);
        Assert.Equal(secondOffer, choiceRow.CurrentOfferIds[1]);
    }

    [Fact]
    public void ADiscoveryTreeBookThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        var tree = new DiscoveryTreeSO();
        tree.availableRecipeBooks!.value.Add(
            new RecipeBookSO { GetGuidFailure = new NullReferenceException("tree book") });
        DiscoveryTreeSO.All.Add(tree);

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    /// <summary>
    /// The augment glyph reader reads the same book link first, so this pass leaves it unbound by
    /// withholding the cost-list type it needs and the recipe-book glyph reader needs nothing of.
    /// </summary>
    [Fact]
    public void ARecipeBookGlyphThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        GlyphSO.All.Add(new GlyphSO
        {
            associatedRecipeBook = new RecipeBookSO
            {
                GetGuidFailure = new NullReferenceException("glyph book"),
            },
        });

        Assert.Throws<NullReferenceException>(
            () => Collector(("ResourceCostList", null)).Collect());
    }
}

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
        var output = new FakeGlyph
        {
            Identity = Guid.NewGuid(),
            NativeAvailable = true,
            NativeDiscoverVisible = true,
            NativeCanDiscover = true,
        };
        output.genericDiscoveryGlyphs.Add(glyphComponent);
        output.genericDiscoveryResources.Add(resourceComponent);
        FakeGlyph.All.Add(output);

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
        var currency = new FakeResource { Quantity = new BigDouble(563, 22) };
        var idle = new FakeDiscoveryTree
        {
            actionMode = FakeState.Idle,
            rerollsLeft = 1,
            hasRemainingDiscovery = true,
            nextItemCost = new FakeDiscoveryCostList { affordable = true },
            totalDiscoveredCount = 3,
            allDiscoverableItems = { new object(), new object(), new object(), new object() },
            mainDiscoverableItemPool = { new object(), new object() },
        };
        idle.nextItemCost.costs.Add(
            new FakeDiscoveryCost(currency, new BigDouble(11, 23)));

        var firstOffer = Guid.NewGuid();
        var secondOffer = Guid.NewGuid();
        var choice = new FakeDiscoveryTree
        {
            actionMode = FakeState.Done,
            rerollsLeft = 2,
            hasRemainingDiscovery = true,
        };
        choice.currentChoiceIds.Add(new FakeGuidContainer(firstOffer));
        choice.currentChoiceIds.Add(new FakeGuidContainer(secondOffer));
        FakeDiscoveryTree.All.Add(idle);
        FakeDiscoveryTree.All.Add(choice);

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
        var tree = new FakeDiscoveryTree();
        tree.availableRecipeBooks!.value.Add(
            new FakeRecipeBook { GetGuidFailure = new NullReferenceException("tree book") });
        FakeDiscoveryTree.All.Add(tree);

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    /// <summary>
    /// The augment glyph reader reads the same book link first, so this pass leaves it unbound by
    /// withholding the cost-list type it needs and the recipe-book glyph reader needs nothing of.
    /// </summary>
    [Fact]
    public void ARecipeBookGlyphThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        FakeGlyph.All.Add(new FakeGlyph
        {
            associatedRecipeBook = new FakeRecipeBook
            {
                GetGuidFailure = new NullReferenceException("glyph book"),
            },
        });

        Assert.Throws<NullReferenceException>(
            () => Collector(("ResourceCostList", null)).Collect());
    }
}

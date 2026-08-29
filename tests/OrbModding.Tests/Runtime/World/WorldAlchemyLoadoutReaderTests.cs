using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins what the ordinary alchemy reader publishes out of a multi-row loadout: which rows, in which
/// order, carrying which values, and what it does instead when the game does not hold up its end.
/// </summary>
/// <remarks>
/// The collector test that covers this category seeds one recipe and one holding, which leaves the
/// ordering, the core filter, and every arithmetic term of the click decision unpinned. How the
/// reader spells a member read — reflective invocation or a compiled accessor — is an implementation
/// choice underneath these assertions.
/// </remarks>
public sealed class WorldAlchemyLoadoutReaderTests : IDisposable
{
    private static readonly Guid FirstRecipeId = new("e1111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondRecipeId = new("e2222222-2222-2222-2222-222222222222");
    private static readonly Guid ThirdRecipeId = new("e3333333-3333-3333-3333-333333333333");
    private static readonly Guid ForeignRecipeId = new("e4444444-4444-4444-4444-444444444444");
    private static readonly Guid ForeignCoreId = new("e5555555-5555-5555-5555-555555555555");
    private static readonly Guid FirstResourceId = new("f1111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondResourceId = new("f2222222-2222-2222-2222-222222222222");

    public WorldAlchemyLoadoutReaderTests() => Clear();

    public void Dispose() => Clear();

    [Fact]
    public void CollectsEveryOrdinaryHoldingInNativeOrderWithItsValues()
    {
        Seed();
        var frame = new GameWorldCycleFrame();

        var report = new WorldAlchemyLoadoutReader(Resolve).Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Collected, report.Outcome);
        Assert.Equal(string.Empty, report.FirstFailure);
        Assert.Equal(0, report.Skipped);

        // The Concept recipe sharing the registry is not an ordinary one and is passed over in
        // silence rather than counted as a shortfall.
        Assert.Equal(3, report.Sampled);
        Assert.Equal(3, frame.AlchemyLoadout.Count);

        // A discovered recipe nothing holds: no position, no holding, and the whole maximum its
        // recipe allows, because an empty price cannot cap how many may be added.
        var first = frame.AlchemyLoadout[0];
        Assert.Equal(FirstRecipeId, first.RecipeId);
        Assert.Equal(-1, first.Position);
        Assert.Equal(2, first.SlotCount);
        Assert.Equal(0, first.Amount);
        Assert.Equal(0, first.TargetAmount);
        Assert.Equal(4, first.FreeUsesRemaining);
        Assert.Equal(9, first.MaximumAdd);
        Assert.True(first.Discovered);
        Assert.True(first.CanAdd);
        Assert.False(first.IsActive);

        // A held recipe reads its remaining slots off the holding rather than off the recipe, and the
        // price caps the maximum below what the slots alone would allow: 1000 / 250 is four more
        // purchases, plus the two free uses left, against seven remaining slots.
        var second = frame.AlchemyLoadout[1];
        Assert.Equal(SecondRecipeId, second.RecipeId);
        Assert.Equal(1, second.Position);
        Assert.Equal(2, second.SlotCount);
        Assert.Equal(2, second.Amount);
        Assert.Equal(3, second.TargetAmount);
        Assert.Equal(2, second.FreeUsesRemaining);
        Assert.Equal(6, second.MaximumAdd);
        Assert.False(second.Discovered);
        Assert.True(second.CanAdd);
        Assert.True(second.IsActive);

        // A recipe already holding every slot it allows: the game refuses another, and the reader
        // publishes the refusal beside a maximum of none rather than in place of one.
        var third = frame.AlchemyLoadout[2];
        Assert.Equal(ThirdRecipeId, third.RecipeId);
        Assert.Equal(0, third.Position);
        Assert.Equal(0, third.Amount);
        Assert.Equal(4, third.TargetAmount);
        Assert.Equal(0, third.FreeUsesRemaining);
        Assert.Equal(0, third.MaximumAdd);
        Assert.True(third.Discovered);
        Assert.False(third.CanAdd);

        // Usage costs come out per sampled recipe in recipe order, and within a recipe in the order
        // the native cost list holds them. The empty-priced recipe contributes none.
        Assert.Equal(3, frame.AlchemyUsageCosts.Count);
        AssertCost(frame.AlchemyUsageCosts[0], SecondRecipeId, FirstResourceId, 250d);
        AssertCost(frame.AlchemyUsageCosts[1], ThirdRecipeId, FirstResourceId, 100d);
        AssertCost(frame.AlchemyUsageCosts[2], ThirdRecipeId, SecondResourceId, 400d);
    }

    /// <summary>An absent registry position is skipped by its position rather than read as a recipe.</summary>
    [Fact]
    public void AnAbsentRecipePositionIsSkippedByItsPosition()
    {
        Seed();
        global::AlchemyManager.instance!.allAlchemy.value.Insert(0, null!);

        var frame = new GameWorldCycleFrame();
        var report = new WorldAlchemyLoadoutReader(Resolve).Collect(new HashSet<Guid>(), frame);

        Assert.Equal(3, report.Sampled);
        Assert.Equal(1, report.Skipped);
        Assert.Equal("recipe 0 had an unexpected native type", report.FirstFailure);
        Assert.Equal(3, frame.AlchemyLoadout.Count);
    }

    /// <summary>
    /// A native refusal degrades the whole category with the game's own message, rather than being
    /// published as a partial reading or escaping into the pass.
    /// </summary>
    [Fact]
    public void ANativeThrowDegradesTheCategoryWithItsMessage()
    {
        Seed();
        global::AlchemyManager.instance!.activeAlchemy.ThrowOnCanAdd = true;

        var frame = new GameWorldCycleFrame();
        var report = new WorldAlchemyLoadoutReader(Resolve).Collect(new HashSet<Guid>(), frame);

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Equal("reading ordinary alchemy threw: CanAddInstance failed", report.FirstFailure);
        Assert.Equal(0, frame.AlchemyLoadout.Count);
        Assert.Equal(0, frame.AlchemyUsageCosts.Count);
    }

    /// <summary>A manager the game has not created yet is named rather than read through.</summary>
    [Fact]
    public void AnAbsentManagerIsNamedRatherThanReadThrough()
    {
        Seed();
        global::AlchemyManager.instance = null;

        var report = new WorldAlchemyLoadoutReader(Resolve)
            .Collect(new HashSet<Guid>(), new GameWorldCycleFrame());

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.Outcome);
        Assert.Equal("AlchemyManager.instance is unavailable", report.FirstFailure);
    }

    /// <summary>A member the build does not expose leaves the category unavailable and says so.</summary>
    [Fact]
    public void AMemberThatCannotBindLeavesTheCategoryUnavailableAndNamesIt()
    {
        var reader = new WorldAlchemyLoadoutReader(
            name => name == "AlchemyInstance" ? typeof(global::AlchemyTypeSO) : Resolve(name));

        Assert.False(reader.IsAvailable);
        Assert.Equal(
            "the ordinary alchemy list or decision members were unavailable",
            reader.Collect(new HashSet<Guid>(), new GameWorldCycleFrame()).FirstFailure);
    }

    /// <summary>A build without the ordinary alchemy types at all names that instead.</summary>
    [Fact]
    public void AnAbsentTypeIsNamedSeparately()
    {
        var reader = new WorldAlchemyLoadoutReader(
            name => name == "ResourceCostList" ? null : Resolve(name));

        Assert.False(reader.IsAvailable);
        Assert.Equal(
            "the ordinary alchemy types were not found on this build",
            reader.Collect(new HashSet<Guid>(), new GameWorldCycleFrame()).FirstFailure);
    }

    private static void AssertCost(
        in WorldAlchemyUsageCost row,
        Guid recipeId,
        Guid resourceId,
        double amount)
    {
        Assert.Equal(recipeId, row.RecipeId);
        Assert.Equal(resourceId, row.ResourceId);
        Assert.Equal(amount, row.Amount.ToDouble());
    }

    private static Type? Resolve(string name) =>
        typeof(global::AlchemyRecipeSO).Assembly.GetType(name, throwOnError: false);

    private static void Clear()
    {
        global::AlchemyManager.instance = null;
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::AlchemyRecipeSO.All.Clear();
        global::AlchemyTypeSO.All.Clear();
        global::UnityEngine.Resources.Objects.Clear();
    }

    private static void Seed()
    {
        var manager = new global::AlchemyManager();
        global::AlchemyManager.instance = manager;

        var first = Recipe(FirstRecipeId, KnownEntities.Alchemy.Uuid, free: 4, maximum: 9);
        var second = Recipe(SecondRecipeId, KnownEntities.Brewing.Uuid, free: 5, maximum: 10);
        second.discovered = false;
        second.usageCost.costs.Add(
            new global::ResourceTuple(Resource(FirstResourceId), new BigDouble(250d, 0)));
        var third = Recipe(ThirdRecipeId, KnownEntities.Refinement.Uuid, free: 0, maximum: 2);
        third.usageCost.costs.Add(
            new global::ResourceTuple(Resource(FirstResourceId), new BigDouble(100d, 0)));
        third.usageCost.costs.Add(
            new global::ResourceTuple(Resource(SecondResourceId), new BigDouble(400d, 0)));

        // A Concept recipe shares the same registry; its core type is not one of the six ordinary
        // families, which is the whole reason the reader filters on the core rather than the list.
        var foreign = Recipe(ForeignRecipeId, ForeignCoreId, free: 1, maximum: 1);

        manager.allAlchemy.value.Add(first);
        manager.allAlchemy.value.Add(second);
        manager.allAlchemy.value.Add(third);
        manager.allAlchemy.value.Add(foreign);

        manager.activeAlchemy.value.Add(new global::AlchemyInstance(third) { queuedQuantity = 4 });
        manager.activeAlchemy.value.Add(
            new global::AlchemyInstance(second) { quantity = 2, queuedQuantity = 3 });
    }

    private static global::AlchemyRecipeSO Recipe(Guid id, Guid coreId, int free, int maximum)
    {
        var core = new global::AlchemyTypeSO();
        core.SetGuid(coreId);
        return new global::AlchemyRecipeSO(id.ToString(), "Alchemy", new[] { core })
        {
            coreType = core,
            freeUsageSlots = new global::ValueModifierRecord(new BigDouble(free, 0)),
            maxUsageSlots = new global::ValueModifierRecord(new BigDouble(maximum, 0)),
        };
    }

    private static global::ResourceSO Resource(Guid id) => new() { uuid = id.ToString() };
}

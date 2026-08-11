using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The Concept slot budget, on the two surfaces a caller reaches it through. A refusal to assign
/// used to arrive as a bare <c>canAdd: no</c>, with the capacity behind it inferable only by
/// counting assignment rows, and the detail read on the same uuid answered as a plain alchemy
/// recipe with the assignment state dropped entirely.
/// </summary>
public sealed class GameMcpConceptSlotTests
{
    private static readonly Guid AnalyzeBlooming =
        Guid.Parse("9ec75f9b-e074-44fc-b7be-05cf556847ad");
    private static readonly Guid AnalyzeBuilding =
        Guid.Parse("b4d22a59-30d6-4de3-8437-f3ea8cbc3c12");
    private static readonly Guid CoreType =
        Guid.Parse("c0000000-0000-4000-8000-000000000001");

    [Fact]
    public void A_refused_assignment_names_its_class_its_sentence_and_the_slots_behind_it()
    {
        var context = GameMcpTestHarness.Context(World(canAdd: false, slots: 2), generation: 3101);

        var row = Assert.IsType<JObject>(GameMcpTestHarness.Json(
            GameMcpWorldQuery.GetRow(
                context, "concept-recipes", AnalyzeBlooming.ToString("D")).Freeze())["row"]);

        Assert.Equal(2, (int)row["usedSlots"]!);
        Assert.Equal(2, (int)row["maximumSlots"]!);
        Assert.False((bool)row["canAdd"]!["available"]!);
        Assert.Equal("ERR_LIMIT", (string?)row["canAdd"]!["reasonCode"]);
        Assert.Equal("Every Concept slot is in use.", (string?)row["canAdd"]!["reason"]);
    }

    /// <summary>
    /// A list with room that still refuses this recipe is a different answer from a full list, and
    /// the sentence has to say which one it is or the caller frees a slot for nothing.
    /// </summary>
    [Fact]
    public void A_refusal_with_room_left_says_so_instead_of_claiming_the_slots_are_gone()
    {
        var context = GameMcpTestHarness.Context(World(canAdd: false, slots: 6), generation: 3102);

        var row = Assert.IsType<JObject>(GameMcpTestHarness.Json(
            GameMcpWorldQuery.GetRow(
                context, "concept-recipes", AnalyzeBlooming.ToString("D")).Freeze())["row"]);

        Assert.Equal(2, (int)row["usedSlots"]!);
        Assert.Equal(6, (int)row["maximumSlots"]!);
        Assert.Equal(
            "Slots are free, and the game still will not take this Concept into one.",
            (string?)row["canAdd"]!["reason"]);
    }

    [Fact]
    public void An_admitted_assignment_carries_no_class_and_no_sentence()
    {
        var context = GameMcpTestHarness.Context(World(canAdd: true, slots: 6), generation: 3103);

        var row = Assert.IsType<JObject>(GameMcpTestHarness.Json(
            GameMcpWorldQuery.GetRow(
                context, "concept-recipes", AnalyzeBlooming.ToString("D")).Freeze())["row"]);

        Assert.True((bool)row["canAdd"]!["available"]!);
        Assert.Null(row["canAdd"]!["reasonCode"]);
        Assert.Null(row["canAdd"]!["reason"]);
    }

    /// <summary>
    /// One uuid is both an alchemy recipe and a Concept recipe. Answering it under the one category and
    /// dropping the other half answered a question the caller did not ask.
    /// </summary>
    [Fact]
    public void Reading_a_concept_recipe_keeps_its_assignment_state()
    {
        var context = GameMcpTestHarness.Context(World(canAdd: false, slots: 2), generation: 3104);

        var concept = GameMcpTestHarness.Detail(context, AnalyzeBlooming);
        var plain = GameMcpTestHarness.Detail(context, AnalyzeBuilding);

        Assert.Equal("alchemy-recipes", (string?)concept["category"]);
        var state = concept["concept"]!;
        Assert.Equal(1, (int)state["assignedCount"]!);
        Assert.Equal(2, (int)state["usedSlots"]!);
        Assert.Equal(2, (int)state["maximumSlots"]!);
        Assert.False((bool)state["canAdd"]!["available"]!);
        Assert.Equal("ERR_LIMIT", (string?)state["canAdd"]!["reasonCode"]);
        Assert.Equal(
            "Every Concept slot is in use.", (string?)state["canAdd"]!["reason"]);

        // One decision, published once. The predicate names the block that holds it rather than
        // reprinting the same verdict byte for byte beside it.
        Assert.Equal("concept.canAdd", (string?)concept["predicates"]!["canAdd"]);

        // An alchemy recipe the Concept registry does not name carries none of it, so the block's
        // presence is the answer to "is this assignable at all".
        Assert.Null(plain["concept"]);
        Assert.Null(plain["predicates"]!["canAdd"]);
    }

    private static GameWorldState World(bool canAdd, int slots) => new()
    {
        CollectedAtEpoch = 41,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(
            new[] { Recipe(AnalyzeBuilding), Recipe(AnalyzeBlooming) }
                .OrderBy(recipe => recipe.EntityId)
                .ToArray()),
        ConceptRecipes = PublicationTable<WorldConceptRecipe>.Create(new[]
        {
            new WorldConceptRecipe(AnalyzeBlooming, CoreType, canAdd, slots),
        }),
        AlchemyInstances = PublicationTable<WorldAlchemyInstance>.Create(
            new[]
            {
                new WorldAlchemyInstance(AnalyzeBlooming, 1, 1, true, BigDouble.One),
                new WorldAlchemyInstance(AnalyzeBuilding, 4, 4, true, BigDouble.One),
            }.OrderBy(instance => instance.RecipeId).ToArray()),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "alchemy recipes", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
            new WorldCollectionCategoryStatus(
                "concept instances", WorldCategoryOutcome.Collected, 2, 0, string.Empty),
        }),
    };

    private static WorldAlchemyRecipe Recipe(Guid id) => new(
        id, CoreType, discovered: true, maxLevel: 1, advancementLevel: 0,
        discoveryRarityLevel: 0, masteryXp: BigDouble.Zero, masteryLevel: 0,
        recipeTime: BigDouble.One, isRequiredDiscovery: false,
        isCompletionRecipe: false, isAdvancementRecipe: false, completionTime: 0,
        isDebugAlchemy: false, power: BigDouble.Zero, speed: BigDouble.Zero,
        drainCostMod: BigDouble.Zero, special: BigDouble.Zero,
        timeReqMod: BigDouble.Zero, timeScalingMod: BigDouble.Zero,
        masteryXpRate: BigDouble.Zero, effectLevels: BigDouble.Zero,
        overdrivePower: BigDouble.Zero, overdriveSpeed: BigDouble.Zero,
        overdriveDrainCostMod: BigDouble.Zero, overdriveXpRate: BigDouble.Zero,
        freeUsageSlots: BigDouble.One, maxUsageSlots: new BigDouble(8),
        cachedCompletionTime: BigDouble.Zero, requiredExperience: BigDouble.One);
}

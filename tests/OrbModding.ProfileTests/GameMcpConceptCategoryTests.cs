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
/// Concepts are the seventh discovery surface, so they are a category of their own. One native
/// class backs two player concepts — the seventy-nine recipes the Alchemy screen draws and the
/// forty-six Concepts the Scholar screen draws — and every read files a row under the page the
/// player meets it on, in a word that page answers to.
/// </summary>
public sealed class GameMcpConceptCategoryTests
{
    /// <summary>Study Mind, a Concept the Scholar screen lists.</summary>
    private static readonly Guid StudyMind =
        Guid.Parse("fa165240-42a6-447d-9d3b-f6fa2865dbf9");

    /// <summary>Analyze Blooming, a Concept on the same list.</summary>
    private static readonly Guid AnalyzeBlooming =
        Guid.Parse("9ec75f9b-e074-44fc-b7be-05cf556847ad");

    /// <summary>Magic Learning, a Concept on the same list.</summary>
    private static readonly Guid MagicLearning =
        Guid.Parse("20a0b45e-38ea-4f41-9e71-79686140d1cc");

    /// <summary>Brew Mana Potion, an ordinary Alchemy recipe that must not move.</summary>
    private static readonly Guid BrewManaPotion =
        Guid.Parse("ef29f6df-4660-4d83-bf62-1230cf1a23de");

    [Fact]
    public void The_concepts_page_lists_the_concepts_and_the_alchemy_page_keeps_its_recipe()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5101);

        var concepts = Rows(context, "concepts");
        var alchemy = Rows(context, "alchemy-recipes");

        Assert.Equal(
            new[] { "Analyze Blooming", "Magic Learning", "Study Mind" },
            concepts.Select(row => (string?)row["name"]).OrderBy(name => name).ToArray());
        Assert.Equal(
            new[] { "Brew Mana Potion" },
            alchemy.Select(row => (string?)row["name"]).ToArray());
    }

    /// <summary>
    /// The word a row prints under <c>category</c> is the word the list verb takes. Round 15 saw
    /// the two differ once, which is a page a caller cannot reach from the row that named it.
    /// </summary>
    [Fact]
    public void The_category_a_row_prints_is_the_category_the_list_verb_answers_to()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5102);

        foreach (var uuid in new[] { StudyMind, AnalyzeBlooming, MagicLearning, BrewManaPotion })
        {
            var printed = (string?)GameMcpTestHarness.Detail(context, uuid)["category"];

            Assert.NotNull(printed);
            var page = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ListRows(context, printed!, 0, 50));

            Assert.Contains(
                page["rows"]!.Values<JObject>(),
                row => (string?)row!["uuid"] == GameMcpTestHarness.Handle(uuid));
        }
    }

    /// <summary>
    /// Naming the other table is a miss in that table, not a quiet resolution into the one the
    /// caller did not ask for.
    /// </summary>
    [Fact]
    public void A_concept_addressed_as_an_alchemy_recipe_misses_in_that_table()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5103);

        var miss = GameMcpTestHarness.Detail(context, "alchemy-recipes", StudyMind);

        Assert.Equal("unavailable", (string?)miss["status"]);
        Assert.Equal("ERR_NOT_FOUND", (string?)miss["reasonCode"]);
        Assert.Equal("There is no alchemy-recipes entry with that id.", (string?)miss["reason"]);
        Assert.Equal("concepts", (string?)GameMcpTestHarness.Detail(context, StudyMind)["category"]);
    }

    [Fact]
    public void Search_files_a_concept_under_concepts_and_a_recipe_under_alchemy_recipes()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5104);

        var page = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, string.Empty, 0, 50, stateFilter: "available", limitFromCaller: false));
        var byName = page["rows"]!.Values<JObject>()
            .ToDictionary(row => (string?)row!["name"] ?? string.Empty, row => row!);

        Assert.Equal("concepts", (string?)byName["Study Mind"]["category"]);
        Assert.Equal("concepts", (string?)byName["Analyze Blooming"]["category"]);
        Assert.Equal("concepts", (string?)byName["Magic Learning"]["category"]);
        Assert.Equal("alchemy-recipes", (string?)byName["Brew Mana Potion"]["category"]);
    }

    /// <summary>
    /// Narrowing search to one of the two pages returns that page's rows and none of the other's,
    /// which is the whole point of the filter having two words to take.
    /// </summary>
    [Fact]
    public void A_search_narrowed_to_one_page_answers_with_that_pages_rows_alone()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5105);

        var concepts = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, string.Empty, 0, 50, "concepts", limitFromCaller: false));
        var alchemy = GameMcpTestHarness.Json(GameMcpWorldQuery.Search(
            context, string.Empty, 0, 50, "alchemy-recipes", limitFromCaller: false));

        Assert.Equal(3, concepts["rows"]!.Values<JObject>().Count());
        Assert.Equal(
            new[] { "Brew Mana Potion" },
            alchemy["rows"]!.Values<JObject>().Select(row => (string?)row!["name"]).ToArray());
    }

    /// <summary>
    /// The inventory counts each page for itself, and the name it used to publish them all under
    /// says where they went rather than answering as an unknown word.
    /// </summary>
    [Fact]
    public void The_category_inventory_counts_both_pages_and_the_retired_name_points_at_one()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5106);

        var inventory = GameMcpTestHarness.Json(GameMcpWorldQuery.ListCategories(context))
            ["categories"]!.Values<JObject>()
            .ToDictionary(row => (string?)row!["category"] ?? string.Empty, row => row!);

        Assert.Equal(3, (int)inventory["concepts"]["count"]!);
        Assert.Equal(1, (int)inventory["alchemy-recipes"]["count"]!);
        Assert.DoesNotContain("concept-recipes", inventory.Keys);

        var retired = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(context, "concept-recipes", 0, 50));

        Assert.Equal("unavailable", (string?)retired["status"]);
        Assert.Equal(
            "unknown category 'concept-recipes'; the forty-six Concepts are a category of their " +
            "own now: list 'concepts'. This name republished them beside the seventy-nine " +
            "Alchemy recipes, and the Scholar screen draws them nowhere near the Alchemy screen",
            (string?)retired["reason"]);
    }

    /// <summary>
    /// The overview counts the two layers apart. One number over both agreed with neither page.
    /// </summary>
    [Fact]
    public void The_overview_counts_discovered_concepts_apart_from_discovered_recipes()
    {
        var context = GameMcpTestHarness.Context(World(), generation: 5107);

        var progression = GameMcpTestHarness.Json(
            GameMcpWorldQuery.Overview(context))["progression"]!;

        Assert.Equal(3, (int)progression["discoveredConcepts"]!);
        Assert.Equal(1, (int)progression["discoveredAlchemyRecipes"]!);
    }

    private static JObject[] Rows(GameMcpFrameContext context, string category) =>
        GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(context, category, 0, 50))
            ["rows"]!.Values<JObject>().Select(row => row!).ToArray();

    private static GameWorldState World() => new()
    {
        CollectedAtEpoch = 51,
        CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(
            new[]
            {
                Recipe(StudyMind, KnownEntities.Reductive.Uuid),
                Recipe(AnalyzeBlooming, KnownEntities.Reductive.Uuid),
                Recipe(MagicLearning, KnownEntities.Reflective.Uuid),
                Recipe(BrewManaPotion, KnownEntities.Brewing.Uuid),
            }.OrderBy(recipe => recipe.EntityId).ToArray()),
        CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
        {
            new WorldCollectionCategoryStatus(
                "alchemy recipes", WorldCategoryOutcome.Collected, 4, 0, string.Empty),
        }),
    };

    private static WorldAlchemyRecipe Recipe(Guid id, Guid coreTypeId) => new(
        id, coreTypeId, discovered: true, maxLevel: 1, advancementLevel: 0,
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

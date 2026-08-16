using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// The game's own glossary on the wire: the word it prints above a number, which of the three
/// sections of a tooltip that word heads, and the sentence it prints under it.
/// </summary>
/// <remarks>
/// Every id, display name, display type and sentence here is the pinned build's own
/// (<c>data/game-data.json</c>, <c>objects.AttributeSO</c>): one record of each of the three words,
/// the single record that names no word at all, and one of the eight the game authors no sentence
/// for. A glossary a reader has to hover something to read is the state this category replaced, so
/// the assertions are about what one page says without a second call.
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
public sealed class GameMcpStatisticGlossaryTests : IDisposable
{
    private static readonly Guid Cost = Guid.Parse("bb3cd428-054a-4e36-9f5e-7e83cbd9c5bb");
    private static readonly Guid AdditionalInformation =
        Guid.Parse("1ab06404-18d4-4bca-b86f-f0a8b4552755");
    private static readonly Guid CreateArtifact =
        Guid.Parse("af661273-3770-4e50-bf9f-13998b012422");
    private static readonly Guid StartingLevel =
        Guid.Parse("a50895d9-5951-4273-a859-89809a82d115");
    private static readonly Guid ArtifactExperience =
        Guid.Parse("0a13f2d2-ab8c-4b4e-be0d-d9b19a469e0d");
    private static readonly Guid ElementalResonance =
        Guid.Parse("399623bf-9407-4c44-8478-b72d5c499d0e");

    public GameMcpStatisticGlossaryTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    /// <summary>
    /// One page carries the word, the section it heads, and the sentence — because the sentence is
    /// the whole reason a reader opens this category, and a glossary charging one call an entry is
    /// 211 calls for what the game prints in one column.
    /// </summary>
    [Fact]
    public void The_glossary_pages_the_word_the_section_and_the_sentence_together()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 6/6",
                "5 of 6 share: isPercent=no; 399623 yes",
                "[id | name | displayType | description]",
                "0a13f2 | Artifact Experience | Statistic | -",
                "1ab064 | Additional Info | Information | Some extra details about this component.",
                "399623 | Elemental Resonance | Statistic | How much elemental modifications " +
                    "affect this spell.",
                "a50895 | Starting Level | - | What level to initialize this craft at.",
                "af6612 | Create Artifact | Action | Create an artifact from the selected glyphs.",
                "bb3cd4 | Cost | Statistic | The amount of resources a component costs to cast, " +
                    "use or purchase.",
            }),
            Render(Json(GameMcpWorldQuery.ListRows(
                Context(World()), "statistics", 0, 50, limitFromCaller: false))));
    }

    /// <summary>
    /// The sentence is said once. <c>world_get</c> already carries the game's own words for every
    /// entity it answers on, so a row that repeated them under a second name would spend a caller's
    /// bytes saying one fact twice.
    /// </summary>
    [Fact]
    public void One_statistic_says_its_sentence_once()
    {
        var native = new global::AttributeSO
        {
            displayName = "Cost",
            description = "The amount of resources a component costs to cast, use or purchase.",
        };
        native.SetGuid(Cost);
        global::IdScriptableObject.RuntimeLookup[Cost] = native;

        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: bb3cd4",
                "name: Cost",
                "category: statistics",
                "description: The amount of resources a component costs to cast, use or purchase.",
                "row: displayType=Statistic",
            }),
            Render(Detail(Cost)));
    }

    /// <summary>
    /// Eight of the 211 are UI plumbing the game authors no sentence for. Each is still a row: the
    /// word it heads is a word the screen prints, and a glossary that dropped the entries whose
    /// meaning the game leaves implicit would be a glossary a reader could not trust to be whole.
    /// </summary>
    /// <remarks>
    /// The two surfaces spell the gap the two ways this surface spells every gap. A list page's
    /// header promises the column on every row of the category, so the cell is there and empty and
    /// the page renders it <c>-</c>; a detail block promises no header, so the field is absent.
    /// </remarks>
    [Fact]
    public void A_statistic_the_game_authors_no_sentence_for_is_a_row_with_no_sentence()
    {
        var context = Context(World());
        var listed = Json(GameMcpWorldQuery.ListRows(context, "statistics", 0, 50))["rows"]!
            .Values<JObject>()
            .Single(row => (string?)row!["name"] == "Artifact Experience")!;

        Assert.Equal(string.Empty, (string?)listed["description"]);
        Assert.Equal("Statistic", (string?)listed["displayType"]);

        var block = Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                context, string.Empty, new[] { ArtifactExperience.ToString("D") }))
            ["results"]!.Values<JObject>())!;

        Assert.Null(block["description"]);
        Assert.Null(block["row"]!["description"]);
        Assert.Equal("Statistic", (string?)block["row"]!["displayType"]);
    }

    /// <summary>
    /// The word is addressable now, so the search that finds every other entity by its screen word
    /// finds a definition by its screen word too.
    /// </summary>
    [Fact]
    public void The_glossary_is_reachable_by_the_word_the_screen_prints()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn]",
                "399623 | Elemental Resonance | statistics | - | name",
            }),
            Render(Json(GameMcpWorldQuery.Search(Context(World()), "Resonance", 0, 10))));
    }

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Detail(Guid uuid) =>
        Assert.Single(
            Json(GameMcpWorldQuery.GetRows(
                Context(World()), string.Empty, new[] { uuid.ToString("D") }))
            ["results"]!.Values<JObject>())!;

    private static JObject Json(GameMcpObjectBuilder value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value.Freeze(), Catalog));

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, Sorted(new[]
        {
            new EntityIdentityName(Cost, "AttributeSO", "Cost", "Cost"),
            new EntityIdentityName(
                AdditionalInformation, "AttributeSO", "Additional Info", "AdditionalInformation"),
            new EntityIdentityName(
                CreateArtifact, "AttributeSO", "Create Artifact", "CreateArtifact"),
            new EntityIdentityName(
                StartingLevel, "AttributeSO", "Starting Level", "CraftingStartingLevel"),
            new EntityIdentityName(
                ArtifactExperience, "AttributeSO", "Artifact Experience", "ArtifactExperience"),
            new EntityIdentityName(
                ElementalResonance, "AttributeSO", "Elemental Resonance", "ElementalResonance"),
        }));

    private static EntityIdentityName[] Sorted(EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    private static GameMcpFrameContext Context(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(981));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static GameWorldState World() =>
        new()
        {
            EntityIdentities = Catalog,
            Statistics = PublicationTable<WorldStatistic>.Create(SortedRows(
                Row(
                    Cost,
                    "Statistic",
                    isPercent: false,
                    "The amount of resources a component costs to cast, use or purchase.",
                    "Cost"),
                Row(
                    AdditionalInformation,
                    "Information",
                    isPercent: false,
                    "Some extra details about this component.",
                    "AdditionalInformation"),
                Row(
                    CreateArtifact,
                    "Action",
                    isPercent: false,
                    "Create an artifact from the selected glyphs.",
                    "Tooltip:CreateArtifact"),

                // The one record that references no display type, and one of the eight the game
                // authors no sentence for.
                Row(
                    StartingLevel,
                    string.Empty,
                    isPercent: false,
                    "What level to initialize this craft at.",
                    "CraftingStartingLevel"),
                Row(
                    ArtifactExperience,
                    "Statistic",
                    isPercent: false,
                    string.Empty,
                    "ArtifactExperience"),
                Row(
                    ElementalResonance,
                    "Statistic",
                    isPercent: true,
                    "How much elemental modifications affect this spell.",
                    "ElementalResonance"))),
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 81,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

    /// <summary>
    /// Every category read clean, so a search page carries its hits rather than a list of the
    /// categories this fixture happens not to populate.
    /// </summary>
    private static WorldCollectionCategoryStatus[] CleanReports() =>
        GameMcpWorldQuery.RegisteredCategoryNames().Concat(new[]
            {
                "harvest-actions", "harvest-elements", "harvest-types", "harvest-action-types",
                "plot-node-actions", "concept-instances", "loadouts", "consumable-families",
                "consumable-inventory", "crafting-recipe-state", "crafting-decisions",
                "ordinary-alchemy-loadout", "plot-authoring", "plot-actions", "action-queues",
                "crafting-stations", "harvest-lifecycle", "spell-slots", "targeting",
                "attribute-group-members",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();

    private static WorldStatistic Row(
        Guid id,
        string displayType,
        bool isPercent,
        string description,
        string globalDefinition) =>
        new(id, displayType, isPercent, description, globalDefinition);

    private static WorldStatistic[] SortedRows(params WorldStatistic[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }
}

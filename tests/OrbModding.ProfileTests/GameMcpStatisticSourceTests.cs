using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// "What gives me a third Discovery Option?" answered with world reads alone: the statistic names
/// how many things move it, and one filter walks them.
/// </summary>
/// <remarks>
/// The worked case is the pinned build's own. <c>DiscoveryTreeChoiceCount</c> is moved by exactly
/// three authored effects: Greater Discovery's research effect (+1), Orb Minimalism's completion
/// effect (+1) and Weakened Choices' while-running effect (-1). The game keeps each on its owner
/// only, so until the reverse edge existed the statistic's own page named none of them.
/// </remarks>
[Collection(NativeRegistryCollection.Name)]
[OrbModding.TestSupport.ResetGameStubs]
public sealed class GameMcpStatisticSourceTests : IDisposable
{
    private static readonly Guid DiscoveryOptions = Guid.Parse("6d1962c7-4bb3-4c5f-ac88-776fd8bbd415");
    private static readonly Guid GreaterDiscovery = Guid.Parse("6386ef09-ed57-41b0-9654-b589f94cd3c4");
    private static readonly Guid OrbMinimalism = Guid.Parse("0bfbaec5-def5-4476-a7c7-85fb1d1753fa");
    private static readonly Guid WeakenedChoices = Guid.Parse("898a76c8-170c-4d6a-9e93-0621ac4b46fa");
    private static readonly Guid DiscoveryRerolls = Guid.Parse("32b5e34c-5e6a-4198-9ff6-95627f9ebea5");
    private static readonly Guid Unmoved = Guid.Parse("0c9b8a7d-6e5f-4a3b-9c2d-1e0f9a8b7c6d");

    public GameMcpStatisticSourceTests() => global::IdScriptableObject.RuntimeLookup.Clear();

    public void Dispose() => global::IdScriptableObject.RuntimeLookup.Clear();

    [Fact]
    public void A_statistic_counts_what_moves_it_per_kind()
    {
        var modifiedBy = Assert.IsType<JArray>(Block("int-variables", DiscoveryOptions)["modifiedBy"]);

        Assert.Equal(
            new[] { "achievements=1", "challenges=1", "research=1" },
            modifiedBy.Values<JObject>()
                .Select(kind => (string?)kind!["kind"] + "=" + (int?)kind["count"])
                .ToArray());
    }

    [Fact]
    public void The_modifies_filter_walks_exactly_what_the_count_counted()
    {
        var page = Search(modifies: "Discovery Options");

        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 3/3",
                "[id | name | category | keywords | matchedOn | effect]",
                "0bfbae | Orb Minimalism | achievements | - | - | +1",
                "6386ef | Greater Discovery | research | - | - | +1",
                "898a76 | Weakened Choices | challenges | - | - | -1 while",
            }),
            Render(page));

        var counted = Block("int-variables", DiscoveryOptions)["modifiedBy"]!
            .Values<JObject>().Sum(kind => (int)kind!["count"]!);
        Assert.Equal(counted, page["rows"]!.Count());
    }

    [Fact]
    public void One_owner_moving_two_things_answers_each_walk_with_its_own_effect()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn | effect]",
                "898a76 | Weakened Choices | challenges | - | - | -99 while",
            }),
            Render(Search(modifies: DiscoveryRerolls.ToString("D"))));
    }

    [Fact]
    public void The_filter_narrows_with_the_others_rather_than_beside_them()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 1/1",
                "[id | name | category | keywords | matchedOn | effect]",
                "6386ef | Greater Discovery | research | - | name | +1",
            }),
            Render(Search(query: "Greater", modifies: "6d1962")));
    }

    [Fact]
    public void A_thing_nothing_moves_says_so_and_carries_no_count()
    {
        Assert.Null(Block("int-variables", Unmoved)["modifiedBy"]);

        var nothing = Search(modifies: "Unmoved Count");
        Assert.Equal(
            "unavailable (ERR_REFUSED): Unmoved Count 0c9b8a is moved by no authored effect this " +
            "world publishes, so its page counts no modifiedBy and there is nothing to narrow to",
            Render(nothing));

        Assert.StartsWith(
            "unavailable (ERR_REFUSED): 'Third Discovery Option' is neither a published id nor",
            Render(Search(modifies: "Third Discovery Option")));
    }

    private static JObject Block(string category, Guid uuid) =>
        Assert.Single(
            GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
                GameMcpTestHarness.Context(World()), category, new[] { uuid.ToString("D") }), Catalog)
            ["results"]!.Values<JObject>())!;

    private static string Render(JObject page) => GameMcpTextPage.Render(page).TrimEnd('\n');

    private static JObject Search(string query = "", string modifies = "") =>
        GameMcpTestHarness.Json(GameMcpWorldSearch.Search(
            GameMcpTestHarness.Context(World()),
            query, 0, 50, limitFromCaller: false, modifiesFilter: modifies), Catalog);

    private static GameWorldState World() =>
        new()
        {
            EntityIdentities = Catalog,
            IntVariables = PublicationTable<WorldNumberVariable>.Create(new[]
            {
                new WorldNumberVariable(DiscoveryOptions, new BigDouble(2), isPercent: false),
                new WorldNumberVariable(DiscoveryRerolls, new BigDouble(1), isPercent: false),
                new WorldNumberVariable(Unmoved, new BigDouble(0), isPercent: false),
            }.OrderBy(row => row.VariableId).ToArray()),
            Research = PublicationTable<WorldResearch>.Create(new[] { Research(GreaterDiscovery) }),
            Achievements = PublicationTable<WorldAchievement>.Create(new[]
            {
                new WorldAchievement(OrbMinimalism, 0, true, false, string.Empty, 1, 1),
            }),
            Challenges = PublicationTable<WorldChallenge>.Create(new[]
            {
                new WorldChallenge(WeakenedChoices, 0, 0, true, false, 5, 10, 12, 30),
            }),
            LevelEffects = PublicationTable<WorldLevelEffect>.Create(GameMcpTestHarness.SortedByOwnerAndOrdinal(new[]
            {
                Effect(GreaterDiscovery, 0, DiscoveryOptions, 1d),
                Effect(OrbMinimalism, 0, DiscoveryOptions, 1d),
                Effect(WeakenedChoices, 0, DiscoveryOptions, -1d, "while"),
                Effect(WeakenedChoices, 1, DiscoveryRerolls, -99d, "while"),
            })),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                GameMcpTestHarness.AllCategoriesCollected()),
            CollectedAtEpoch = 61,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

    private static WorldResearch Research(Guid id) =>
        new(
            id,
            level: 0,
            queuedLevels: 0,
            researchStage: 0,
            selfBonusLevels: 0,
            maxLevel: 1,
            researchTime: 60d,
            isDeveloping: false,
            isActive: false,
            flagged: false,
            available: true,
            visible: true,
            complete: false,
            canDevelop: true,
            withinDevelopRange: true,
            meetsLevelRequirements: true,
            stillHasLeeway: true,
            belowArtificialMaxLevel: true,
            belowMaxInvestmentLevel: true,
            purchasedLevels: 0,
            baseLevel: 0,
            bonusLevel: 0,
            totalLevel: 0,
            artificialMaxLevel: 0,
            hiddenLevel: false,
            levelVisibilityRange: 2,
            requiredStagesCached: 0,
            requiredTimeCached: BigDouble.Zero,
            baseRequirementLevel: 0,
            effectiveRequirementLevel: 0,
            requirementAdjustments: PublicationTable<WorldResearchRequirementAdjustment>.Empty,
            modifiers: new RawResearchModifiers(
                BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero));

    private static WorldLevelEffect Effect(
        Guid ownerId,
        int ordinal,
        Guid targetId,
        double amount,
        string when = "") =>
        new(ownerId, ordinal, string.Empty, targetId, (int)GameValueModifierType.Raw,
            new BigDouble(amount), 0, when);

    private static readonly EntityIdentityCatalogSnapshot Catalog =
        EntityIdentityCatalogSnapshot.Bound(1, new[]
        {
            new EntityIdentityName(
                DiscoveryOptions, "IntVariable", "Discovery Options", "DiscoveryTreeChoiceCount"),
            new EntityIdentityName(
                DiscoveryRerolls, "IntVariable", "Max Discovery Rerolls", "DiscoveryTreeRerolls"),
            new EntityIdentityName(Unmoved, "IntVariable", "Unmoved Count", "UnmovedCount"),
            new EntityIdentityName(
                GreaterDiscovery, "ResearchSO", "Greater Discovery", "GreaterDiscovery"),
            new EntityIdentityName(OrbMinimalism, "AchievementSO", "Orb Minimalism", "OrbMinimalism"),
            new EntityIdentityName(
                WeakenedChoices, "ChallengeSO", "Weakened Choices", "WeakenedChoices"),
        }.OrderBy(row => row.EntityId).ToArray());
}

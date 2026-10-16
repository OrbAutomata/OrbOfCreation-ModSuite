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
/// The shipped roster: the six base agromancy verbs of the pinned build, with the ids, screen words
/// and authored records the game ships, on the page a caller pages them from.
/// </summary>
/// <remarks>
/// Every id, display name and internal name here is the pinned build's own
/// (<c>data/game-data.json</c>, <c>objects.HarvestActionSO</c>), and each authored record's base
/// value is 100 — so this is the roster a fresh save answers with, before anything has been bought.
/// The synthetic fixture beside it in <c>GameMcpTypeReachTests</c> proves the edge; this one proves
/// the population.
/// </remarks>
public sealed class GameMcpAgromancyActionRosterTests
{
    private static readonly Guid Create = Guid.Parse("6dc7df29-0e91-4f67-98f2-3dca56e4d4e3");
    private static readonly Guid Expand = Guid.Parse("f3f0d951-a34b-4bbe-99a8-ca7f6ce932f6");
    private static readonly Guid Mining = Guid.Parse("af40da52-4a75-420c-a88d-008c3f5fc443");
    private static readonly Guid Plant = Guid.Parse("8ead5f36-ecfa-4332-b0ae-f1fb51542b0a");
    private static readonly Guid Transmorgify =
        Guid.Parse("8456ad89-4d85-4fee-b490-d46ee3521f36");
    private static readonly Guid Woodcutting =
        Guid.Parse("b7033a1a-349f-4ebd-aae6-ba847cc9a109");

    private static readonly Guid Weave = Guid.Parse("6369e602-51be-4a4f-8d12-e298a081c886");
    private static readonly Guid Harvest = Guid.Parse("eeaa8aa3-d0a8-41be-abc1-eb176c96bee8");
    private static readonly Guid Develop = Guid.Parse("f1b45d8c-6f93-4a9b-a4f1-d989104832ee");

    /// <summary>
    /// Six rows, whole, because six is the whole category — and each says which verb it is and the
    /// three numbers a bonus on its type moves.
    /// </summary>
    [Fact]
    public void The_six_base_verbs_are_one_page()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "rows 6/6",
                "these 6 share: power=100, speed=100, costMod=100",
                "[id | name]",
                "6dc7df | Create",
                "8456ad | Transmorgify",
                "8ead5f | Plant",
                "af40da | Mining",
                "b7033a | Woodcutting",
                "f3f0d9 | Expand",
            }),
            Render(Json(GameMcpWorldQuery.ListRows(
                Context(World()), "agromancy-actions", 0, 50, limitFromCaller: false))));
    }

    /// <summary>
    /// One detail page: the screen's own word for the verb, the type it wears, and every record the
    /// class stores.
    /// </summary>
    [Fact]
    public void One_verb_answers_with_its_screen_words_and_its_type()
    {
        Assert.Equal(
            string.Join('\n', new[]
            {
                "uuid: af40da",
                "name: Mining",
                "internalName: MiningHarvestAction",
                "category: agromancy-actions",
                "keywords: Harvest",
                "row: power=100, speed=100, costMod=100",
            }),
            Render(Detail(Mining)));
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
        EntityIdentityCatalogSnapshot.Bound(1, SortedNames(new[]
        {
            new EntityIdentityName(Create, "HarvestActionSO", "Create", "CreateHarvestAction"),
            new EntityIdentityName(Expand, "HarvestActionSO", "Expand", "ExpandHarvestAction"),
            new EntityIdentityName(Mining, "HarvestActionSO", "Mining", "MiningHarvestAction"),
            new EntityIdentityName(Plant, "HarvestActionSO", "Plant", "PlantHarvestAction"),
            new EntityIdentityName(
                Transmorgify, "HarvestActionSO", "Transmorgify", "TransmorgifyHarvestAction"),
            new EntityIdentityName(
                Woodcutting, "HarvestActionSO", "Woodcutting", "WoodcuttingHarvestAction"),
            new EntityIdentityName(Weave, "HarvestActionTypeSO", "Weave", "WeaveActionType"),
            new EntityIdentityName(Harvest, "HarvestActionTypeSO", "Harvest", "HarvestActionType"),
            new EntityIdentityName(Develop, "HarvestActionTypeSO", "Develop", "DevelopActionType"),
        }));

    /// <summary>
    /// The catalog is binary-searched by id, so real ids have to be written in the order the game's
    /// own registry would hand them over rather than in the order a reader lists the verbs.
    /// </summary>
    private static EntityIdentityName[] SortedNames(EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    private static GameMcpFrameContext Context(GameWorldState world)
    {
        var publisher = new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(907));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static GameWorldState World()
    {
        var keywords = Keywords(
            Keyword(Create, Weave),
            Keyword(Expand, Develop),
            Keyword(Mining, Harvest),
            Keyword(Plant, Weave),
            Keyword(Transmorgify, Weave),
            Keyword(Woodcutting, Harvest));

        return new GameWorldState
        {
            EntityIdentities = Catalog,
            HarvestActions = PublicationTable<WorldHarvestAction>.Create(Sorted(
                Action(Create), Action(Expand), Action(Mining),
                Action(Plant), Action(Transmorgify), Action(Woodcutting))),
            EntityKeywords = keywords,
            CollectionCategories =
                PublicationTable<WorldCollectionCategoryStatus>.Create(CleanReports()),
            CollectedAtEpoch = 63,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
    }

    /// <summary>One verb with the records the pinned build authors: 100 on each of the three.</summary>
    private static WorldHarvestAction Action(Guid id) =>
        new(id, new BigDouble(100), new BigDouble(100), new BigDouble(100));

    private static WorldEntityKeyword Keyword(Guid action, Guid type) => new(
        action, WorldKeywordOwnerKind.HarvestAction, WorldKeywordSource.TypeList, 0, type);

    private static WorldHarvestAction[] Sorted(params WorldHarvestAction[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return rows;
    }

    private static PublicationTable<WorldEntityKeyword> Keywords(params WorldEntityKeyword[] rows)
    {
        Array.Sort(rows, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var source = ((int)left.Source).CompareTo((int)right.Source);
            return source != 0 ? source : left.Ordinal.CompareTo(right.Ordinal);
        });
        return PublicationTable<WorldEntityKeyword>.Create(rows);
    }

    private static WorldCollectionCategoryStatus[] CleanReports() =>
        GameMcpWorldQuery.RegisteredCategoryNames().Concat(new[]
            {
                "plot-node-actions", "concept-instances", "plot-authoring",
                "crafting-recipe-state", "crafting-decisions", "consumable-inventory",
                "loadouts", "harvest-elements", "harvest-actions", "plot-actions",
                "action-queue-slots",

                // The three type rosters whose wire name is not their collector's name.
                "harvest-types", "harvest-action-types", "consumable-families",
                "attribute-group-members",
            })
            .Distinct(StringComparer.Ordinal)
            .Select(name => new WorldCollectionCategoryStatus(
                name, WorldCategoryOutcome.Collected, 0, 0, string.Empty))
            .ToArray();
}

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

public sealed class GameMcpAffordabilityTests
{
    private static readonly Guid Cheap = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Dear = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid Resource = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void The_overview_counts_the_structures_that_can_be_bought_right_now()
    {
        // The overview named how many structures are unlocked, which is not the question a caller
        // asks it, and answering "how many can I buy" meant paging the whole category.
        var overview = GameMcpTestHarness.Json(GameMcpWorldQuery.Overview(World()));

        Assert.Equal(2, (int)overview["economy"]!["unlockedStructures"]!);
        Assert.Equal(1, (int)overview["economy"]!["affordableStructures"]!);
    }

    [Fact]
    public void The_affordable_filter_pages_only_the_rows_whose_price_is_met()
    {
        var page = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(World(), "structures", 0, 50, affordableOnly: true));

        var row = Assert.Single(page["rows"]!.Values<JObject>())!;
        Assert.Equal(Cheap.ToString("D"), (string?)row["uuid"]);
        Assert.True((bool)row["affordable"]!);
        Assert.Equal(1, (int)page["total"]!);
        Assert.Null(page["nextOffset"]);
    }

    [Fact]
    public void An_unfiltered_page_still_answers_for_the_whole_category()
    {
        var page = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(World(), "structures", 0, 50));

        Assert.Equal(2, page["rows"]!.Values<JObject>().Count());
        Assert.Equal(2, (int)page["total"]!);
    }

    [Fact]
    public void A_category_with_no_price_refuses_the_filter_instead_of_ignoring_it()
    {
        var refusal = GameMcpTestHarness.Json(
            GameMcpWorldQuery.ListRows(World(), "resources", 0, 50, affordableOnly: true));

        Assert.Equal("unavailable", (string?)refusal["status"]);
        Assert.Equal("filter_not_supported", (string?)refusal["reasonCode"]);
        Assert.Contains("structures", (string?)refusal["reason"], StringComparison.Ordinal);
    }

    private static GameMcpFrameContext World()
    {
        var world = new GameWorldState
        {
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(1, new[]
            {
                new EntityIdentityName(Cheap, "StructureSO", "Cheap Hut", "cheapHut"),
                new EntityIdentityName(Dear, "StructureSO", "Dear Tower", "dearTower"),
                new EntityIdentityName(Resource, "ResourceSO", "Mana", "mana"),
            }),
            Structures = PublicationTable<WorldStructure>.Create(new[]
            {
                Structure(Cheap),
                Structure(Dear),
            }),
            PurchaseCosts = PublicationTable<WorldPurchaseCost>.Create(new[]
            {
                Cost(Cheap, affordable: true),
                Cost(Dear, affordable: false),
            }),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                GameMcpTestHarness.BandwidthResource(
                    Resource, new BigDouble(50), new BigDouble(100)),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "structures", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "resources", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "purchase-costs", WorldCategoryOutcome.Collected, 0, 0, string.Empty),
            }),
            CollectedAtEpoch = 25,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(world, new WorldGeneration(733));
        return GameMcpTestHarness.Context(publisher.ReadLatest());
    }

    private static WorldPurchaseCost Cost(Guid entityId, bool affordable) => new(
        entityId,
        Resource,
        new BigDouble(10),
        new BigDouble(10),
        1,
        new BigDouble(10),
        PublicationTable<WorldPurchaseCostModifierSource>.Empty,
        affordabilityEvaluated: true,
        availableAmount: new BigDouble(affordable ? 50 : 1),
        combinedEffectiveAmount: new BigDouble(10),
        resourceAffordable: affordable,
        resourceAffordabilityReasonCode: affordable ? "affordable" : "insufficient_resource",
        affordable: affordable,
        affordabilityReasonCode: affordable ? "affordable" : "unaffordable");

    private static WorldStructure Structure(Guid id)
    {
        var modifiers = new RawStructureModifiers(
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero);
        var reading = new RawStructureSample(
            id,
            Guid.Empty,
            BigDouble.One,
            BigDouble.Zero,
            unlocked: true,
            queuedEchos: 0,
            completedEchos: 0,
            selfBonusLevels: 0,
            queueTimeLeft: BigDouble.Zero,
            currentBuildTime: BigDouble.Zero,
            flagged: false,
            baseLevel: 0,
            queueTimeTotal: 0,
            debugStructure: false,
            disabled: false,
            observableId: 0,
            insufficientReqPenaltyActive: false,
            bufferDevelopedQuantity: 0,
            costPerQuantityId: Guid.Empty,
            in modifiers);
        return new WorldStructure(
            in reading,
            BigDouble.One,
            hasWorkInFlight: false,
            BigDouble.One,
            developmentProgress: 0);
    }
}

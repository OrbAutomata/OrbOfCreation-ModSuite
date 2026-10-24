using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpWorldQueryTests
{
    [Fact]
    public void ResourceRowsUseOnlyNamedPlayerFacingSpendableFacts()
    {
        var resourceId = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resourceId,
            new BigDouble(5d, 24),
            new BigDouble(8d, 26),
            visible: true,
            lifetimeQuantity: new BigDouble(1d, 28),
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100d),
            gainRate: new BigDouble(100d),
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: true,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        var resource = new WorldResource(
            in reading,
            isCapped: true,
            headroom: new BigDouble(7.5d, 26),
            fillFraction: 0.00625d,
            isAtCapacity: false,
            trueQuantity: new BigDouble(5.63d, 24),
            trueRate: new BigDouble(1.4d, 21));
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                }),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            GameMcpTestHarness.Context(world, generation: 1003),
            "resources",
            new[] { resourceId.ToString("D") }));
        var block = Assert.Single(response["results"]!.Values<JObject>())!;
        var row = block["row"]!;

        // Identity is said once, on the block, and the row under it carries only its own columns.
        // The row used to repeat the same handle, name and category the block already published.
        // `nativeType` is gone from a block a category names: `resources` is one native class and
        // `world_categories` publishes which, so the block said the same fact twice.
        // No `predicates`: a resource's one gate is its ledger membership, and that is a row fact
        // under the name that says what it is rather than a verdict on the resource itself.
        Assert.Equal(
            new[] { "uuid", "name", "category", "row" },
            block.Children<JProperty>().Select(property => property.Name));
        Assert.Equal(
            new[]
            {
                "meter", "amount", "capacity", "netRatePerSecond", "atCapacity", "inLedger",
            },
            row.Children<JProperty>().Select(property => property.Name));
        Assert.Equal("Knowledge", (string?)block["name"]);
        Assert.Equal("resources", (string?)block["category"]);
        Assert.Equal("held", (string?)row["meter"]);
        Assert.Equal("5.00e24", (string?)row["amount"]);
        Assert.Equal("8.00e26", (string?)row["capacity"]);
        Assert.Equal("1.40e21", (string?)row["netRatePerSecond"]);
        Assert.False((bool)row["atCapacity"]!);
        Assert.Null(row["reading"]);
        Assert.Null(row["quantity"]);
        Assert.Null(row["trueQuantity"]);
        Assert.Null(row["rateInputs"]);
        Assert.Null(row["traits"]);
        Assert.Null(row["modifiers"]);
        // The detail read costs what the detail costs: identity said once, on top of the row a list
        // page would have shown. The retired `predicates` block spent its bytes on a copy of the
        // ledger bit under two names; `inLedger` spends fewer on the bit itself, said once.
        Assert.Equal(214, System.Text.Encoding.UTF8.GetByteCount(
            response.ToString(Newtonsoft.Json.Formatting.None)));

        var list = GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            GameMcpTestHarness.Context(world, generation: 1003),
            "resources",
            0,
            10));
        var listed = Assert.Single(list["rows"]!.Values<JObject>())!;
        Assert.Equal((string?)row["amount"], (string?)listed["amount"]);
        Assert.Equal("5.00e24", (string?)listed["amount"]);
    }

    [Fact]
    public void UncappedResourceNamesThatInsteadOfTheNativeNegativeCapacitySentinel()
    {
        var resourceId = Guid.Parse("67acd892-3260-47b7-aaca-23e49c5903d4");
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            resourceId,
            new BigDouble(5d, 24),
            new BigDouble(-9.48d, 9),
            visible: true,
            lifetimeQuantity: BigDouble.Zero,
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100d),
            gainRate: new BigDouble(100d),
            drain: BigDouble.Zero,
            reservation: BigDouble.Zero,
            usage: BigDouble.Zero,
            inLossMode: false,
            inRestMode: true,
            inRallyMode: false,
            appliedLevels: 0,
            levelVariableId: Guid.Empty,
            in rateInputs,
            in traits,
            in modifiers);
        var resource = new WorldResource(
            in reading,
            isCapped: false,
            headroom: BigDouble.Zero,
            fillFraction: 0d,
            isAtCapacity: false,
            trueQuantity: new BigDouble(9.83d, 24),
            trueRate: BigDouble.Zero);
        var world = new GameWorldState
        {
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                new[]
                {
                    new WorldCollectionCategoryStatus(
                        "resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                }),
            CollectedAtEpoch = 1,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var response = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            GameMcpTestHarness.Context(world, generation: 1004),
            "resources",
            new[] { resourceId.ToString("D") }));
        var row = Assert.Single(response["results"]!.Values<JObject>())!["row"]!;

        Assert.Equal("5.00e24", (string?)row["amount"]);
        Assert.Equal("0", (string?)row["netRatePerSecond"]);

        // The native ceiling here is -9.48e9. Neither that number nor a plain `atCapacity: no`
        // belongs on a resource that cannot fill, and dropping the pair let a page of uncapped
        // resources read as a page whose reader was never told capacities exist.
        Assert.Equal("uncapped", (string?)row["capacity"]);
        Assert.Equal("uncapped", (string?)row["atCapacity"]);
    }

    [Fact]
    public void BoundedLevelsUseJsonCardinalsEvenWhenProjectedFromBigDouble()
    {
        var encoded = GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["rows"] = new GameMcpArrayBuilder(
                    new GameMcpObjectBuilder { ["effectiveLevel"] = 1 },
                    new GameMcpObjectBuilder
                    {
                        ["effectiveLevel"] = new GameMcpDomainValue(new BigDouble(1.57d, 2)),
                    }),
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog);
        var rows = encoded["rows"]!.OfType<JObject>().ToArray();

        Assert.Equal(1, (int)rows[0]["effectiveLevel"]!);
        Assert.Equal(157, (int)rows[1]["effectiveLevel"]!);
    }

    [Fact]
    public void DomainProjectionFieldsNormalizeToOneWireDialect()
    {
        var uuid = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var encoded = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["entityId"] = uuid,
                ["mcpCategory"] = "resources",
                ["quantity"] = new GameMcpDomainValue(new BigDouble(2.5d, 3)),
                ["unlocked"] = true,
                ["outcome"] = "PostconditionFailed",
                ["execution"] = "OneShotQueue",
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog));

        Assert.Equal(GameMcpTestHarness.Handle(uuid), (string?)encoded["uuid"]);
        Assert.Equal("Knowledge", (string?)encoded["name"]);
        Assert.Equal("resources", (string?)encoded["category"]);
        Assert.Equal("2.50e3", (string?)encoded["amount"]);
        Assert.True((bool)encoded["available"]!);
        Assert.Equal("postcondition_failed", (string?)encoded["outcome"]);
        Assert.Equal("one_shot_queue", (string?)encoded["execution"]);
        Assert.Null(encoded["entityId"]);
        Assert.Null(encoded["mcpCategory"]);
        Assert.Null(encoded["quantity"]);
        Assert.Null(encoded["unlocked"]);
    }

    [Fact]
    public void RecursiveWireAuditRejectsBareEntityUuidsAndLegacyIdentifierAliases()
    {
        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var resource = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var weak = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var magnified = Guid.Parse("b0387ddd-2bd8-4799-8cd0-f8c624458930");
        var improvedCasting = Guid.Parse("21628be0-4377-4b13-b28c-171ab29324bf");
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["tree"] = new GameMcpObjectBuilder { ["entityId"] = tree },
            ["cost"] = new GameMcpObjectBuilder { ["resourceUuid"] = resource },
            ["offers"] = new GameMcpArrayBuilder(weak, magnified),
            ["implicated"] = new GameMcpObjectBuilder { ["ownerUuid"] = improvedCasting },
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        var banned = new HashSet<string>(StringComparer.Ordinal)
        {
            "entityId", "resourceUuid", "resourceId", "glyphId", "treeUuid",
            "offerUuid", "selectedUuid",
        };
        var document = Assert.IsType<JObject>(encoded);
        Assert.DoesNotContain(
            document.DescendantsAndSelf().OfType<JProperty>(),
            property => banned.Contains(property.Name));
        var references = document.DescendantsAndSelf()
            .OfType<JObject>()
            .Where(item => item["uuid"] is not null)
            .ToArray();
        Assert.Equal(5, references.Length);
        Assert.All(references, reference =>
        {
            Assert.False(string.IsNullOrWhiteSpace((string?)reference["name"]));
            if (reference["internalName"] is JToken internalName)
            {
                Assert.NotEqual(
                    (string?)reference["name"],
                    (string?)internalName);
            }
        });
    }

    [Fact]
    public void OverviewIsCompactAndExactReadDerivesNativeType()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);
        var overview = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(state));
        Assert.Equal("available", (string?)overview["status"]);
        Assert.NotNull(overview["economy"]);
        Assert.NotNull(overview["progression"]);
        Assert.NotNull(overview["running"]);
        Assert.Null(overview["detailCategories"]);
        Assert.Null(overview["unlocks"]);
        Assert.Null(overview["harvest"]);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(
            overview.ToString(Newtonsoft.Json.Formatting.None)) < 1_650);

        var exact = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D")));
        Assert.Equal("available", (string?)exact["status"]);
        Assert.Null(exact["expectedNativeType"]);
        Assert.Equal(4, (int)exact["row"]!["masteryLevel"]!);
    }

    [Fact]
    public void ListIsLeanAndGetCarriesTheCuratedDecisionDetail()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var list = GameMcpTestHarness.Json(
            GameMcpWorldList.ListRows(state, "spell-recipes", 0, 10));
        var scan = Assert.Single(list["rows"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(GameMcpAcceptanceFixture.SpellId), (string?)scan["uuid"]);
        Assert.Null(scan["nameEvidence"]);
        Assert.Equal(4, (int)scan["masteryLevel"]!);
        Assert.Null(scan["spellPowerMod"]);
        Assert.Null(scan["category"]);
        Assert.Null(scan["loadoutAdd"]);
        Assert.Equal(99, System.Text.Encoding.UTF8.GetByteCount(
            list.ToString(Newtonsoft.Json.Formatting.None)));

        var searchState = GameMcpAcceptanceFixture.Snapshot(
            GameMcpAcceptanceFixture.SpellWorld(4, 30) with
            {
                CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(
                    GameMcpTestHarness.AllCategoriesCollected()),
            });
        var search = GameMcpTestHarness.Json(GameMcpWorldSearch.Search(
            searchState,
            GameMcpAcceptanceFixture.SpellId.ToString("D"),
            0,
            5));
        // A match names the entity, the category that reads the rest of it, and the words the game
        // prints on it. Borrowing each category's scan columns unioned every category's headings
        // onto one page and left nine cells in ten empty, on the tool whose whole job is routing
        // the caller to the right read.
        var match = Assert.Single(search["rows"]!.Values<JObject>())!;
        Assert.Equal((string?)scan["uuid"], (string?)match["uuid"]);
        Assert.Equal((string?)scan["name"], (string?)match["name"]);
        Assert.Equal("spell-recipes", (string?)match["category"]);
        Assert.Equal("id", (string?)match["matchedOn"]);
        Assert.Equal(5, match.Properties().Count());

        var exact = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            state,
            "spell-recipes",
            GameMcpAcceptanceFixture.SpellId.ToString("D")));
        Assert.Null(exact["row"]!["spellPowerMod"]);
        Assert.Equal("spell-recipes", (string?)exact["category"]);
        Assert.NotNull(exact["row"]!["loadoutAdd"]);
    }
}

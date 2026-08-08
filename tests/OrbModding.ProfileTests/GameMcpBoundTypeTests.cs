using System;
using System.Linq;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpTestHarness;

namespace OrbModding.ProfileTests;

/// <summary>
/// One quantity, one JSON type. A key that can carry a <c>BigDouble</c> ships the game's
/// Scientific string everywhere; a key that can only carry a bounded cardinal ships a JSON number
/// everywhere; and a ceiling ships in the type of the value it caps, so a range never states its
/// two ends two ways.
/// </summary>
public sealed class GameMcpBoundTypeTests
{
    /// <summary>
    /// The refusal a player actually saw: <c>minimumAmount: 1</c> beside
    /// <c>maximumAmount: "240"</c>, one range in two types, because the ceiling was listed as a
    /// player magnitude while the floor was not.
    /// </summary>
    [Fact]
    public void A_range_states_both_of_its_ends_in_one_type()
    {
        var refusal = Json(new GameMcpObjectBuilder
        {
            ["status"] = "refused",
            ["reasonCode"] = "level_out_of_range",
            ["minimumAmount"] = 1,
            ["maximumAmount"] = 240,
        });

        Assert.Equal(JTokenType.Integer, refusal["minimumAmount"]!.Type);
        Assert.Equal(JTokenType.Integer, refusal["maximumAmount"]!.Type);
    }

    /// <summary>
    /// Every bound on an argument a caller sends back is a bounded cardinal, whichever tool
    /// produced it — the schema field it feeds accepts an integer and nothing else.
    /// </summary>
    [Theory]
    [InlineData("minimumAmount")]
    [InlineData("maximumAmount")]
    [InlineData("minimumSlot")]
    [InlineData("maximumSlot")]
    [InlineData("maximumDestination")]
    [InlineData("maximumAdditional")]
    [InlineData("maximumBatch")]
    [InlineData("minimum")]
    [InlineData("maximum")]
    public void An_argument_bound_is_a_json_number(string field)
    {
        var response = Json(new GameMcpObjectBuilder { [field] = 8 });

        Assert.Equal(JTokenType.Integer, response[field]!.Type);
    }

    /// <summary>
    /// The counterpart rule: a quantity of stuff keeps the game's own display shape even when one
    /// category happens to hold it in an <c>int</c>, so a caller never has to know which category
    /// it is reading to know what <c>amount</c> looks like — and a stock's ceiling matches it.
    /// </summary>
    [Fact]
    public void A_player_magnitude_and_its_ceiling_stay_in_the_games_own_shape()
    {
        var row = Json(new GameMcpObjectBuilder
        {
            ["amount"] = 16,
            ["maximumCarry"] = 16,
        });

        Assert.Equal(JTokenType.String, row["amount"]!.Type);
        Assert.Equal(JTokenType.String, row["maximumCarry"]!.Type);
        Assert.Equal("16", (string?)row["amount"]);
    }

    /// <summary>
    /// A magnitude big enough to need the exponent still gets it. The rule is about which
    /// vocabulary a key speaks, never about shortening the number.
    /// </summary>
    [Fact]
    public void A_magnitude_past_the_plain_range_still_ships_scientific()
    {
        var row = Json(new GameMcpObjectBuilder { ["cost"] = 1400000d });

        Assert.Equal("1.4e6", (string?)row["cost"]);
    }

    /// <summary>
    /// The rule read off a real producer instead of off an object this test built. The equipment
    /// weight budget is the block that broke it: its ceiling caps a carried weight, so it takes the
    /// magnitude's own name and shape, and nothing the read produces spells <c>maximum</c> or
    /// <c>minimum</c> as a string.
    /// </summary>
    [Fact]
    public void The_equipment_weight_budget_names_its_ceiling_after_the_magnitude_it_caps()
    {
        var world = WeightBudgetWorld();
        var response = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            GameMcpWorldQuery.GetRow(
                Context(world, generation: 2601), "equipment", Equipment.ToString("D")).Freeze(),
            world.EntityIdentities));

        var budget = response["row"]!["weightBudget"]!;
        Assert.Equal("60", (string?)budget["used"]);
        Assert.Equal("100", (string?)budget["maximumCarry"]);
        Assert.Null(budget["maximum"]);
        Assert.DoesNotContain(
            response.Descendants().OfType<JProperty>(),
            property => property.Name is "maximum" or "minimum" &&
                property.Value.Type == JTokenType.String);
    }

    /// <summary>
    /// The other producer of a bare <c>maximum</c>: a writable setting's declared domain. An
    /// integer setting states both ends as integers, so a caller does not have to know which
    /// setting it asked about to know what the ceiling looks like.
    /// </summary>
    [Fact]
    public void An_integer_settings_declared_domain_states_both_ends_as_integers()
    {
        var entry = new ConfigFile().Bind(
            "AutoConcept", "TrainingPeriodSeconds", 30,
            new ConfigDescription("period", new AcceptableValueRange<int>(10, 3600)));

        Assert.False(GameMcpConfigurationValuePolicy.TryValidate(
            entry, "9", out var reason, out var bound));
        var refusal = Json(GameMcpConfigurationValuePolicy.RefusalFacts(
            Command("AutoConcept", "TrainingPeriodSeconds", "9"), in bound));

        var setting = refusal["setting"]!;
        Assert.Equal(JTokenType.Integer, setting["minimum"]!.Type);
        Assert.Equal(JTokenType.Integer, setting["maximum"]!.Type);
        Assert.Equal(10, (int)setting["minimum"]!);
        Assert.Equal(3600, (int)setting["maximum"]!);
        Assert.Contains("from 10 to 3600", reason, StringComparison.Ordinal);
    }

    private static GameMcpCommand Command(string section, string key, string value) =>
        new(1, GameMcpCommandKind.ConfigurationSet, 9, 3, section, Guid.Empty, Guid.Empty,
            string.Empty, 1, key, value, false, false);

    private static readonly Guid Equipment = Guid.Parse("f6000000-0000-0000-0000-000000000001");
    private static readonly Guid EquipmentType = Guid.Parse("f6000000-0000-0000-0000-000000000002");
    private static readonly Guid Bandwidth = Guid.Parse("f6000000-0000-0000-0000-000000000003");

    private static GameWorldState WeightBudgetWorld()
    {
        var decision = new WorldEquipmentDecision(true, string.Empty, EquipmentType, 1, 4, 1, 3,
            1, 2, 2, 1, true,
            PublicationTable<WorldEquipmentUsageCost>.Create(new[]
            {
                new WorldEquipmentUsageCost(Bandwidth, new BigDouble(20)),
            }));
        var equipment = new WorldEquipment(Equipment, true, 0, BigDouble.Zero, 3, false,
            BigDouble.One, BigDouble.One, BigDouble.One, 1, 0, -1, BigDouble.Zero,
            loadout: decision);
        var identities = EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(Equipment, "EquipmentSO", "Prismatic Lens", "prismaticLens"),
            new EntityIdentityName(EquipmentType, "EquipmentTypeSO", "Focus", "focus"),
            new EntityIdentityName(Bandwidth, "ResourceSO", "Attunement", "attunement"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 26,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(26, identities),
            Resources = PublicationTable<WorldResource>.Create(new[]
            {
                BandwidthResource(Bandwidth, new BigDouble(60), new BigDouble(100)),
            }),
            Equipment = PublicationTable<WorldEquipment>.Create(new[] { equipment }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus("equipment", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus("resources", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
    }
}

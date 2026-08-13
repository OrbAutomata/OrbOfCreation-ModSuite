using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpGenericLevelTests
{
    private static readonly Guid EquipmentTypeId = Guid.Parse("fb000000-0000-0000-0000-000000000001");
    private static readonly Guid GlyphId = Guid.Parse("fb000000-0000-0000-0000-000000000002");
    private static readonly Guid ResourceTypeId = Guid.Parse("fb000000-0000-0000-0000-000000000003");
    private static readonly Guid TimeRuneId = Guid.Parse("fb000000-0000-0000-0000-000000000004");
    private static readonly Guid CostResourceId = Guid.Parse("fb000000-0000-0000-0000-000000000005");

    [Fact]
    public void Tool_exposes_one_subject_and_the_two_real_level_list_controls()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_level");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        Assert.Equal(new[] { "mode", "uuid", "amount" },
            tool["inputSchema"]!["required"]!.Values<string>());
        Assert.Equal(new[] { "purchase", "bonus" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.Null(tool["inputSchema"]!["properties"]!["expectedNativeType"]);
        var operation = GameMcpProtocolRouter.BuildOperation("game_level", new JObject
        {
            ["mode"] = "purchase",
            ["uuid"] = GlyphId.ToString("D"),
            ["amount"] = 2,
        });
        Assert.Equal(GameMcpOperationClass.Gameplay, operation.Classification);
    }

    [Fact]
    public void Every_owned_level_category_publishes_the_native_next_decision()
    {
        var world = World(total: 5, bonus: 2, purchaseAffordable: false);

        var equipment = Row(world, "equipment-types", EquipmentTypeId);
        var glyph = Row(world, "glyphs", GlyphId);
        var resourceType = Row(world, "resource-types", ResourceTypeId);
        var timeRune = Row(world, "time-runes", TimeRuneId);

        AssertDecision(equipment, supportsBonus: true);
        AssertDecision(glyph, supportsBonus: true);
        AssertDecision(resourceType, supportsBonus: true);
        AssertDecision(timeRune, supportsBonus: false);
        var cost = Assert.Single(glyph["purchase"]!["costs"]!.Values<JObject>());
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("5", (string?)cost["cost"]);
        Assert.Equal("80", (string?)cost["spendableAmount"]);

        // One price shape: what it asks, what is held, whether that covers it, and the resource. A
        // level price used to say three of the four and leave affordability to a sibling key on the
        // decision, so a glyph's price and a research's price read as two different tables in one
        // session. The sibling key stays because it is a different fact — the game's own answer for
        // the whole purchase — while the column answers per resource and so names the one that is
        // short. Here they disagree, which is exactly why neither can stand in for the other.
        Assert.Equal(
            new[] { "cost", "spendableAmount", "affordable", "resource" },
            cost.Properties().Select(property => property.Name).ToArray());
        Assert.True((bool)cost["affordable"]!);
        Assert.False((bool)glyph["purchase"]!["affordable"]!);
    }

    [Fact]
    public void Settled_delta_is_observed_from_the_new_world_for_each_level_kind()
    {
        var before = World(total: 5, bonus: 2, purchaseAffordable: true);
        var afterPaid = World(
            total: 6, bonus: 2, purchaseAffordable: true, maximumUsages: 4);
        var afterBonus = World(total: 6, bonus: 3, purchaseAffordable: true, maximumUsages: 4);
        var purchase = Command("purchase", before);
        var bonus = Command("bonus", before);

        var paidDelta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(afterPaid, generation: 902), purchase,
            GameMcpCommandResult.Committed("committed", 9, 3)), afterPaid);
        var bonusDelta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(afterBonus, generation: 903), bonus,
            GameMcpCommandResult.Committed("committed", 9, 3)), afterBonus);

        Assert.Equal(3, (int)paidDelta["paidLevel"]!["before"]!);
        Assert.Equal(4, (int)paidDelta["paidLevel"]!["after"]!);
        Assert.Equal(2, (int)bonusDelta["bonusLevel"]!["before"]!);
        Assert.Equal(3, (int)bonusDelta["bonusLevel"]!["after"]!);
        Assert.Equal(6, (int)bonusDelta["totalLevel"]!["after"]!);

        // The glyph screen counts uses, so the count it draws travels with the level that bought it.
        Assert.Equal(3, (int)paidDelta["usableCount"]!["before"]!);
        Assert.Equal(4, (int)paidDelta["usableCount"]!["after"]!);
        Assert.Equal(4, (int)bonusDelta["usableCount"]!["after"]!);

        // Payment reporting left the wire on both level-buying verbs. A level that asks for nothing
        // still says so, because that changes what a caller does next; what a level cost and what
        // the next one asks belong to the world publication, which keeps both curves in full.
        Assert.Null(paidDelta["paid"]);
        Assert.Null(paidDelta["costPerLevel"]);
        Assert.Null(paidDelta["free"]);
    }

    [Fact]
    public void Listed_glyph_levels_match_the_row_for_a_bonus_levelled_glyph()
    {
        var world = World(total: 5, bonus: 2, purchaseAffordable: false);
        var listed = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 901), "glyphs", 0, 10).Freeze(), world);
        var row = Row(world, "glyphs", GlyphId);

        var entry = Assert.Single(listed["rows"]!.Values<JObject>())!;
        Assert.Equal(3, (int)entry["paidLevel"]!);
        Assert.Equal(2, (int)entry["bonusLevel"]!);
        Assert.Equal(5, (int)entry["totalLevel"]!);
        Assert.Equal((int)row["paidLevel"]!, (int)entry["paidLevel"]!);
        Assert.Equal((int)row["bonusLevel"]!, (int)entry["bonusLevel"]!);
        Assert.Equal((int)row["totalLevel"]!, (int)entry["totalLevel"]!);
    }

    /// <summary>
    /// A glyph with no authored <c>levelingCost</c> is levelled by the game for nothing:
    /// <c>ResourceCostList.HasEnough()</c> is true over an empty list, and a levelable's cost
    /// button never performs a cost. The wire says so instead of dropping the array.
    /// </summary>
    [Fact]
    public void A_glyph_the_game_levels_for_nothing_publishes_an_empty_cost_list_and_says_so()
    {
        var glyph = Row(
            World(5, 2, purchaseAffordable: true, levelsAreFree: true), "glyphs", GlyphId);

        Assert.True((bool)glyph["purchase"]!["available"]!);
        Assert.Empty(glyph["purchase"]!["costs"]!.Values<JObject>());
        Assert.True((bool)glyph["purchase"]!["free"]!);
    }

    [Fact]
    public void Hidden_or_unlearned_rows_never_advertise_level_purchase()
    {
        var world = World(5, 2, purchaseAffordable: true,
            glyphLearned: false, resourceTypeHidden: true);

        var glyph = Row(world, "glyphs", GlyphId);
        var resourceType = Row(world, "resource-types", ResourceTypeId);

        Assert.Equal("locked", (string?)glyph["state"]);
        Assert.False((bool)glyph["purchase"]!["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)glyph["purchase"]!["reasonCode"]);
        Assert.False((bool)resourceType["purchase"]!["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)resourceType["purchase"]!["reasonCode"]);
    }

    /// <summary>
    /// A glyph the picker will not offer says which of the two reasons it is, and the two are the
    /// two populations: an augment waits on a discovery, an unlocker on the authored condition its
    /// container holds. This world publishes no condition for the unlocker, which is the one case
    /// the honest-unknown sentence is written for. Unnamed, both answered that sentence, on every
    /// unlearned glyph in the game.
    /// </summary>
    [Theory]
    [InlineData(false, "ERR_LOCKED",
        "The game keeps this locked, and says nothing about what would unlock it.")]
    [InlineData(true, "ERR_LOCKED", "This has not been discovered yet.")]
    public void An_unlearned_glyph_names_the_gate_the_game_actually_published(
        bool discoverable,
        string expectedClass,
        string expectedReason)
    {
        var glyph = Row(
            World(5, 2, purchaseAffordable: true, glyphLearned: false,
                glyphDiscoverable: discoverable),
            "glyphs",
            GlyphId);

        Assert.Equal("locked", (string?)glyph["state"]);
        Assert.Equal(expectedClass, (string?)glyph["reasonCode"]);
        Assert.Equal(expectedReason, (string?)glyph["reason"]);
    }

    [Fact]
    public void PrerequisiteLearnedGlyphIsAvailableWithoutClaimingDiscovery()
    {
        var glyph = Row(
            World(5, 2, purchaseAffordable: true, glyphDiscoverable: false),
            "glyphs",
            GlyphId);

        // The pair the two columns exist for: the picker offers this glyph, and it was never
        // discovered — `state` answers the first and `discovered` still answers the second, which
        // is why the glyph list kept both.
        Assert.Equal("available", (string?)glyph["state"]);
        Assert.False((bool)glyph["discovered"]!);

        // An absent block read as "not discovered yet", the opposite of the truth here: this glyph
        // is owned already and the game never routes it through discovery at all.
        Assert.False((bool)glyph["discover"]!["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)glyph["discover"]!["reasonCode"]);
        Assert.Null(glyph["discover"]!["costs"]);
    }

    /// <summary>
    /// The hidden gate the detail row publishes belongs on the list too: a hidden resource type
    /// refuses every level purchase, and a list without it is a list a caller probes row by row.
    /// </summary>
    [Fact]
    public void A_resource_type_list_row_carries_the_hidden_gate_its_detail_does()
    {
        var world = World(5, 2, purchaseAffordable: true, resourceTypeHidden: true);
        var listed = Json(GameMcpWorldQuery.ListRows(
            GameMcpTestHarness.Context(world, generation: 901),
            "resource-types", 0, 10).Freeze(), world);
        var detail = Row(world, "resource-types", ResourceTypeId);

        var entry = Assert.Single(listed["rows"]!.Values<JObject>())!;
        Assert.True((bool)entry["hidden"]!);
        Assert.Equal((bool)detail["hidden"]!, (bool)entry["hidden"]!);
        Assert.Equal((int)detail["totalLevel"]!, (int)entry["level"]!);
    }

    private static JObject Row(
        GameWorldState world,
        string category,
        Guid id) =>
        Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 901),
            category, id.ToString("D")).Freeze(), world)["row"] as JObject ??
        throw new InvalidOperationException("row was unavailable");

    private static void AssertDecision(JObject row, bool supportsBonus)
    {
        Assert.Equal(supportsBonus ? 3 : 5, (int)row["paidLevel"]!);
        Assert.Equal(5, (int)row["totalLevel"]!);
        Assert.False((bool)row["purchase"]!["available"]!);
        Assert.False((bool)row["purchase"]!["affordable"]!);
        Assert.Equal("ERR_UNAFFORDABLE", (string?)row["purchase"]!["reasonCode"]);
        if (supportsBonus)
        {
            Assert.Equal(2, (int)row["bonusLevel"]!);
            Assert.True((bool)row["bonus"]!["available"]!);
        }
        else
        {
            Assert.Null(row["bonusLevel"]);
            Assert.Null(row["bonus"]);
        }
    }

    /// <summary>
    /// The game's headroom can be below the ask. One level bought against an ask of two settled
    /// into an answer indistinguishable from a satisfied <c>amount=1</c>, so a caller batching its
    /// own progression accumulated drift with no signal at all.
    /// </summary>
    [Fact]
    public void A_purchase_that_delivered_fewer_levels_than_asked_says_both_numbers()
    {
        var before = World(total: 5, bonus: 2, purchaseAffordable: true);
        var after = World(total: 6, bonus: 2, purchaseAffordable: true, maximumUsages: 4);
        var asked = Command("purchase", before, amount: 2);

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 904), asked,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Equal(3, (int)delta["paidLevel"]!["before"]!);
        Assert.Equal(4, (int)delta["paidLevel"]!["after"]!);
        Assert.Equal(2, (int)delta["requestedAmount"]!);
        Assert.Equal(1, (int)delta["deliveredAmount"]!);
    }

    /// <summary>
    /// A satisfied ask says nothing about itself: what was requested is only worth saying when it
    /// differs from what arrived.
    /// </summary>
    [Fact]
    public void A_fully_delivered_purchase_does_not_restate_what_was_asked()
    {
        var before = World(total: 5, bonus: 2, purchaseAffordable: true);
        var after = World(total: 6, bonus: 2, purchaseAffordable: true, maximumUsages: 4);
        var asked = Command("purchase", before);

        var delta = Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 905), asked,
            GameMcpCommandResult.Committed("committed", 9, 3)), after);

        Assert.Null(delta["requestedAmount"]);
        Assert.Null(delta["deliveredAmount"]);
    }

    private static GameMcpCommand Command(
        string mode,
        GameWorldState before,
        int amount = 1) =>
        new(1, GameMcpCommandKind.GenericLevel,
            9, 3, mode, GlyphId, Guid.Empty, "GlyphSO",
            amount, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 901));

    private static GameWorldState World(
        int total,
        int bonus,
        bool purchaseAffordable,
        bool glyphLearned = true,
        bool glyphDiscoverable = true,
        bool glyphDiscoveryRequired = false,
        bool resourceTypeHidden = false,
        bool levelsAreFree = false,
        int maximumUsages = 3)
    {
        var paidCosts = levelsAreFree
            ? PublicationTable<WorldLevelableCost>.Empty
            : PublicationTable<WorldLevelableCost>.Create(new[]
                { new WorldLevelableCost(CostResourceId, new BigDouble(5)) });
        var bonusCosts = PublicationTable<WorldLevelableCost>.Create(new[]
            { new WorldLevelableCost(CostResourceId, new BigDouble(2)) });
        var withBonus = new WorldLevelableDecision(total, bonus, true,
            purchaseAffordable, paidCosts, true, true, true, bonusCosts);
        var withoutBonus = new WorldLevelableDecision(total, 0, true,
            purchaseAffordable, paidCosts, false, false, false,
            PublicationTable<WorldLevelableCost>.Empty);
        var equipmentType = new WorldEquipmentType(
            EquipmentTypeId, total - bonus, bonus, 1, new BigDouble(4),
            new BigDouble(8), 0, 0, withBonus);
        var glyph = new WorldGlyph(GlyphId, total - bonus, bonus, 0, glyphLearned,
            glyphDiscoverable, glyphDiscoveryRequired, false, false, false, 0, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, maximumUsages, levelDecision: withBonus);
        var resourceType = new WorldResourceType(
            resourceTypeId: ResourceTypeId,
            level: total - bonus,
            freeLevels: bonus,
            specialHidden: resourceTypeHidden,
            ignoreAudit: false,
            ignoreEffects: false,
            auditHasMaxQuantity: false,
            levelDecision: withBonus);
        var timeRune = new WorldTimeRune(TimeRuneId, true, total, 0,
            BigDouble.Zero, 1, false, true, BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, BigDouble.Zero, levelDecision: withoutBonus);
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(CostResourceId, new BigDouble(80),
            new BigDouble(100), true, BigDouble.Zero, BigDouble.Zero,
            new BigDouble(100), new BigDouble(100), BigDouble.Zero, BigDouble.Zero,
            BigDouble.Zero, false, false, false, 0, Guid.Empty,
            in rateInputs, in traits, in modifiers);
        var resource = new WorldResource(in reading, true, new BigDouble(20), 0.8,
            false, new BigDouble(80), BigDouble.Zero);
        var identities = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(EquipmentTypeId, "EquipmentTypeSO", "Artifacts", "artifacts"),
            new EntityIdentityName(GlyphId, "GlyphSO", "Echo Glyph", "echo_glyph"),
            new EntityIdentityName(ResourceTypeId, "ResourceTypeSO", "Alchemy Materials", "alchemy_materials"),
            new EntityIdentityName(TimeRuneId, "TimeRuneSO", "Quickening", "quickening"),
            new EntityIdentityName(CostResourceId, "ResourceSO", "Knowledge", "knowledge"),
        }).OrderBy(row => row.EntityId).ToArray();
        return new GameWorldState
        {
            CollectedAtEpoch = 9,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = EntityIdentityCatalogSnapshot.Bound(9, identities),
            EquipmentTypes = PublicationTable<WorldEquipmentType>.Create(new[] { equipmentType }),
            Glyphs = PublicationTable<WorldGlyph>.Create(new[] { glyph }),
            ResourceTypes = PublicationTable<WorldResourceType>.Create(new[] { resourceType }),
            TimeRunes = PublicationTable<WorldTimeRune>.Create(new[] { timeRune }),
            Resources = PublicationTable<WorldResource>.Create(new[] { resource }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                Collected("equipment types"), Collected("glyphs"),
                Collected("resource types"), Collected("time runes"), Collected("resources"),
            }),
        };
    }

    private static WorldCollectionCategoryStatus Collected(string category) =>
        new(category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);

    private static JObject Json(GameMcpValue value, GameWorldState world) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, world.EntityIdentities));
}

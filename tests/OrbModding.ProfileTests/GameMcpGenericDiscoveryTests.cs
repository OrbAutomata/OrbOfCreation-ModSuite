using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpGenericDiscoveryTests
{
    private static readonly Guid GlyphId =
        Guid.Parse("f3000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId =
        Guid.Parse("f3000000-0000-0000-0000-000000000002");
    private static readonly Guid ComponentId =
        Guid.Parse("f3000000-0000-0000-0000-000000000003");
    private static readonly Guid AmbiguousOutputId =
        Guid.Parse("f3000000-0000-0000-0000-000000000004");

    [Fact]
    public void ToolAdvertisesOneComponentFirstAndEventOfferDiscoveryNamespace()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode" }, schema["required"]!.Values<string>());
        Assert.Equal(
            new[] { "preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["surface"]);
        Assert.NotNull(schema["properties"]!["components"]);
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.NotNull(schema["properties"]!["offerUuid"]);
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["amount"]);
    }

    [Fact]
    public void ValidationNamesMissingCompositionFieldsAndPreviewIsReadOnly()
    {
        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        var missing = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject(),
            }));
        var accepted = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject
                {
                    ["mode"] = "preview",
                    ["surface"] = "spellcraft",
                    ["components"] = new JArray(new JObject
                    {
                        ["uuid"] = GlyphId.ToString("D"),
                        ["count"] = 1,
                    }),
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'mode' is missing", GameMcpTestHarness.Page(missing));
        Assert.DoesNotContain(
            "refused (ERR_INPUT)", GameMcpTestHarness.Page(accepted), StringComparison.Ordinal);
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_discover",
            new JObject
            {
                ["mode"] = "preview",
                ["surface"] = "spellcraft",
                ["components"] = new JArray(new JObject
                {
                    ["uuid"] = GlyphId.ToString("D"),
                    ["count"] = 1,
                }),
            });
        Assert.Equal(GameMcpOperationClass.ReadOnly, operation.Classification);
        Assert.Equal("spellcraft", operation.Key);
        Assert.Single(operation.UuidCounts);
    }

    [Theory]
    [InlineData("spellcraft", 16)]
    [InlineData("glyphcraft", 22)]
    [InlineData("devote", 22)]
    [InlineData("runecraft", 22)]
    [InlineData("alchemy", 22)]
    [InlineData("artifacts", 22)]
    [InlineData("concepts", 22)]
    public void Confirm_routes_only_spellcraft_to_the_spell_resolver(
        string surface,
        int expected)
    {
        Assert.Equal(
            expected,
            (int)GameMcpCommandKinds.FromRequest("game_discover", "confirm", surface));
    }

    [Fact]
    public void Predecision_and_poststate_are_named_and_carry_the_exact_next_cost()
    {
        var row = Json(GameMcpWorldQuery.GetRow(
            Context(), "glyphs", GlyphId.ToString("D")));
        var postState = Json(GameMcpWorldQuery.ProjectPostState(
            Context(), "glyphs", GlyphId));

        Assert.Equal("available", (string?)row["status"]);
        Assert.Null(row["worldGeneration"]);
        var glyph = row["row"]!;
        Assert.Equal(GameMcpTestHarness.Handle(GlyphId), (string?)glyph["uuid"]);
        Assert.Equal("Amplify", (string?)glyph["name"]);
        Assert.True((bool)glyph["discover"]!["available"]!);
        Assert.True((bool)glyph["discover"]!["required"]!);
        var cost = Assert.Single(glyph["discover"]!["costs"]!).Value<JObject>()!;
        Assert.Equal(GameMcpTestHarness.Handle(ResourceId), (string?)cost["resource"]!["uuid"]);
        Assert.Equal("Arcane Dust", (string?)cost["resource"]!["name"]);
        Assert.Equal("5", (string?)cost["cost"]);
        Assert.Equal("8", (string?)cost["spendableAmount"]);
        Assert.True((bool)cost["affordable"]!);

        Assert.Equal("Amplify", (string?)postState["name"]);
        Assert.NotNull(postState["discover"]!["costs"]);
        Assert.Null(postState["receipt"]);
        Assert.Null(postState["payment"]);
        Assert.Null(postState["worldGeneration"]);
    }

    [Fact]
    public void Generic_preview_resolves_the_UI_surface_recipe_without_echoing_the_request()
    {
        var preview = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(
            Context(),
            "glyphcraft",
            new[]
            {
                new GameMcpUuidCount(ComponentId, 1),
                new GameMcpUuidCount(ResourceId, 1),
            }));

        Assert.Equal("available", (string?)preview["status"]);
        Assert.Equal("glyphcraft", (string?)preview["surface"]);
        Assert.Null(preview["components"]);
        var output = preview["output"]!;
        Assert.Equal(GameMcpTestHarness.Handle(GlyphId), (string?)output["uuid"]);
        Assert.Equal("Amplify", (string?)output["name"]);
        Assert.True((bool)output["discover"]!["available"]!);
        Assert.NotNull(output["discover"]!["costs"]);
    }

    /// <summary>
    /// Not holding a glyph and asking too much of one you hold are different answers with different
    /// next moves. Folded into one clause, a glyph the player has never seen refused by quoting a
    /// usage ceiling of nought — which reads as a clamp on something they own.
    /// </summary>
    [Fact]
    public void A_component_glyph_the_player_does_not_hold_says_so_rather_than_quoting_a_ceiling()
    {
        var preview = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(
            Context(componentLearned: false),
            "glyphcraft",
            new[]
            {
                new GameMcpUuidCount(ComponentId, 1),
                new GameMcpUuidCount(ResourceId, 1),
            }));

        Assert.Equal("unavailable", (string?)preview["status"]);
        Assert.Equal("ERR_LOCKED", (string?)preview["reasonCode"]);
        Assert.Equal(
            "Glyph Focus has not been discovered yet.",
            (string?)preview["reason"]);
    }

    [Fact]
    public void Generic_preview_refuses_ambiguous_authored_recipes_instead_of_guessing()
    {
        var preview = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(
            Context(ambiguous: true),
            "glyphcraft",
            new[]
            {
                new GameMcpUuidCount(ComponentId, 1),
                new GameMcpUuidCount(ResourceId, 1),
            }));

        Assert.Equal("unavailable", (string?)preview["status"]);
        Assert.Equal("ERR_INPUT", (string?)preview["reasonCode"]);
        Assert.Contains("2 published glyphs", (string?)preview["reason"]);
        Assert.Null(preview["output"]);
    }

    [Fact]
    public void Success_yields_to_poststate_while_failure_names_the_missing_outcome()
    {
        var failure = new GenericDiscoverySubmission(
            GenericDiscoveryPreflight.VerificationFailed,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(2, 1, 0),
            "target remained undiscovered");
        var success = new GenericDiscoverySubmission(
            GenericDiscoveryPreflight.Proceeded,
            GenericDiscoveryNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 1, 1),
            "target discovered");

        var failed = Json(GameMcpGenericDiscoveryProjection.Project(in failure));
        var committed = Json(GameMcpGenericDiscoveryProjection.Project(in success));

        Assert.Equal("requested entity discovered", (string?)failed["missingOutcome"]);
        Assert.Single(failed.Properties());
        Assert.Empty(committed.Properties());
    }

    private static GameMcpFrameContext Context(
        bool ambiguous = false,
        bool componentLearned = true)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(World(ambiguous, componentLearned), new WorldGeneration(2301));
        return GameMcpTestHarness.Context(
            publisher.ReadLatest(), configurationGeneration: 8, lifecycleGeneration: 15);
    }

    private static GameWorldState World(
        bool ambiguous = false,
        bool componentLearned = true)
    {
        var costs = PublicationTable<WorldDiscoverableCost>.Create(new[]
        {
            new WorldDiscoverableCost(ResourceId, new BigDouble(5), new BigDouble(8)),
        });
        var decision = new WorldDiscoverableDecision(
            visible: true,
            canDiscover: true,
            discovered: false,
            required: true,
            affordable: true,
            costs,
            PublicationTable<Guid>.Create(new[] { ComponentId }),
            PublicationTable<Guid>.Create(new[] { ResourceId }));
        var glyph = Glyph(GlyphId, decision, maximumUsages: 1);
        var component = Glyph(
            ComponentId, default, maximumUsages: 2, learned: componentLearned);
        var glyphs = ambiguous
            ? new[] { glyph, component, Glyph(AmbiguousOutputId, decision, maximumUsages: 1) }
            : new[] { glyph, component };
        return new GameWorldState
        {
            CollectedAtEpoch = 15,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = Identities(),
            Glyphs = PublicationTable<WorldGlyph>.Create(glyphs),
            Resources = PublicationTable<WorldResource>.Create(new[] { Resource() }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "glyphs", WorldCategoryOutcome.Collected, glyphs.Length, 0, string.Empty),
            }),
        };
    }

    private static WorldResource Resource()
    {
        var rateInputs = default(RawResourceRateInputs);
        var traits = default(RawResourceTraits);
        var modifiers = default(RawResourceModifiers);
        var reading = new RawResourceSample(
            ResourceId,
            new BigDouble(8),
            new BigDouble(100),
            visible: true,
            lifetimeQuantity: new BigDouble(8),
            discoveryTime: BigDouble.Zero,
            quality: new BigDouble(100),
            gainRate: BigDouble.Zero,
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
        return new WorldResource(
            in reading,
            isCapped: true,
            headroom: new BigDouble(92),
            fillFraction: 0.08,
            isAtCapacity: false,
            trueQuantity: new BigDouble(8),
            trueRate: BigDouble.Zero);
    }

    private static WorldGlyph Glyph(
        Guid id,
        WorldDiscoverableDecision decision,
        int maximumUsages,
        bool learned = true) => new(
            id,
            level: 0,
            freeLevels: 0,
            discoveryRarityLevel: 1,
            learned: learned,
            discoverable: true,
            discoveryRequired: true,
            augmentsSpells: false,
            requiresDuration: false,
            requiresToggleable: false,
            masteryReqCount: 0,
            BigDouble.Zero,
            BigDouble.Zero,
            BigDouble.One,
            maximumUsages: maximumUsages,
            discovery: decision);

    private static EntityIdentityCatalogSnapshot Identities()
    {
        var rows = GameMcpTestHarness.EntityCatalog.Rows.AsSpan().ToArray().Concat(new[]
        {
            new EntityIdentityName(GlyphId, "GlyphSO", "Amplify", "amplify"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Arcane Dust", "arcaneDust"),
            new EntityIdentityName(ComponentId, "GlyphSO", "Focus", "focus"),
            new EntityIdentityName(AmbiguousOutputId, "GlyphSO", "Echo", "echo"),
        }).OrderBy(row => row.EntityId).ToArray();
        return EntityIdentityCatalogSnapshot.Bound(15, rows);
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, Identities()));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}

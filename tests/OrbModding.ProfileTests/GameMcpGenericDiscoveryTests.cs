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
    private static readonly Guid SpellRecipeId =
        Guid.Parse("f3000000-0000-0000-0000-000000000005");
    private static readonly Guid StructureId =
        Guid.Parse("f3000000-0000-0000-0000-000000000006");

    [Fact]
    public void ToolAdvertisesOneTargetAddressedAndEventOfferDiscoveryNamespace()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode", "uuid" }, schema["required"]!.Values<string>());
        Assert.Equal(
            new[] { "preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["uuid"]);
        Assert.NotNull(schema["properties"]!["offerUuid"]);
        Assert.Null(schema["properties"]!["surface"]);
        Assert.Null(schema["properties"]!["components"]);
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["amount"]);
    }

    /// <summary>
    /// The verb names what it would discover and nothing else. A caller reaching for the retired
    /// composition arguments is told they are not fields rather than silently ignored.
    /// </summary>
    [Fact]
    public void ValidationRefusesTheRetiredSurfaceAndComponentFieldsAndPreviewIsReadOnly()
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
        var composed = router.Handle(GameMcpAcceptanceFixture.Request(
            2,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject
                {
                    ["mode"] = "confirm",
                    ["uuid"] = GlyphId.ToString("D"),
                    ["surface"] = "glyphcraft",
                    ["components"] = new JArray(new JObject
                    {
                        ["uuid"] = ComponentId.ToString("D"),
                        ["count"] = 1,
                    }),
                },
            }));
        var accepted = router.Handle(GameMcpAcceptanceFixture.Request(
            3,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject
                {
                    ["mode"] = "preview",
                    ["uuid"] = GlyphId.ToString("D"),
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'mode' is missing; required field 'uuid' is missing",
            GameMcpTestHarness.Page(missing));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field 'surface' " +
            "is not accepted by game_discover; field 'components' is not accepted by " +
            "game_discover", GameMcpTestHarness.Page(composed));
        Assert.DoesNotContain(
            "refused (ERR_INPUT)", GameMcpTestHarness.Page(accepted), StringComparison.Ordinal);
        var operation = GameMcpProtocolRouter.BuildOperation(
            "game_discover",
            new JObject
            {
                ["mode"] = "preview",
                ["uuid"] = GlyphId.ToString("D"),
            });
        Assert.Equal(GameMcpOperationClass.ReadOnly, operation.Classification);
        Assert.Equal(GlyphId, operation.Uuid);
        Assert.Empty(operation.UuidCounts);
    }

    /// <summary>
    /// Every discovery screen is the same button, so every confirm lands on the one boundary that
    /// presses it — spells included.
    /// </summary>
    [Theory]
    [InlineData("spell-recipes")]
    [InlineData("glyphs")]
    [InlineData("rituals")]
    [InlineData("time-runes")]
    [InlineData("alchemy-recipes")]
    [InlineData("equipment")]
    public void Confirm_routes_every_discovery_screen_to_one_boundary(string category)
    {
        Assert.NotEmpty(category);
        Assert.Equal(
            GameMcpCommandKind.GenericDiscovery,
            GameMcpCommandKinds.FromRequest("game_discover", "confirm", string.Empty));
        Assert.Equal(
            GameMcpCommandKind.DiscoveryTreeOffer,
            GameMcpCommandKinds.FromRequest("game_discover", "offer_confirm", string.Empty));
        Assert.Equal(
            GameMcpCommandKind.SpellWorkbench,
            GameMcpCommandKinds.FromRequest("game_spell_loadout", "add", string.Empty));
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
    public void Preview_answers_the_buttons_admission_from_the_targets_own_row()
    {
        var preview = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(Context(), GlyphId));

        Assert.Equal("available", (string?)preview["status"]);
        Assert.Null(preview["surface"]);
        Assert.Null(preview["components"]);
        Assert.Null(preview["autoLoad"]);
        var output = preview["output"]!;
        Assert.Equal(GameMcpTestHarness.Handle(GlyphId), (string?)output["uuid"]);
        Assert.Equal("Amplify", (string?)output["name"]);
        Assert.True((bool)output["discover"]!["available"]!);
        Assert.NotNull(output["discover"]!["costs"]);
    }

    /// <summary>
    /// A spell's press may also load it, so the preview says which half of that is already settled.
    /// </summary>
    [Fact]
    public void Preview_of_a_spell_says_whether_the_same_press_would_load_it()
    {
        var free = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(Context(), SpellRecipeId));
        var full = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(
            Context(loadoutHasRoom: false), SpellRecipeId));

        Assert.Equal("unverified", (string?)free["autoLoad"]!["willLoad"]);
        Assert.Equal(
            "A loadout slot is free, so the game loads the spell straight away if its usage " +
            "cost fits the spell-power headroom. That fit is only settled once the spell exists.",
            (string?)free["autoLoad"]!["reason"]);
        Assert.Equal("no", (string?)full["autoLoad"]!["willLoad"]);
        Assert.Equal(
            "Every loadout slot holds a spell, so a discovered spell stays unloaded until you " +
            "free one.",
            (string?)full["autoLoad"]!["reason"]);
    }

    /// <summary>
    /// A locked discovery screen is the row's own answer, not a surprise saved for the press.
    /// </summary>
    /// <remarks>
    /// <c>ViewSO.IsAvailable()</c> is published for every view, so the two screens the suite pins —
    /// Magic &gt; Spellbook &gt; Unlock and Magic &gt; Augments &gt; Glyphcraft — are read here and
    /// answered in the same words the verb would refuse in.
    /// </remarks>
    [Fact]
    public void A_locked_discovery_screen_is_answered_on_the_row()
    {
        var locked = Context(screensUnlocked: false);

        var glyph = Json(GameMcpWorldQuery.ProjectPostState(
            locked, "glyphs", GlyphId))["discover"]!;
        var recipe = Json(GameMcpWorldQuery.ProjectPostState(
            locked, "spell-recipes", SpellRecipeId))["discover"]!;

        Assert.False((bool)glyph["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)glyph["reasonCode"]);
        Assert.Equal(
            "The screen this action lives on is not unlocked yet.", (string?)glyph["reason"]);
        Assert.Null(glyph["costs"]);
        Assert.False((bool)recipe["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)recipe["reasonCode"]);
        Assert.Null(recipe["costs"]);
    }

    [Fact]
    public void Preview_of_something_no_discovery_screen_draws_says_so()
    {
        var preview = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(Context(), StructureId));

        Assert.Equal("unavailable", (string?)preview["status"]);
        Assert.Equal("ERR_LOCKED", (string?)preview["reasonCode"]);
        Assert.Equal(
            "Watchtower is not something the game lets you discover.",
            (string?)preview["reason"]);
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
        bool componentLearned = true,
        bool loadoutHasRoom = true,
        bool screensUnlocked = true)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            World(ambiguous, componentLearned, loadoutHasRoom, screensUnlocked),
            new WorldGeneration(2301));
        return GameMcpTestHarness.Context(
            publisher.ReadLatest(), configurationGeneration: 8, lifecycleGeneration: 15);
    }

    private static GameWorldState World(
        bool ambiguous = false,
        bool componentLearned = true,
        bool loadoutHasRoom = true,
        bool screensUnlocked = true)
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
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(KnownEntities.MagicGlyphsDiscover.Uuid, false, false, screensUnlocked),
                new WorldView(KnownEntities.MagicSpellbookLearn.Uuid, false, false, screensUnlocked),
            }),
            Glyphs = PublicationTable<WorldGlyph>.Create(glyphs),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[] { SpellRecipe(decision) }),
            SpellWorkbench = new WorldSpellWorkbench(
                equippedCount: loadoutHasRoom ? 0 : 1,
                maximumEquipped: 1,
                hasEmptySlot: loadoutHasRoom),
            Resources = PublicationTable<WorldResource>.Create(new[] { Resource() }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "glyphs", WorldCategoryOutcome.Collected, glyphs.Length, 0, string.Empty),
            }),
        };
    }

    private static WorldSpellRecipe SpellRecipe(WorldDiscoverableDecision decision) => new(
        SpellRecipeId,
        false,
        1,
        BigDouble.Zero,
        0,
        false,
        false,
        false,
        0,
        1d,
        1,
        false,
        BigDouble.One,
        BigDouble.One,
        BigDouble.One,
        BigDouble.One,
        BigDouble.One,
        BigDouble.One,
        false,
        PublicationTable<WorldSpellRecipeGlyph>.Create(new[]
        {
            new WorldSpellRecipeGlyph(0, ComponentId),
        }),
        decision.Costs,
        true,
        decision);

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
            new EntityIdentityName(SpellRecipeId, "SpellRecipeSO", "Firebolt", "firebolt"),
            new EntityIdentityName(StructureId, "StructureSO", "Watchtower", "watchtower"),
        }).OrderBy(row => row.EntityId).ToArray();
        return EntityIdentityCatalogSnapshot.Bound(15, rows);
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, Identities()));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}

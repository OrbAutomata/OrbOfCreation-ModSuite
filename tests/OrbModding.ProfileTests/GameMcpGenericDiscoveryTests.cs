using System;
using System.Collections.Generic;
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
    /// <summary>The game's own Spell Discoveries tree, named in the shipped identity fixture.</summary>
    private static readonly Guid SpellTreeId =
        Guid.Parse("5ba0b305-21bd-4b43-af88-cc763ac04df8");
    private static readonly Guid OlderSpellInstanceId =
        Guid.Parse("4b8a5e2c-0d61-4f3a-9c77-6a1b2c3d4e5f");
    private static readonly Guid MintedSpellInstanceId =
        Guid.Parse("4b8a5e2c-0d61-4f3a-9c77-6a1b2c3d4e60");
    private static readonly Guid RecipeBookId =
        Guid.Parse("f3000000-0000-0000-0000-000000000007");

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
    [InlineData("augment-glyphs")]
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
            Context(), "augment-glyphs", GlyphId.ToString("D")));
        var postState = Json(GameMcpWorldQuery.ProjectPostState(
            Context(), "augment-glyphs", GlyphId));

        Assert.Equal("available", (string?)row["status"]);
        Assert.Null(row["worldGeneration"]);
        var glyph = row["row"]!;
        Assert.Equal(GameMcpTestHarness.Handle(GlyphId), (string?)glyph["uuid"]);
        Assert.Equal("Amplify", (string?)glyph["name"]);
        Assert.True((bool)glyph["discover"]!["available"]!);

        // `IsDiscoverRequired()` is captured and not published: a bare flag no verdict here turns
        // on. The tree that holds the required discovery says so on its own row.
        Assert.Null(glyph["discover"]!["required"]);
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
    /// The press that discovers a spell may also load it, and the number it answers with is the one
    /// the player will count to on the bar. The array index reached the wire: a confirm that filled
    /// bar slot 5 said <c>slot: 4</c> while the bar, the screen reader and <c>world_get</c> all said
    /// 5, so the caller's next press addressed the spell beside it.
    /// </summary>
    [Fact]
    public void A_confirmed_spell_says_the_bar_slot_the_screen_shows()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.GenericDiscovery, 9, 3, "confirm", SpellRecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(World(), generation: 41));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                World(spellDiscovered: true, loadedSlotIndex: 4), generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.False((bool)delta["discovered"]!["before"]!);
        Assert.True((bool)delta["discovered"]!["after"]!);
        Assert.True((bool)delta["loadout"]!["loaded"]!);
        Assert.Equal(5, (int)delta["loadout"]!["slot"]!);
    }

    /// <summary>
    /// The slot a confirm names is the copy that press minted, not the first copy on the bar.
    /// </summary>
    /// <remarks>
    /// A recipe can sit on the bar more than once, and the older copy is the levelled one a caller
    /// has been casting. Naming the first slot holding the recipe pointed a fresh discovery's
    /// answer at that older copy. The world before the press is what tells them apart: the minted
    /// copy is the occupant it did not carry.
    /// </remarks>
    [Fact]
    public void A_confirmed_spell_names_the_copy_the_press_minted()
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.GenericDiscovery, 9, 3, "confirm", SpellRecipeId, Guid.Empty,
            "SpellRecipeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                World(olderCopyOnBar: true), generation: 41));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                World(spellDiscovered: true, loadedSlotIndex: 4, olderCopyOnBar: true),
                generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.True((bool)delta["loadout"]!["loaded"]!);
        Assert.Equal(5, (int)delta["loadout"]!["slot"]!);
        Assert.Equal(
            GameMcpTestHarness.Handle(MintedSpellInstanceId), (string?)delta["loadout"]!["uuid"]);
    }

    /// <summary>
    /// A hidden spell's whole answer used to be that it was hidden. The Recipe Book it is behind is
    /// the one thing a caller can act on, and the press has named it since the discovery boundary
    /// landed: a live round met two hidden spells whose rows said only "The game is not showing
    /// this yet" and had to go and work out which book each was waiting on.
    /// </summary>
    [Fact]
    public void A_hidden_spell_row_names_the_recipe_book_it_is_waiting_on()
    {
        var row = Json(GameMcpWorldQuery.ProjectPostState(
            Context(spellHidden: true), "spell-recipes", SpellRecipeId))["discover"]!;

        Assert.False((bool)row["available"]!);
        Assert.Equal(
            "The game is not showing this yet. It needs the Expansion recipe book, which is not " +
            "owned.",
            (string?)row["reason"]);
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
            locked, "augment-glyphs", GlyphId))["discover"]!;
        var recipe = Json(GameMcpWorldQuery.ProjectPostState(
            locked, "spell-recipes", SpellRecipeId))["discover"]!;

        Assert.False((bool)glyph["available"]!);
        Assert.Equal("ERR_LOCKED", (string?)glyph["reasonCode"]);
        Assert.Equal(
            "Magic > Augments > Glyphcraft is not unlocked yet, so the game draws no row for this.",
            (string?)glyph["reason"]);
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

    /// <summary>
    /// The preview promises exactly what the press is gated on. Every preflight refusal a caller
    /// could have acted on ahead of the call is a predicate this row already answers; the rest are
    /// boundary revalidations no read could have settled, and there is no third kind.
    /// </summary>
    /// <remarks>
    /// A round read <c>available: yes</c> off a discovery preview and was refused on a core
    /// glyph's owned level — a gate the preview never named, twelve lines under a table showing
    /// that level as zero. The verb used to stage the recipe's glyphs into the spell workbench and
    /// validate them itself; it now presses the game's own row
    /// (<c>UIDiscoverablePage.HandleClick</c>) and stages nothing, so the gate is gone rather than
    /// hidden. What keeps it gone is this partition: a preflight a caller could act on has to
    /// arrive with the predicate that pre-reads it, or it falls in neither list and is named here.
    /// </remarks>
    [Fact]
    public void Every_gate_the_press_refuses_on_is_a_predicate_the_preview_already_answers()
    {
        var answeredByTheRow = new[]
        {
            GenericDiscoveryPreflight.NotVisible,
            GenericDiscoveryPreflight.AlreadyDiscovered,
            GenericDiscoveryPreflight.DiscoveryUnavailable,
            GenericDiscoveryPreflight.Unaffordable,
            GenericDiscoveryPreflight.GlyphRecipeEmpty,
            GenericDiscoveryPreflight.ScreenLocked,
        };
        var settledOnlyAtTheBoundary = new[]
        {
            GenericDiscoveryPreflight.Proceeded,
            GenericDiscoveryPreflight.ContractUnavailable,
            GenericDiscoveryPreflight.WrongThread,
            GenericDiscoveryPreflight.LifecycleReplaced,
            GenericDiscoveryPreflight.IdentityUnavailable,
            GenericDiscoveryPreflight.UnsupportedType,
            GenericDiscoveryPreflight.MutationPermitUnavailable,
            GenericDiscoveryPreflight.PostCommitFault,
            GenericDiscoveryPreflight.VerificationFailed,
        };

        Assert.Empty(answeredByTheRow.Intersect(settledOnlyAtTheBoundary));
        Assert.Equal(
            Enum.GetValues<GenericDiscoveryPreflight>().OrderBy(value => (int)value),
            answeredByTheRow.Concat(settledOnlyAtTheBoundary).OrderBy(value => (int)value));

        // So the all-clear answer defers nothing: it says yes and names no gate left over. The one
        // fact this press genuinely cannot settle ahead of itself is the auto-load, and it wears
        // the suite's word for that instead of a verdict.
        var preview = Json(GameMcpWorldQuery.ProjectDiscoveryPreview(Context(), SpellRecipeId));
        var discover = preview["output"]!["discover"]!;

        Assert.Equal("available", (string?)preview["status"]);
        Assert.True((bool)discover["available"]!);
        Assert.Null(discover["reasonCode"]);
        Assert.Null(discover["verbDecides"]);
        Assert.Equal("unverified", (string?)preview["autoLoad"]!["willLoad"]);
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

    /// <summary>
    /// Every discoverable kind is gated on the screen its tree is drawn under, alchemy recipes
    /// included — both of theirs.
    /// </summary>
    /// <remarks>
    /// Each <c>DiscoveryTreeSO</c> carries an authored <c>viewLocation</c> whose last element is
    /// the view its page is drawn under: rituals end at <c>RitualsDiscover</c>, artifacts at
    /// <c>WorkshopArtifactCreate</c>, time runes at <c>TimeTimeRuneCreate</c>.
    /// <c>AlchemyRecipeSO</c> is drawn on two — <c>ConceptDiscoveryTree</c> ends at
    /// <c>ScholarConceptDiscover</c> while <c>AlchemyDiscoveryTree</c> ends at
    /// <c>AlchAlchemyDiscover</c> — so its row reads the screen off the recipe's own alchemy type.
    /// </remarks>
    [Theory]
    [InlineData("rituals", "2ebf945f-56bc-44fe-a82a-7f117779ce37", "Rituals > Discover")]
    [InlineData("equipment", "02c64c96-de30-4e73-bafe-5f454bb58a66",
        "Workshop > Artifacts > Create")]
    [InlineData("time-runes", "01a6d158-0fcd-40bc-a3a2-8f748086201d",
        "Time > Time Runes > Create")]
    [InlineData("alchemy-recipes", "05589125-5a98-4e74-a1ae-2b2146ea68c4",
        "Alchemy > Alchemy > Learn")]
    [InlineData("alchemy-recipes", "6f7f6b2c-6ad0-4a05-9a35-0f2ab7e0e0d1",
        "Scholar > Concepts > Discover")]
    public void A_discovery_row_is_gated_on_the_screen_its_tree_is_drawn_under(
        string category, string uuid, string path)
    {
        var id = Guid.Parse(uuid);

        var open = Json(GameMcpWorldQuery.GetRow(
            ScreenContext(screensUnlocked: true), category, id.ToString("D")))["row"]!;
        var shut = Json(GameMcpWorldQuery.GetRow(
            ScreenContext(screensUnlocked: false), category, id.ToString("D")))["row"]!;

        Assert.True((bool)open["discover"]!["available"]!);
        Assert.False((bool)shut["discover"]!["available"]!);
        Assert.Null(shut["discover"]!["reasonCode"]);
        // The door is named. "The screen this action lives on" is true and unusable: a live round
        // met it on a ritual and on an artifact and had to work out which page it meant.
        Assert.Equal(
            path + " is not unlocked yet, so the game draws no row for this.",
            (string?)shut["discover"]!["reason"]);
    }

    /// <summary>
    /// Each of the two alchemy screens gates only the recipes it draws: with Alchemy &gt; Alchemy
    /// &gt; Learn shut and Scholar &gt; Concepts &gt; Discover open, the ordinary recipe answers
    /// locked and the concept answers available, and the other way round.
    /// </summary>
    [Fact]
    public void Neither_alchemy_screen_gates_the_rows_the_other_one_draws()
    {
        var conceptsOnly = ScreenContext(
            screensUnlocked: false, conceptDiscoverUnlocked: true);
        var learnOnly = ScreenContext(
            screensUnlocked: false, alchemyLearnUnlocked: true);

        var shutPotion = Json(GameMcpWorldQuery.GetRow(
            conceptsOnly, "alchemy-recipes", AlchemyRecipeId.ToString("D")))["row"]!;
        var openConcept = Json(GameMcpWorldQuery.GetRow(
            conceptsOnly, "alchemy-recipes", ConceptRecipeId.ToString("D")))["row"]!;
        var openPotion = Json(GameMcpWorldQuery.GetRow(
            learnOnly, "alchemy-recipes", AlchemyRecipeId.ToString("D")))["row"]!;
        var shutConcept = Json(GameMcpWorldQuery.GetRow(
            learnOnly, "alchemy-recipes", ConceptRecipeId.ToString("D")))["row"]!;

        Assert.False((bool)shutPotion["discover"]!["available"]!);
        Assert.Null(shutPotion["discover"]!["reasonCode"]);
        Assert.True((bool)openConcept["discover"]!["available"]!);
        Assert.True((bool)openPotion["discover"]!["available"]!);
        Assert.False((bool)shutConcept["discover"]!["available"]!);
        Assert.Null(shutConcept["discover"]!["reasonCode"]);
    }

    /// <summary>
    /// A recipe whose alchemy type belongs to neither screen answers that it could not be told,
    /// rather than naming a screen and being wrong half the time.
    /// </summary>
    [Fact]
    public void An_alchemy_row_belonging_to_neither_screen_says_the_screen_is_unknown()
    {
        var row = Json(GameMcpWorldQuery.GetRow(
            ScreenContext(screensUnlocked: true),
            "alchemy-recipes",
            OrphanRecipeId.ToString("D")))["row"]!;

        Assert.False((bool)row["discover"]!["available"]!);
        Assert.Null(row["discover"]!["reasonCode"]);
        Assert.Equal(
            "Which screen the game draws this on could not be told, so whether that screen is " +
            "unlocked is unknown.",
            (string?)row["discover"]!["reason"]);
    }

    /// <summary>
    /// A row the tree is holding as the offer the player picked prints the tree, not a price.
    /// </summary>
    /// <remarks>
    /// A live round read a concept's row straight after the roll that offered it and was told
    /// <c>available: no, ERR_UNAFFORDABLE, cost: 100 of 63.0 Psi</c> — the 100 the roll had just
    /// spent. Taking the offer costs nothing more: <c>UIDiscoveryTreePage.OnConfirmClick</c>
    /// reaches <c>DiscoveryTreeSO.DiscoverSelectedItem</c>, which charges nothing, while the row's
    /// own price is a separate <c>IDiscoverable.GetDiscoverCost()</c> purchase. Every other row
    /// keeps its price, so the suppression is the tree's state and not a blanket.
    /// </remarks>
    [Fact]
    public void A_row_the_tree_holds_as_its_offer_prints_the_tree_and_not_a_second_price()
    {
        var context = ScreenContext(screensUnlocked: true, treeHoldsOffer: ConceptRecipeId);

        var held = Json(GameMcpWorldQuery.GetRow(
            context, "alchemy-recipes", ConceptRecipeId.ToString("D")))["row"]!["discover"]!;
        var other = Json(GameMcpWorldQuery.GetRow(
            context, "alchemy-recipes", AlchemyRecipeId.ToString("D")))["row"]!["discover"]!;

        Assert.False((bool)held["available"]!);
        Assert.Null(held["reasonCode"]);
        Assert.Equal(
            "Concept Discoveries is holding this as the offer you picked, and confirming it " +
            "there costs nothing more — the roll already paid. Use game_discover " +
            "mode=offer_confirm on that tree; discovering it from this row would be a second, " +
            "separate purchase.",
            (string?)held["reason"]);
        Assert.Null(held["costs"]);
        Assert.Equal(
            GameMcpTestHarness.Handle(ConceptTreeId), (string?)held["offeredBy"]!["uuid"]);
        Assert.True((bool)other["available"]!);
        Assert.NotNull(other["costs"]);

        // The spell row writes its own `discover` block rather than going through
        // AddDiscoveryDecision, so the rule has to hold on both producers or it holds on five
        // surfaces out of six.
        var spell = Json(GameMcpWorldQuery.ProjectPostState(
            Context(treeHoldsOffer: SpellRecipeId), "spell-recipes", SpellRecipeId))["discover"]!;

        Assert.False((bool)spell["available"]!);
        Assert.Equal("ERR_STATE", (string?)spell["reasonCode"]);
        Assert.Equal(
            "Spell Discoveries is holding this as the offer you picked, and confirming it there " +
            "costs nothing more — the roll already paid. Use game_discover mode=offer_confirm " +
            "on that tree; discovering it from this row would be a second, separate purchase.",
            (string?)spell["reason"]);
        Assert.Null(spell["costs"]);
    }

    private static readonly Guid AlchemyRecipeId =
        Guid.Parse("05589125-5a98-4e74-a1ae-2b2146ea68c4");
    private static readonly Guid AlchemyTypeId =
        Guid.Parse("b42c6192-7d9b-40d0-aa40-3d46a9348e52");
    private static readonly Guid ConceptRecipeId =
        Guid.Parse("6f7f6b2c-6ad0-4a05-9a35-0f2ab7e0e0d1");
    private static readonly Guid OrphanRecipeId =
        Guid.Parse("6f7f6b2c-6ad0-4a05-9a35-0f2ab7e0e0d2");

    /// <summary>The game's own Concept Discoveries tree, named in the shipped identity fixture.</summary>
    private static readonly Guid ConceptTreeId =
        Guid.Parse("3444dae4-9323-4e4e-8a0a-ae800da15ab8");

    /// <summary>The game's own Psi, the resource the live round's concept row asked 100 of.</summary>
    private static readonly Guid PsiId =
        Guid.Parse("471e1ce5-18ab-446d-a3bc-fcaa17bda96e");

    /// <summary>An alchemy type in neither audited set, so neither screen claims its recipe.</summary>
    private static readonly Guid OrphanAlchemyTypeId =
        Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static WorldAlchemyRecipe AlchemyRecipe(
        Guid recipeId,
        Guid coreTypeId,
        WorldDiscoverableDecision discovery) => new(
        recipeId, coreTypeId, discovered: false, maxLevel: 1,
        advancementLevel: 0, discoveryRarityLevel: 0, masteryXp: BigDouble.Zero,
        masteryLevel: 0, recipeTime: BigDouble.One, isRequiredDiscovery: false,
        isCompletionRecipe: false, isAdvancementRecipe: false, completionTime: 0,
        isDebugAlchemy: false, power: BigDouble.Zero, speed: BigDouble.Zero,
        drainCostMod: BigDouble.Zero, special: BigDouble.Zero,
        timeReqMod: BigDouble.Zero, timeScalingMod: BigDouble.Zero,
        masteryXpRate: BigDouble.Zero, effectLevels: BigDouble.Zero,
        overdrivePower: BigDouble.Zero, overdriveSpeed: BigDouble.Zero,
        overdriveDrainCostMod: BigDouble.Zero, overdriveXpRate: BigDouble.Zero,
        freeUsageSlots: BigDouble.One, maxUsageSlots: new BigDouble(8),
        cachedCompletionTime: BigDouble.Zero, requiredExperience: BigDouble.One,
        discovery: discovery);

    /// <summary>
    /// One row of each screen-gated kind, all offered by the game, with the owning views open or
    /// shut together — and the two alchemy screens separately settable, because the two alchemy
    /// rows are drawn on different ones.
    /// </summary>
    private static GameMcpFrameContext ScreenContext(
        bool screensUnlocked,
        bool? alchemyLearnUnlocked = null,
        bool? conceptDiscoverUnlocked = null,
        Guid treeHoldsOffer = default)
    {
        var offered = new WorldDiscoverableDecision(
            visible: true,
            canDiscover: true,
            discovered: false,
            required: false,
            affordable: true,
            PublicationTable<WorldDiscoverableCost>.Create(new[]
            {
                new WorldDiscoverableCost(PsiId, new BigDouble(100), new BigDouble(63)),
            }));
        var modifiers = default(RawRitualModifiers);
        var world = new GameWorldState
        {
            CollectedAtEpoch = 15,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "rituals", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "equipment", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "time runes", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
                new WorldCollectionCategoryStatus(
                    "alchemy recipes", WorldCategoryOutcome.Collected, 3, 0, string.Empty),
            }),
            Views = PublicationTable<WorldView>.Create(new[]
            {
                new WorldView(
                    KnownEntities.RitualsDiscover.Uuid, false, false, screensUnlocked),
                new WorldView(
                    KnownEntities.WorkshopArtifactCreate.Uuid, false, false, screensUnlocked),
                new WorldView(
                    KnownEntities.TimeTimeRuneCreate.Uuid, false, false, screensUnlocked),
                new WorldView(
                    KnownEntities.AlchAlchemyDiscover.Uuid, false, false,
                    alchemyLearnUnlocked ?? screensUnlocked),
                new WorldView(
                    KnownEntities.ScholarConceptDiscover.Uuid, false, false,
                    conceptDiscoverUnlocked ?? screensUnlocked),
            }.OrderBy(view => view.EntityId).ToArray()),
            Rituals = PublicationTable<WorldRitual>.Create(new[]
            {
                new WorldRitual(
                    Guid.Parse("2ebf945f-56bc-44fe-a82a-7f117779ce37"),
                    discovered: false,
                    inBattle: false,
                    activeInstances: 0,
                    reachedLevel: 0,
                    lastReachedLevel: 0,
                    selectedLevel: 1,
                    wavesCompleted: 0,
                    discoveryRarityLevel: 0,
                    critLevel: 0,
                    echoLevel: 0,
                    chainLevel: 0,
                    durationRewardBlocks: 0,
                    battleTotalWeight: BigDouble.Zero,
                    in modifiers,
                    hideEndScreenResults: false,
                    isDiscoverRequired: false,
                    forceLevel: false,
                    forceLevelValue: 0,
                    baseWaves: 0,
                    maxWaves: 0,
                    requiredWaves: 0,
                    baseWeight: 0,
                    minimumEffectLevel: 0,
                    failedRun: false,
                    discovery: offered),
            }),
            Equipment = PublicationTable<WorldEquipment>.Create(new[]
            {
                new WorldEquipment(
                    Guid.Parse("02c64c96-de30-4e73-bafe-5f454bb58a66"),
                    false, 0, BigDouble.Zero, 0, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, 0, 0, -1, BigDouble.Zero,
                    discovery: offered,
                    loadout: new WorldEquipmentDecision(
                        false, "not created yet", Guid.Empty, 0, 0, 0, 0, 0, 0, 0, 0, false,
                        PublicationTable<WorldEquipmentUsageCost>.Empty)),
            }),
            TimeRunes = PublicationTable<WorldTimeRune>.Create(new[]
            {
                new WorldTimeRune(
                    Guid.Parse("01a6d158-0fcd-40bc-a3a2-8f748086201d"),
                    false, 0, 0, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero,
                    discovery: offered),
            }),
            AlchemyRecipes = PublicationTable<WorldAlchemyRecipe>.Create(new[]
            {
                AlchemyRecipe(AlchemyRecipeId, AlchemyTypeId, offered),
                AlchemyRecipe(
                    ConceptRecipeId,
                    AlchemyGameplayDomainClassifier.ReductiveConceptTypeUuid,
                    offered),
                AlchemyRecipe(OrphanRecipeId, OrphanAlchemyTypeId, offered),
            }.OrderBy(recipe => recipe.EntityId).ToArray()),
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
            {
                new WorldDiscoveryTree(
                    ConceptTreeId, true, 2, BigDouble.Zero, 1, false, treeHoldsOffer,
                    treeHoldsOffer == Guid.Empty
                        ? Array.Empty<Guid>()
                        : new[] { treeHoldsOffer },
                    false, true, Array.Empty<WorldDiscoveryTreeCost>(),
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false),
            }),
        };
        return GameMcpTestHarness.Context(world, generation: 2311);
    }

    private static GameMcpFrameContext Context(
        bool ambiguous = false,
        bool componentLearned = true,
        bool loadoutHasRoom = true,
        bool screensUnlocked = true,
        bool spellHidden = false,
        Guid treeHoldsOffer = default)
    {
        using var publisher =
            new ServiceWorldPublisher<GameWorldState>(GameWorldStateDefaults.Empty);
        publisher.Publish(
            World(
                ambiguous,
                componentLearned,
                loadoutHasRoom,
                screensUnlocked,
                spellHidden: spellHidden,
                treeHoldsOffer: treeHoldsOffer),
            new WorldGeneration(2301));
        return GameMcpTestHarness.Context(
            publisher.ReadLatest(), configurationGeneration: 8, lifecycleGeneration: 15);
    }

    private static GameWorldState World(
        bool ambiguous = false,
        bool componentLearned = true,
        bool loadoutHasRoom = true,
        bool screensUnlocked = true,
        bool spellDiscovered = false,
        int loadedSlotIndex = -1,
        bool spellHidden = false,
        bool olderCopyOnBar = false,
        Guid treeHoldsOffer = default)
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
        var hidden = new WorldDiscoverableDecision(
            visible: false,
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
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(glyphs),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(
                new[] { SpellRecipe(spellHidden ? hidden : decision, spellDiscovered) }),
            RecipeBookGlyphs = PublicationTable<WorldRecipeBookGlyph>.Create(new[]
            {
                new WorldRecipeBookGlyph(ComponentId, RecipeBookId),
            }),
            RecipeBooks = PublicationTable<WorldRecipeBook>.Create(new[]
            {
                new WorldRecipeBook(RecipeBookId, available: false),
            }),
            SpellSlots = SpellSlots(loadedSlotIndex, olderCopyOnBar),
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
            {
                new WorldDiscoveryTree(
                    SpellTreeId, true, 2, BigDouble.Zero, 1, false, treeHoldsOffer,
                    treeHoldsOffer == Guid.Empty
                        ? Array.Empty<Guid>()
                        : new[] { treeHoldsOffer },
                    false, true, Array.Empty<WorldDiscoveryTreeCost>(),
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false),
            }),
            SpellWorkbench = new WorldSpellWorkbench(
                equippedCount: loadoutHasRoom ? 0 : 1,
                maximumEquipped: 1,
                hasEmptySlot: loadoutHasRoom),
            Resources = PublicationTable<WorldResource>.Create(new[] { Resource() }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "augment glyphs", WorldCategoryOutcome.Collected, glyphs.Length, 0, string.Empty),
            }),
        };
    }

    /// <summary>
    /// The bar as the world publishes it: every occupant carries the instance identity of the copy
    /// in that slot, which is how the copy a press minted is told from one that was already there.
    /// </summary>
    private static PublicationTable<WorldSpellSlot> SpellSlots(
        int loadedSlotIndex,
        bool olderCopyOnBar)
    {
        var slots = new List<WorldSpellSlot>();
        if (olderCopyOnBar) slots.Add(Slot(0, OlderSpellInstanceId));
        if (loadedSlotIndex >= 0) slots.Add(Slot(loadedSlotIndex, MintedSpellInstanceId));
        return slots.Count == 0
            ? PublicationTable<WorldSpellSlot>.Empty
            : PublicationTable<WorldSpellSlot>.Create(slots.ToArray());
    }

    private static WorldSpellSlot Slot(int slotIndex, Guid instanceId) => new(
        slotIndex, instanceId, SpellRecipeId, occupied: true, casting: false,
        readyingCast: false, attuning: false, channeled: false, toggled: false,
        chargeable: false, castReady: true, chargeAvailable: true,
        resourcesCovered: true, currentCharges: 1, maximumCharges: 1,
        cooldownRemaining: BigDouble.Zero);

    private static WorldSpellRecipe SpellRecipe(
        WorldDiscoverableDecision decision,
        bool discovered = false) => new(
        SpellRecipeId,
        discovered,
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
            new EntityIdentityName(RecipeBookId, "RecipeBookSO", "Expansion", "expansion"),
        }).OrderBy(row => row.EntityId).ToArray();
        return EntityIdentityCatalogSnapshot.Bound(15, rows);
    }

    private static JObject Json(GameMcpValue value) =>
        Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(value, Identities()));

    private static JObject Json(GameMcpObjectBuilder value) => Json(value.Freeze());
}

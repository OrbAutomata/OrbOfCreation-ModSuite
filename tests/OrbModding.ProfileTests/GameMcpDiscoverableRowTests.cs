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

public sealed class GameMcpDiscoverableRowTests
{
    private static readonly Guid GlyphId =
        Guid.Parse("f3000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId =
        Guid.Parse("f3000000-0000-0000-0000-000000000002");
    private static readonly Guid ComponentId =
        Guid.Parse("f3000000-0000-0000-0000-000000000003");
    private static readonly Guid SpellRecipeId =
        Guid.Parse("f3000000-0000-0000-0000-000000000005");
    /// <summary>The game's own Spell Discoveries tree, named in the shipped identity fixture.</summary>
    private static readonly Guid SpellTreeId =
        Guid.Parse("5ba0b305-21bd-4b43-af88-cc763ac04df8");
    private static readonly Guid OlderSpellInstanceId =
        Guid.Parse("4b8a5e2c-0d61-4f3a-9c77-6a1b2c3d4e5f");
    private static readonly Guid MintedSpellInstanceId =
        Guid.Parse("4b8a5e2c-0d61-4f3a-9c77-6a1b2c3d4e60");
    private static readonly Guid RecipeBookId =
        Guid.Parse("f3000000-0000-0000-0000-000000000007");

    /// <summary>
    /// No screen discovers one thing, so the verb drives a tree's roll and nothing else. A per-thing
    /// press once discovered a spell no button could have.
    /// </summary>
    [Fact]
    public void The_tool_drives_only_a_trees_roll()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(new[] { "mode", "uuid" }, schema["required"]!.Values<string>());
        Assert.Equal(
            new[] { "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>());
        Assert.NotNull(schema["properties"]!["offerUuid"]);

        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox, GameMcpToolCatalog.Shipping);
        var confirm = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject
                {
                    ["mode"] = "confirm",
                    ["uuid"] = GlyphId.ToString("D"),
                },
            }));

        Assert.StartsWith("refused (ERR_INPUT)", GameMcpTestHarness.Page(confirm));
        Assert.Equal(
            GameMcpCommandKind.DiscoveryTreeOffer,
            GameMcpToolCatalog.Shipping.BuildOperation(
                "game_discover",
                new JObject
                {
                    ["mode"] = "offer_confirm",
                    ["uuid"] = GlyphId.ToString("D"),
                }).Route.Kind);
    }

    /// <summary>
    /// An undiscovered row says which roll gives it and whether that roll can, and carries no
    /// price: there is no press on the row for a price to belong to. Glyphs, runes, concepts and
    /// spells read <c>discover: yes</c> with a cost, and the press behind it refused.
    /// </summary>
    [Fact]
    public void An_undiscovered_row_names_the_roll_that_gives_it_and_no_price()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            Context(), "augment-glyphs", GlyphId.ToString("D")), Identities());
        var discover = row["row"]!["discover"]!;

        Assert.False((bool)discover["available"]!);
        Assert.Equal(
            "Only the roll on Magic > Augments > Glyphcraft gives this, and it is in that " +
            "roll's pool.",
            (string?)discover["reason"]);
        Assert.Null(discover["costs"]);
        Assert.Null(discover["required"]);
    }

    /// <summary>
    /// A spell is out of its roll's pool until every Recipe Book of its <c>recipeBookList</c> is
    /// owned — <c>SpellRecipeSO.IsDiscoverVisible</c> — and the row names the book. The core
    /// glyphs are the data's old composition shape and no longer reach the wire.
    /// </summary>
    [Fact]
    public void A_hidden_spell_row_names_the_recipe_book_its_pool_is_waiting_on()
    {
        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectPostState(
            Context(spellHidden: true), "spell-recipes", SpellRecipeId), Identities());
        var discover = row["discover"]!;

        Assert.False((bool)discover["available"]!);
        Assert.Equal(
            "Only the roll on Magic > Spellbook > Unlock gives this, and it is not in that " +
            "roll's pool until you own the Expansion recipe book.",
            (string?)discover["reason"]);
        Assert.Null(discover["costs"]);
        Assert.Null(row["composedOf"]);
        var book = Assert.Single(row["belongsTo"]!["recipeBooks"]!.Values<JObject>())!;
        Assert.Equal("Expansion", (string?)book["name"]);
        Assert.False((bool)book["owned"]!);
    }

    /// <summary>
    /// A confirmed offer that discovers a spell says the bar slot the screen shows. The array
    /// index reached the wire once: a press that filled bar slot 5 said <c>slot: 4</c>.
    /// </summary>
    [Fact]
    public void A_confirmed_spell_offer_says_the_bar_slot_the_screen_shows()
    {
        var delta = OfferConfirmDelta(World(), World(spellDiscovered: true, loadedSlotIndex: 4));

        Assert.True((bool)delta["loadout"]!["loaded"]!);
        Assert.Equal(5, (int)delta["loadout"]!["slot"]!);
    }

    /// <summary>
    /// The slot a confirm names is the copy that press minted, not the first copy on the bar: the
    /// older copy is the levelled one a caller has been casting, and the world before the press is
    /// what tells them apart.
    /// </summary>
    [Fact]
    public void A_confirmed_spell_offer_names_the_copy_the_press_minted()
    {
        var delta = OfferConfirmDelta(
            World(olderCopyOnBar: true),
            World(spellDiscovered: true, loadedSlotIndex: 4, olderCopyOnBar: true));

        Assert.True((bool)delta["loadout"]!["loaded"]!);
        Assert.Equal(5, (int)delta["loadout"]!["slot"]!);
        Assert.Equal(
            GameMcpTestHarness.Handle(MintedSpellInstanceId), (string?)delta["loadout"]!["uuid"]);
    }

    /// <summary>
    /// Every discoverable kind names the screen its tree is drawn under, alchemy recipes included —
    /// both of theirs.
    /// </summary>
    /// <remarks>
    /// Each <c>DiscoveryTreeSO</c> carries an authored <c>viewLocation</c> whose last element is
    /// the view its page is drawn under. <c>AlchemyRecipeSO</c> is drawn on two —
    /// <c>ConceptDiscoveryTree</c> ends at <c>ScholarConceptDiscover</c> while
    /// <c>AlchemyDiscoveryTree</c> ends at <c>AlchAlchemyDiscover</c> — so its row reads the screen
    /// off the recipe's own alchemy type.
    /// </remarks>
    [Theory]
    [InlineData("rituals", "2ebf945f-56bc-44fe-a82a-7f117779ce37", "Rituals > Discover")]
    [InlineData("equipment", "02c64c96-de30-4e73-bafe-5f454bb58a66",
        "Workshop > Artifacts > Create")]
    [InlineData("time-runes", "01a6d158-0fcd-40bc-a3a2-8f748086201d",
        "Time > Time Runes > Create")]
    [InlineData("alchemy-recipes", "05589125-5a98-4e74-a1ae-2b2146ea68c4",
        "Alchemy > Alchemy > Learn")]
    [InlineData("concepts", "6f7f6b2c-6ad0-4a05-9a35-0f2ab7e0e0d1",
        "Scholar > Concepts > Discover")]
    public void A_discovery_row_names_the_screen_its_tree_is_drawn_under(
        string category, string uuid, string path)
    {
        var discover = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            ScreenContext(), category, uuid), Identities())["row"]!["discover"]!;

        Assert.False((bool)discover["available"]!);
        Assert.Equal(
            "Only the roll on " + path + " gives this, and it is in that roll's pool.",
            (string?)discover["reason"]);
    }

    /// <summary>
    /// A recipe whose alchemy type belongs to neither screen names no screen rather than one that
    /// is wrong half the time.
    /// </summary>
    [Fact]
    public void An_alchemy_row_belonging_to_neither_screen_names_no_screen()
    {
        var discover = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            ScreenContext(), "alchemy-recipes", OrphanRecipeId.ToString("D")), Identities())["row"]!["discover"]!;

        Assert.Equal(
            "Only a discovery tree's roll gives this, and it is in that roll's pool.",
            (string?)discover["reason"]);
    }

    /// <summary>
    /// A row a tree is offering right now points at the tree's own pick.
    /// </summary>
    [Fact]
    public void A_row_on_offer_says_to_pick_it_on_its_tree()
    {
        var discover = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            ScreenContext(onOffer: ConceptRecipeId),
            "concepts",
            ConceptRecipeId.ToString("D")), Identities())["row"]!["discover"]!;

        Assert.Equal(
            "Only the roll on Scholar > Concepts > Discover gives this, and it is on offer there " +
            "now; pick it with game_discover mode=offer_select on that tree.",
            (string?)discover["reason"]);
        Assert.True((bool)discover["offered"]!);
    }

    /// <summary>
    /// A row the tree is holding as the offer the player picked prints the tree.
    /// </summary>
    /// <remarks>
    /// Taking the offer costs nothing more: <c>UIDiscoveryTreePage.OnConfirmClick</c> reaches
    /// <c>DiscoveryTreeSO.DiscoverSelectedItem</c>, which charges nothing.
    /// </remarks>
    [Fact]
    public void A_row_the_tree_holds_as_its_offer_prints_the_tree()
    {
        var context = ScreenContext(treeHoldsOffer: ConceptRecipeId);

        var held = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "concepts", ConceptRecipeId.ToString("D")), Identities())["row"]!["discover"]!;

        Assert.False((bool)held["available"]!);
        Assert.Equal(
            "Concept Discoveries is holding this as the offer you picked, and confirming it " +
            "there costs nothing more — the roll already paid. Use game_discover " +
            "mode=offer_confirm on that tree.",
            (string?)held["reason"]);
        Assert.Equal(
            GameMcpTestHarness.Handle(ConceptTreeId), (string?)held["offeredBy"]!["uuid"]);

        // The spell row writes its own `discover` block, so the rule has to hold on both producers.
        var spell = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectPostState(
            Context(treeHoldsOffer: SpellRecipeId), "spell-recipes", SpellRecipeId), Identities())["discover"]!;

        Assert.Equal("ERR_STATE", (string?)spell["reasonCode"]);
        Assert.Equal(
            "Spell Discoveries is holding this as the offer you picked, and confirming it there " +
            "costs nothing more — the roll already paid. Use game_discover mode=offer_confirm " +
            "on that tree.",
            (string?)spell["reason"]);
    }

    private static JObject OfferConfirmDelta(GameWorldState before, GameWorldState after)
    {
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "confirm", SpellTreeId, SpellRecipeId,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(before, generation: 41));
        return GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(after, generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));
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
    /// One undiscovered row of each kind a discovery screen draws, all in their roll's pool.
    /// </summary>
    private static GameMcpFrameContext ScreenContext(
        Guid treeHoldsOffer = default,
        Guid onOffer = default)
    {
        var offered = new WorldDiscoverableDecision(
            visible: true,
            canDiscover: true,
            discovered: false,
            required: false,
            affordable: true,
            PublicationTable<WorldResourceCost>.Create(new[]
            {
                new WorldResourceCost(PsiId, new BigDouble(100), new BigDouble(63)),
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
                    treeHoldsOffer != Guid.Empty
                        ? new[] { treeHoldsOffer }
                        : onOffer != Guid.Empty
                            ? new[] { onOffer }
                            : Array.Empty<Guid>(),
                    false, true, Array.Empty<WorldResourceCost>(),
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false),
            }),
        };
        return GameMcpTestHarness.Context(world, generation: 2311);
    }

    private static GameMcpFrameContext Context(
        bool spellHidden = false,
        Guid treeHoldsOffer = default) =>
        GameMcpTestHarness.Context(
            World(spellHidden: spellHidden, treeHoldsOffer: treeHoldsOffer),
            2301,
            configurationGeneration: 8,
            lifecycleGeneration: 15);

    private static GameWorldState World(
        bool spellDiscovered = false,
        int loadedSlotIndex = -1,
        bool spellHidden = false,
        bool olderCopyOnBar = false,
        Guid treeHoldsOffer = default)
    {
        var costs = PublicationTable<WorldResourceCost>.Create(new[]
        {
            new WorldResourceCost(ResourceId, new BigDouble(5), new BigDouble(8)),
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
        var glyphs = new[] { Glyph(GlyphId, decision, maximumUsages: 1) };
        return new GameWorldState
        {
            CollectedAtEpoch = 15,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            EntityIdentities = Identities(),
            AugmentGlyphs = PublicationTable<WorldGlyph>.Create(glyphs),
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(
                new[] { SpellRecipe(spellHidden ? hidden : decision, spellDiscovered) }),
            SpellRelations = PublicationTable<WorldSpellRelation>.Create(new[]
            {
                new WorldSpellRelation(
                    SpellRecipeId, WorldSpellRelationKind.RecipeBook, 0, RecipeBookId),
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
                    false, true, Array.Empty<WorldResourceCost>(),
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false),
            }),
            SpellWorkbench = new WorldSpellWorkbench(
                equippedCount: 0,
                maximumEquipped: 1,
                hasEmptySlot: true),
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
        int maximumUsages) => new(
            id,
            level: 0,
            freeLevels: 0,
            discoveryRarityLevel: 1,
            learned: true,
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

    private static EntityIdentityCatalogSnapshot Identities() =>
        GameMcpTestHarness.CatalogWith(
            new EntityIdentityName(GlyphId, "GlyphSO", "Amplify", "amplify"),
            new EntityIdentityName(ResourceId, "ResourceSO", "Arcane Dust", "arcaneDust"),
            new EntityIdentityName(ComponentId, "GlyphSO", "Focus", "focus"),
            new EntityIdentityName(SpellRecipeId, "SpellRecipeSO", "Firebolt", "firebolt"),
            new EntityIdentityName(RecipeBookId, "RecipeBookSO", "Expansion", "expansion"));
}

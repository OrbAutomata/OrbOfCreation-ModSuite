using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpDiscoveryTreeTestSupport;

namespace OrbModding.ProfileTests;

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpDiscoveryTreeReadTests
{
    [Fact]
    public void WorldGetAndOfferAdmissionShareTheSameDiscoveryTreeIdentityAndNativeType()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var tree = new WorldDiscoveryTree(
            treeId,
            visible: true,
            actionMode: 0,
            actionTime: BigDouble.Zero,
            rerollsLeft: 2,
            usedRerollsLastDiscover: false,
            selectedChoiceId: Guid.Empty,
            currentOfferIds: Array.Empty<Guid>(),
            hasImmediateRequiredDiscovery: false,
            nextItemAffordable: true,
            nextItemCosts: Array.Empty<WorldResourceCost>(),
            overrideRerollsId: Guid.Empty,
            overrideChoicesId: Guid.Empty,
            additionalDiscoveryChoices: 0,
            discoveryBonusLevelCost: 0,
            debugMode: false,
            totalDiscoveredCount: 0,
            poolDiscoveredCount: 3,
            totalDiscoverableCount: 3,
            poolDiscoverableCount: 4,
            hasRequiredDiscovery: true,
            hasRemainingDiscovery: true,
            hasCompletedAllDiscoveries: false);
        var world = new GameWorldState
        {
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[] { tree }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "discovery-trees",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
            }),
            CollectedAtEpoch = 7,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };
        var context = GameMcpTestHarness.Context(world, generation: 81);

        var read = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            context,
            "discovery-trees",
            treeId.ToString("D")));

        Assert.Equal("available", (string?)read["status"]);
        Assert.Null(read["expectedNativeType"]);
        Assert.Equal("idle", (string?)read["row"]!["mode"]);
        Assert.Equal("Glyph Discoveries", (string?)read["name"]);
        Assert.Null(read["row"]!["treeId"]);
        Assert.Null(read["row"]!["debugMode"]);
        Assert.Null(read["row"]!["overrideChoicesId"]);
        var explanation = GameMcpTestHarness.Detail(context, treeId);
        Assert.Null(explanation["status"]);
        Assert.Equal("discovery-trees", (string?)explanation["category"]);
        Assert.Equal("idle", (string?)explanation["row"]!["mode"]);
        Assert.True(GameMcpEntityCapabilityMap.Contains(
            world,
            treeId,
            GameMcpCommandKind.DiscoveryTreeOffer,
            out var reason), reason);
        Assert.True(GameMcpEntityCapabilityMap.Supports(
            "discovery-trees",
            GameMcpCommandKind.DiscoveryTreeOffer));

        // The scan row used to fall through to the reflected projector and publish the raw native
        // actionMode integer, so the same fact had two names and two shapes across two reads.
        var listed = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldList.ListRows(
            context, "discovery-trees", 0, 10).Freeze())["rows"]!.Values<JObject>())!;
        Assert.Equal("idle", (string?)listed["mode"]);
        Assert.Null(listed["actionMode"]);
        Assert.Equal(GameMcpTestHarness.Handle(treeId), (string?)listed["uuid"]);
    }

    [Fact]
    public void IdleTreeReadPublishesExactScalarCostAndOnlyDecisionFields()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var currencyId = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var world = DiscoveryWorld(new WorldDiscoveryTree(
            treeId, true, 0, BigDouble.Zero, 1, false, Guid.Empty,
            Array.Empty<Guid>(), false, true,
            new[]
            {
                new WorldResourceCost(
                    currencyId,
                    new BigDouble(11, 23),
                    new BigDouble(563, 22)),
            },
            Guid.NewGuid(), Guid.NewGuid(), 9, 17, true, 2, 8, 5, 9, true, true, false));

        var response = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(world, generation: 82),
            "discovery-trees",
            treeId.ToString("D")));
        var row = (JObject)response["row"]!;

        Assert.Equal("Glyph Discoveries", (string?)response["name"]);
        Assert.Equal(GameMcpTestHarness.Handle(treeId), (string?)response["uuid"]);
        Assert.Equal("idle", (string?)row["mode"]);
        Assert.True((bool)row["initiate"]!["available"]!);
        Assert.Null(row["initiate"]!["affordable"]);
        var cost = Assert.Single(row["initiate"]!["costs"]!).Value<JObject>()!;
        Assert.Equal(GameMcpTestHarness.Handle(currencyId), (string?)cost["resource"]!["uuid"]);
        Assert.NotNull(cost["resource"]!["name"]);
        Assert.Equal("1.10e24", (string?)cost["cost"]);
        Assert.Equal("5.63e24", (string?)cost["spendableAmount"]);
        Assert.Null(row["reroll"]);
        Assert.DoesNotContain("debugMode", response.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("override", response.ToString(), StringComparison.Ordinal);
        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            response.ToString(Newtonsoft.Json.Formatting.None));
        Assert.True(responseBytes < 650);

        var unaffordableTree = new WorldDiscoveryTree(
            treeId, true, 0, BigDouble.Zero, 1, false, Guid.Empty,
            Array.Empty<Guid>(), false, false,
            new[]
            {
                new WorldResourceCost(
                    currencyId,
                    new BigDouble(11, 23),
                    new BigDouble(1, 2)),
            },
            Guid.Empty, Guid.Empty, 0, 0, false, 2, 8, 5, 9, true, true, false);
        var unaffordable = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(DiscoveryWorld(unaffordableTree), generation: 83),
            "discovery-trees",
            treeId.ToString("D")));
        Assert.False((bool)unaffordable["row"]!["initiate"]!["available"]!);
        Assert.Null(unaffordable["row"]!["initiate"]!["reasonCode"]);
        Assert.Equal("1.10e24",
            (string?)unaffordable["row"]!["initiate"]!["costs"]![0]!["cost"]);
        Assert.Equal("100",
            (string?)unaffordable["row"]!["initiate"]!["costs"]![0]!["spendableAmount"]);

        // The price row reads in the screen's rounding, and a round read `cost: 5 of 5 Knowledge
        // affordable=no` off 4.6 Knowledge and went looking for a suite bug. The sentence carries
        // the same two numbers game_purchase's refusal does, so a near miss reads as a near miss
        // rather than as "The named resources fall short of the price."
        Assert.Equal("Needs 1.10e24 Knowledge (have 100).",
            (string?)unaffordable["row"]!["initiate"]!["reason"]);
    }

    /// <summary>
    /// A tree the game is not showing quotes no price. The row used to print
    /// "The game is not showing this discovery tree." and then <c>400 of 100 Mana affordable=no</c>
    /// in the same block, which sent a caller to go and earn a price for a press that would still
    /// not exist. An unpressable press says why it is unpressable and nothing else.
    /// </summary>
    [Fact]
    public void ATreeTheGameIsNotShowingQuotesNoPrice()
    {
        var treeId = Guid.NewGuid();
        var currencyId = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var hidden = new WorldDiscoveryTree(
            treeId, false, 0, BigDouble.Zero, 1, false, Guid.Empty,
            Array.Empty<Guid>(), false, false,
            new[]
            {
                new WorldResourceCost(
                    currencyId, new BigDouble(4, 2), new BigDouble(1, 2)),
            },
            Guid.Empty, Guid.Empty, 0, 0, false, 2, 8, 5, 9, true, true, false);

        var row = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(DiscoveryWorld(hidden), generation: 84),
            "discovery-trees",
            treeId.ToString("D")))["row"]!;

        Assert.False((bool)row["initiate"]!["available"]!);
        // A tree whose screen is shut is locked, not missing: the row is right here.
        Assert.Null(row["initiate"]!["reasonCode"]);
        Assert.Equal(
            "The game is not showing this discovery tree.",
            (string?)row["initiate"]!["reason"]);
        Assert.Null(row["initiate"]!["costs"]);
    }

    [Fact]
    public void ChoiceTreeOffersAreOrderedResolvableAndExplainableFromOneWorld()
    {
        var treeId = Guid.NewGuid();
        var runeId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var glyphId = Guid.Parse("81894d9f-4e91-43da-9f47-2a97d77a2294");
        var tree = new WorldDiscoveryTree(
            treeId, true, 2, BigDouble.Zero, 1, false, Guid.Empty,
            new[] { runeId, glyphId }, false, false,
            Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
            0, 0, false, 2, 8, 5, 9, true, true, false);
        var world = DiscoveryWorld(
            tree,
            timeRunes: new[]
            {
                new WorldTimeRune(
                    runeId, false, 0, 1, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            },
            glyphs: new[]
            {
                new WorldGlyph(
                    glyphId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            });
        var context = GameMcpTestHarness.Context(world, generation: 83);

        var treeRead = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            context, "discovery-trees", treeId.ToString("D")));
        var offers = treeRead["row"]!["offers"]!.Values<JObject>().ToArray();
        Assert.Equal(
            new[]
            {
                GameMcpTestHarness.Handle(runeId), GameMcpTestHarness.Handle(glyphId),
            },
            offers.Select(offer => (string?)offer!["uuid"]));
        Assert.All(offers, offer => Assert.NotNull(offer!["name"]));
        Assert.True((bool)treeRead["row"]!["reroll"]!["available"]!);

        var runeRead = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            context, "time-runes", runeId.ToString("D")));
        Assert.Equal("available", (string?)runeRead["status"]);
        var explanation = GameMcpTestHarness.Detail(context, runeId);
        Assert.Null(explanation["status"]);
        Assert.Equal("Ability Persist", (string?)explanation["name"]);

        // The offered rune passes every predicate, and says so rather than going silent. Its
        // discovery verdict is the row's alone: `canDiscover` was the same answer a second time.
        Assert.True((bool)explanation["predicates"]!["visible"]!["available"]!);
        Assert.Null(explanation["predicates"]!["canDiscover"]);
        Assert.False((bool)explanation["row"]!["discover"]!["available"]!);
    }

    [Fact]
    public void UnresolvedChoiceIsLocalizedToItsTreeAndFailsClosed()
    {
        var treeId = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var world = DiscoveryWorld(new WorldDiscoveryTree(
            treeId, true, 2, BigDouble.Zero, 1, false, Guid.Empty,
            new[] { missing }, false, false, Array.Empty<WorldResourceCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 0, 1, 3, 2, true, true, false));

        var response = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(world, generation: 84),
            "discovery-trees",
            treeId.ToString("D")));

        Assert.Equal("unavailable", (string?)response["status"]);
        Assert.Equal("ERR_UNAVAILABLE", (string?)response["reasonCode"]);
        var implicated = Assert.Single(response["implicatedOffers"]!).Value<JObject>()!;
        Assert.Equal(GameMcpTestHarness.Handle(treeId), (string?)implicated["tree"]!["uuid"]);
        Assert.Equal(GameMcpTestHarness.Handle(missing), (string?)implicated["offer"]!["uuid"]);
        Assert.Null(implicated["offer"]!["nameEvidence"]);
        Assert.Equal(0, (int)implicated["ordinal"]!);
    }

    [Theory]
    [InlineData(false, 2, false, 1, "The game is not showing this discovery tree.")]
    [InlineData(true, 0, false, 1, null)]
    [InlineData(true, 2, false, 0, "This tree is showing no offers to reroll.")]
    [InlineData(true, 2, false, -1,
        "A reroll was already spent on this discovery, so no further reroll is offered.")]
    [InlineData(true, 2, false, -2, "No rerolls are left this cycle.")]
    public void RerollReadNamesEveryUnavailableState(
        bool visible,
        int mode,
        bool immediateRequired,
        int offerState,
        string? reason)
    {
        var treeId = Guid.NewGuid();
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var offers = offerState == 0 ? Array.Empty<Guid>() : new[] { offerId };
        var rerolls = offerState < 0 ? 0 : 1;
        var used = offerState == -1;
        var tree = new WorldDiscoveryTree(
            treeId, visible, mode, BigDouble.Zero, rerolls, used, Guid.Empty,
            offers, immediateRequired, false, Array.Empty<WorldResourceCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false);
        var world = DiscoveryWorld(
            tree,
            timeRunes: new[]
            {
                new WorldTimeRune(
                    offerId, false, 0, 1, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            });

        var response = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(world, generation: 85),
            "discovery-trees",
            treeId.ToString("D")));

        if (mode == 2)
        {
            var reroll = response["row"]!["reroll"]!;
            Assert.False((bool)reroll["available"]!);
            // Four different kinds of no, four different sentences, and no class on any of them:
            // this is a read of the tree, not the reroll press refusing.
            Assert.Null(reroll["reasonCode"]);
            Assert.Equal(reason, (string?)reroll["reason"]);
        }
        else
        {
            Assert.Null(response["row"]!["reroll"]);
        }
        if (offers.Length == 0) Assert.Null(response["row"]!["offers"]);
    }

    /// <summary>
    /// The game's reroll button asks HasRerolls() and nothing else, and RerollChoices guards choice
    /// mode and the budget. A tree owing a required discovery re-offers that one thing, which is
    /// worth saying beside a live press — it was published as a refusal, and with Confirm refused on
    /// the same tree the suite left no way out of choice mode at all.
    /// </summary>
    [Fact]
    public void A_required_discovery_is_advice_on_the_reroll_rather_than_a_refusal()
    {
        var treeId = Guid.NewGuid();
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var tree = new WorldDiscoveryTree(
            treeId, true, 2, BigDouble.Zero, 1, false, Guid.Empty,
            new[] { offerId }, true, false, Array.Empty<WorldResourceCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false);
        var world = DiscoveryWorld(
            tree,
            timeRunes: new[]
            {
                new WorldTimeRune(
                    offerId, true, 0, 1, BigDouble.Zero, 0, true, true,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            },
            identities: new[]
            {
                new EntityIdentityName(offerId, "TimeRuneSO", "Slow Time", "slowTime"),
            });

        var row = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(world, generation: 85),
            "discovery-trees",
            treeId.ToString("D")))["row"]!;

        Assert.True((bool)row["reroll"]!["available"]!);
        Assert.Null(row["reroll"]!["reasonCode"]);
        Assert.Equal(
            "This tree owes a required discovery, so it is offering that one thing: a reroll " +
            "spends a reroll and offers it again.",
            (string?)row["reroll"]!["note"]);

        // The rigged offer names something already owned: EnterChoiceMode hands back the required
        // discovery without the IsDiscovered filter the pool branch applies.
        var offer = Assert.Single(row["offers"]!.Values<JObject>())!;
        Assert.True((bool)offer["discovered"]!);
    }

    /// <summary>
    /// A press starts a roll; the game rolls the offers three seconds later. The empty offer list in
    /// between read as a press that had done nothing.
    /// </summary>
    [Fact]
    public void A_rolling_tree_says_the_roll_is_running_and_when_its_offers_appear()
    {
        var treeId = Guid.NewGuid();
        var tree = new WorldDiscoveryTree(
            treeId, true, 1, new BigDouble(0.1d), 1, false, Guid.Empty,
            Array.Empty<Guid>(), false, false, Array.Empty<WorldResourceCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, false, true, false);

        var row = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(DiscoveryWorld(tree), generation: 85),
            "discovery-trees",
            treeId.ToString("D")))["row"]!;

        Assert.Equal("crafting", (string?)row["mode"]);
        Assert.Null(row["offers"]);
        Assert.Equal(
            "This roll is still running. The game rolls its offers three seconds after the " +
            "press, and this tree has been rolling 0.10s.",
            (string?)row["rolling"]);
    }

    /// <summary>
    /// "This tree has nothing left to discover." stood on a row printing 12 discovered of 65. The
    /// game folds two conditions into one flag — the whole tree finished, and a tree whose pool has
    /// nothing in reach — and only the first makes that sentence true.
    /// </summary>
    [Fact]
    public void ATreeWithNothingInReachIsNotATreeThatIsFinished()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");

        var blocked = Initiate(treeId, discovered: 12, discoverable: 65);
        Assert.Null(blocked["reasonCode"]);
        Assert.Equal(
            "Nothing in this tree can be discovered right now: 12 of its 65 are discovered, and " +
            "none of the other 53 is in reach — a tree's pool is widened by its Recipe Books, and " +
            "an item in the pool is offered only once the game shows it.",
            (string?)blocked["reason"]);

        var finished = Initiate(treeId, discovered: 65, discoverable: 65);
        Assert.Null(finished["reasonCode"]);
        Assert.Equal(
            "Every one of this tree's 65 discoveries is made.", (string?)finished["reason"]);
    }

    private static JObject Initiate(Guid treeId, int discovered, int discoverable) =>
        (JObject)GameMcpTestHarness.Json(GameMcpWorldDetail.GetRow(
            GameMcpTestHarness.Context(
                DiscoveryWorld(new WorldDiscoveryTree(
                    treeId, true, 0, BigDouble.Zero, 1, false, Guid.Empty,
                    Array.Empty<Guid>(), false, false,
                    Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, discovered, discovered, discoverable, discoverable,
                    false, false, discovered >= discoverable)),
                generation: 84),
            "discovery-trees",
            treeId.ToString("D")))["row"]!["initiate"]!;

    /// <summary>
    /// The game marks a discovery page's tab while its tree owes a required discovery, and that
    /// roll is priced in the required item's own resources. A live round hunted a Skill source for
    /// several reads while the mark on Magic &gt; Spellbook &gt; Unlock held the answer, because no
    /// world read carried the mark.
    /// </summary>
    [Fact]
    public void TheOverviewNamesEveryMarkedRequiredRollWithItsScreenAndPrice()
    {
        var spellTree = Guid.Parse("5ba0b305-21bd-4b43-af88-cc763ac04df8");
        var glyphTree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var conceptTree = Guid.Parse("3444dae4-9323-4e4e-8a0a-ae800da15ab8");
        var verdantEnergy = Guid.Parse("fca918e7-ba2a-4659-a2fb-d5f9371ee37f");
        WorldDiscoveryTree Idle(Guid id, bool required) => new(
            id, true, 0, BigDouble.Zero, 1, false, Guid.Empty, Array.Empty<Guid>(),
            required, false,
            new[] { new WorldResourceCost(verdantEnergy, new BigDouble(12, 2), new BigDouble(5, 2)) },
            Guid.Empty, Guid.Empty, 0, 0, false, 2, 8, 5, 9, required, true, false);
        var world = new GameWorldState
        {
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
            {
                Idle(spellTree, required: true),
                Idle(glyphTree, required: false),
                Idle(conceptTree, required: true),
            }),
            Views = WorldTable.Create(new[]
            {
                new WorldView(KnownEntities.MagicSpellbookLearn.Uuid, false, false, true, spellTree),
                new WorldView(KnownEntities.MagicGlyphsDiscover.Uuid, false, false, true, glyphTree),
                new WorldView(
                    KnownEntities.ScholarConceptDiscover.Uuid, false, false, false, conceptTree),
            }),
            EntityIdentities = EntityIdentityCatalogSnapshot.Unbound(1),
            CollectedAtEpoch = 7,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

        var overview = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(
            GameMcpTestHarness.Context(world, generation: 90)));

        var roll = Assert.Single(overview["requiredRolls"]!.Values<JObject>())!;
        Assert.Equal(GameMcpTestHarness.Handle(spellTree), (string?)roll["tree"]!["uuid"]);
        Assert.Equal("Magic > Spellbook > Unlock", (string?)roll["screen"]);
        var cost = Assert.Single(roll["costs"]!.Values<JObject>())!;
        Assert.Equal("Verdant Energy", (string?)cost["resource"]!["name"]);
        Assert.Equal("1.20e3", (string?)cost["cost"]);
        Assert.False((bool)cost["affordable"]!);
        Assert.Contains(
            "cost: 1.20e3 of 500 Verdant Energy",
            GameMcpTextPage.Render(overview),
            StringComparison.Ordinal);

        var quiet = GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(GameMcpTestHarness.Context(
            new GameWorldState
            {
                DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
                    { Idle(glyphTree, required: false) }),
                Views = world.Views,
                EntityIdentities = EntityIdentityCatalogSnapshot.Unbound(1),
                CollectedAtEpoch = 7,
                CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            },
            generation: 91)));
        Assert.Null(quiet["requiredRolls"]);
    }

    /// <summary>
    /// The game draws a tab, and the mark on it, only while that tab's view is available, so a
    /// discovery page under a locked screen carries no mark. A live round read a required roll on
    /// Alchemy &gt; Alchemy &gt; Learn while Alchemy itself answered that it is not unlocked yet.
    /// </summary>
    [Fact]
    public void ARequiredRollUnderALockedScreenIsNotListed()
    {
        var alchemyTree = Guid.NewGuid();
        var alchemyTab = Guid.NewGuid();
        var control = Guid.NewGuid();
        var tree = new WorldDiscoveryTree(
            alchemyTree, true, 0, BigDouble.Zero, 1, false, Guid.Empty, Array.Empty<Guid>(),
            true, true,
            new[] { new WorldResourceCost(control, new BigDouble(1, 2), new BigDouble(5, 2)) },
            Guid.Empty, Guid.Empty, 0, 0, false, 2, 8, 5, 9, true, true, false);
        JObject Overview(bool screenAvailable) => GameMcpTestHarness.Json(GameMcpWorldOverview.Overview(
            GameMcpTestHarness.Context(
                new GameWorldState
                {
                    DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[] { tree }),
                    Views = WorldTable.Create(new[]
                    {
                        new WorldView(
                            KnownEntities.AlchemyScreen.Uuid, false, false, screenAvailable,
                            subViewIds: new[] { alchemyTab }),
                        new WorldView(
                            alchemyTab, false, false, true,
                            subViewIds: new[] { KnownEntities.AlchAlchemyDiscover.Uuid }),
                        new WorldView(
                            KnownEntities.AlchAlchemyDiscover.Uuid, false, false, true, alchemyTree),
                    }),
                    EntityIdentities = EntityIdentityCatalogSnapshot.Unbound(1),
                    CollectedAtEpoch = 7,
                    CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
                },
                generation: screenAvailable ? 93UL : 92UL)));

        Assert.Null(Overview(screenAvailable: false)["requiredRolls"]);
        var roll = Assert.Single(Overview(screenAvailable: true)["requiredRolls"]!.Values<JObject>())!;
        Assert.Equal("Alchemy > Alchemy > Learn", (string?)roll["screen"]);
    }
}

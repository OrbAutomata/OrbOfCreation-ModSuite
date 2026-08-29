using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpDiscoveryTreeOfferTests
{
    [Fact]
    public void EventOfferLifecycleIsFoldedIntoTheOneDiscoveryNamespace()
    {
        Assert.DoesNotContain(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discovery_offer");
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode", "uuid" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.NotNull(schema["properties"]!["offerUuid"]);
        Assert.Null(schema["properties"]!["worldGeneration"]);
        Assert.Null(schema["properties"]!["expectedNativeType"]);
        Assert.Null(schema["properties"]!["verbosity"]);
        Assert.Null(schema["properties"]!["detail"]);
    }

    [Fact]
    public void Conditional_offer_identity_errors_are_structured()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "game_discover",
                ["arguments"] = new JObject
                {
                    ["mode"] = "offer_select",
                    ["uuid"] = Guid.NewGuid().ToString("D"),
                },
            }));

        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: required " +
            "field 'offerUuid' is missing for mode 'offer_select'",
            GameMcpTestHarness.Page(response));
    }

    [Fact]
    public void Emergency_stop_closes_discovery_offer_admission_as_gameplay()
    {
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.DiscoveryTreeOffer,
            expectedLifecycleGeneration: 9,
            expectedConfigurationGeneration: 1,
            mode: "reroll",
            Guid.NewGuid(),
            Guid.Empty,
            "DiscoveryTreeSO", 1,
            string.Empty,
            string.Empty,
            false);

        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            currentLifecycleGeneration: 9,
            currentConfigurationGeneration: 1,
            emergencyStopEngaged: true,
            out var terminal));
        Assert.Equal("emergency_stop", terminal.Code);
    }

    [Fact]
    public void Preflight_refusal_without_native_evidence_omits_the_receipt_body()
    {
        var submission = DiscoveryTreeOfferSubmission.Reject(
            DiscoveryTreeOfferPreflight.TreeUnavailable,
            "the exact tree is unavailable");

        var projected = GameMcpTestHarness.Json(
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Initiate,
                in submission));

        Assert.Empty(projected.Properties());
        Assert.Null(projected["receipt"]);
        Assert.Null(projected["nativeStage"]);
        Assert.Null(projected["outcome"]);
        Assert.Null(projected["quarantined"]);
    }

    /// <remarks>
    /// A precondition list written by hand drifts from the code that enforces it; the number the
    /// refusal actually read cannot.
    /// </remarks>
    [Fact]
    public void A_spent_reroll_budget_is_refused_with_the_budget_it_read()
    {
        var submission = DiscoveryTreeOfferSubmission.RerollsExhausted(
            "No rerolls are left on this tree.", 0);

        var projected = GameMcpTestHarness.Json(
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Reroll,
                in submission));

        Assert.Equal(0, (int?)projected["rerollsLeft"]);
        Assert.Single(projected.Properties());
    }

    [Fact]
    public void FaultNamesOnlyTheMissingOutcome()
    {
        var submission = new DiscoveryTreeOfferSubmission(
            DiscoveryTreeOfferPreflight.VerificationFailed,
            DiscoveryTreeOfferNativeStage.Verification,
            NativeMutationOutcome.PostconditionFailed,
            new NativeMutationCallOutcome(1, 1, 0),
            "the requested transition was not observed");

        var projected = GameMcpTestHarness.Json(
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Initiate,
                in submission));

        Assert.Equal("crafting mode", (string?)projected["missingOutcome"]);
        Assert.Single(projected.Properties());
    }

    [Theory]
    [InlineData("initiate")]
    [InlineData("reroll")]
    public void InitiateAndRerollSettleOnTheCraftTheyStartNotTheOffersItProduces(string mode)
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var completedAt = DateTime.UtcNow.Ticks;
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, mode, treeId, Guid.Empty,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0), generation: 41));

        // Native's InitiateCraftingMode and RerollChoices both end in EnterCraftingMode; the offers
        // only land three seconds later, well past the settle budget, so waiting on them reported a
        // timeout for every mutation that in fact committed.
        var crafting = GameMcpTestHarness.Context(
            Tree(treeId, actionMode: 1, collectedAtUtcTicks: completedAt + 1), generation: 42);

        Assert.True(GameMcpPostStateSettlement.IsReady(crafting, 41, completedAt, command));
        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, collectedAtUtcTicks: completedAt + 1),
                generation: 42),
            41, completedAt, command));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectPostState(
            crafting, "discovery-trees", treeId));
        Assert.Equal("crafting", (string?)response["mode"]);
        Assert.Null(response["offers"]);
    }

    /// <remarks>
    /// A selection changes which offer is held, not what is offered, and the caller picked from the
    /// list it is being handed back. The settled tree still names the selection and the budget.
    /// </remarks>
    [Fact]
    public void Selecting_an_offer_does_not_re_send_the_list_it_was_picked_from()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var selected = new WorldDiscoveryTree(
            treeId, true, 2, BigDouble.Zero, 1, false, offerId,
            new[] { offerId }, false, true, Array.Empty<WorldDiscoveryTreeCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false);
        var world = DiscoveryWorld(
            selected,
            timeRunes: new[]
            {
                new WorldTimeRune(
                    offerId, false, 0, 1, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            });
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "select", treeId, offerId,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false);

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(world, generation: 806),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Null(delta["offers"]);
        Assert.Equal(GameMcpTestHarness.Handle(offerId), (string?)delta["selectedOffer"]!["uuid"]);
        Assert.Equal(1, (int)delta["rerollsLeft"]!);
    }

    /// <summary>
    /// The press permanently spends a discovery choice. It used to answer with a bare count and
    /// nothing naming what was taken, so a caller could not confirm the discovery landed and read
    /// the tree again to learn what it had got — with an identity the tool held in its own request.
    /// </summary>
    [Fact]
    public void Confirming_an_offer_names_what_was_discovered_and_moves_the_count_as_a_pair()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "offer_confirm", treeId, offerId,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2), generation: 41));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, discoveredCount: 1), generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(GameMcpTestHarness.Handle(offerId), (string?)delta["discovered"]!["uuid"]);
        Assert.Equal(0, (int)delta["discoveredCount"]!["before"]!);
        Assert.Equal(1, (int)delta["discoveredCount"]!["after"]!);
        // A count that moved says nothing about how much of the tree is left, which is the question
        // a caller confirmed an offer to make progress on.
        Assert.Equal(4, (int)delta["discoverableCount"]!);
        Assert.Equal("choice", (string?)delta["mode"]!["before"]);
        Assert.Equal("idle", (string?)delta["mode"]!["after"]);
        Assert.True((bool)delta["hasRemainingDiscoveries"]!);
    }

    /// <summary>
    /// A spent reroll answers the way the challenge reroll does: the budget as a pair, whether the
    /// offers actually moved, and the offers themselves. A bare post-value with no offers said
    /// nothing about what the press bought, so the caller re-read the whole tree to find out.
    /// </summary>
    [Fact]
    public void A_spent_reroll_pairs_the_budget_and_names_the_offers_it_bought()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var first = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var second = Guid.Parse("b1d6b0b6-98c1-4b74-90a4-7d0f7dbd3a1f");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "offer_reroll", treeId, Guid.Empty,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2, rerollsLeft: 2, offers: new[] { first }),
                generation: 61));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2, rerollsLeft: 1, offers: new[] { second }),
                generation: 62),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(2, (int)delta["rerollsLeft"]!["before"]!);
        Assert.Equal(1, (int)delta["rerollsLeft"]!["after"]!);
        Assert.True((bool)delta["changed"]!);
        Assert.Equal(
            GameMcpTestHarness.Handle(second),
            (string?)Assert.Single(delta["offers"]!)["uuid"]);
    }

    /// <summary>
    /// A reroll that spent the budget and put the same offers back says so, rather than leaving a
    /// caller to diff two tree reads.
    /// </summary>
    [Fact]
    public void A_reroll_that_moved_nothing_says_the_offers_did_not_change()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offer = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "offer_reroll", treeId, Guid.Empty,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2, rerollsLeft: 1, offers: new[] { offer }),
                generation: 63));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2, rerollsLeft: 0, offers: new[] { offer }),
                generation: 64),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(0, (int)delta["rerollsLeft"]!["after"]!);
        Assert.False((bool)delta["changed"]!);
    }

    private static GameWorldState Tree(
        Guid treeId,
        int actionMode,
        long collectedAtUtcTicks = 0,
        int discoveredCount = 0,
        int rerollsLeft = 2,
        Guid[]? offers = null) => new()
        {
            CollectedAtEpoch = 7,
            CollectedAtUtcTicks = collectedAtUtcTicks,
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
            {
                new WorldDiscoveryTree(
                    treeId, true, actionMode, BigDouble.Zero, rerollsLeft, false, Guid.Empty,
                    offers ?? Array.Empty<Guid>(), false, true,
                    Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, discoveredCount, 3, discoveredCount + 3, 4, true, true, false),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "discovery-trees", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };

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
            nextItemCosts: Array.Empty<WorldDiscoveryTreeCost>(),
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

        var read = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context,
            "discovery-trees",
            treeId.ToString("D")));

        Assert.Equal("available", (string?)read["status"]);
        Assert.Null(read["expectedNativeType"]);
        Assert.Equal("idle", (string?)read["row"]!["mode"]);
        Assert.Equal("Glyph Discoveries", (string?)read["row"]!["name"]);
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
        var listed = Assert.Single(GameMcpTestHarness.Json(GameMcpWorldQuery.ListRows(
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
                new WorldDiscoveryTreeCost(
                    currencyId,
                    new BigDouble(11, 23),
                    new BigDouble(563, 22)),
            },
            Guid.NewGuid(), Guid.NewGuid(), 9, 17, true, 2, 8, 5, 9, true, true, false));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 82),
            "discovery-trees",
            treeId.ToString("D")));
        var row = (JObject)response["row"]!;

        Assert.Equal("Glyph Discoveries", (string?)row["name"]);
        Assert.Equal(GameMcpTestHarness.Handle(treeId), (string?)row["uuid"]);
        Assert.Equal("idle", (string?)row["mode"]);
        Assert.True((bool)row["initiate"]!["available"]!);
        Assert.Null(row["initiate"]!["affordable"]);
        var cost = Assert.Single(row["initiate"]!["costs"]!).Value<JObject>()!;
        Assert.Equal(GameMcpTestHarness.Handle(currencyId), (string?)cost["resource"]!["uuid"]);
        Assert.NotNull(cost["resource"]!["name"]);
        Assert.Equal("1.1e24", (string?)cost["cost"]);
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
                new WorldDiscoveryTreeCost(
                    currencyId,
                    new BigDouble(11, 23),
                    new BigDouble(1, 2)),
            },
            Guid.Empty, Guid.Empty, 0, 0, false, 2, 8, 5, 9, true, true, false);
        var unaffordable = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(DiscoveryWorld(unaffordableTree), generation: 83),
            "discovery-trees",
            treeId.ToString("D")));
        Assert.False((bool)unaffordable["row"]!["initiate"]!["available"]!);
        Assert.Equal("ERR_UNAFFORDABLE", (string?)unaffordable["row"]!["initiate"]!["reasonCode"]);
        Assert.Equal("1.1e24",
            (string?)unaffordable["row"]!["initiate"]!["costs"]![0]!["cost"]);
        Assert.Equal("100",
            (string?)unaffordable["row"]!["initiate"]!["costs"]![0]!["spendableAmount"]);
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
                new WorldDiscoveryTreeCost(
                    currencyId, new BigDouble(4, 2), new BigDouble(1, 2)),
            },
            Guid.Empty, Guid.Empty, 0, 0, false, 2, 8, 5, 9, true, true, false);

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(DiscoveryWorld(hidden), generation: 84),
            "discovery-trees",
            treeId.ToString("D")))["row"]!;

        Assert.False((bool)row["initiate"]!["available"]!);
        // A tree whose screen is shut is locked, not missing: the row is right here.
        Assert.Equal("ERR_LOCKED", (string?)row["initiate"]!["reasonCode"]);
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
            Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
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

        var treeRead = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
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

        var runeRead = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            context, "time-runes", runeId.ToString("D")));
        Assert.Equal("available", (string?)runeRead["status"]);
        var explanation = GameMcpTestHarness.Detail(context, runeId);
        Assert.Null(explanation["status"]);
        Assert.Equal("Ability Persist", (string?)explanation["name"]);

        // The offered rune passes every predicate, and says so rather than going silent.
        Assert.True((bool)explanation["predicates"]!["visible"]!["available"]!);
        Assert.True((bool)explanation["predicates"]!["canDiscover"]!["available"]!);
    }

    [Fact]
    public void UnresolvedChoiceIsLocalizedToItsTreeAndFailsClosed()
    {
        var treeId = Guid.NewGuid();
        var missing = Guid.NewGuid();
        var world = DiscoveryWorld(new WorldDiscoveryTree(
            treeId, true, 2, BigDouble.Zero, 1, false, Guid.Empty,
            new[] { missing }, false, false, Array.Empty<WorldDiscoveryTreeCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 0, 1, 3, 2, true, true, false));

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
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
    [InlineData(false, 2, false, 1, "ERR_LOCKED", "The game is not showing this discovery tree.")]
    [InlineData(true, 0, false, 1, null, null)]
    [InlineData(true, 2, true, 1, "ERR_STATE",
        "This tree has a discovery to take first, so its offers cannot be rerolled.")]
    [InlineData(true, 2, false, 0, "ERR_NOT_FOUND", "This tree is showing no offers to reroll.")]
    [InlineData(true, 2, false, -1, "ERR_STATE",
        "A reroll was already spent on this discovery, so no further reroll is offered.")]
    [InlineData(true, 2, false, -2, "ERR_LIMIT", "No rerolls are left this cycle.")]
    public void RerollReadNamesEveryUnavailableState(
        bool visible,
        int mode,
        bool immediateRequired,
        int offerState,
        string? reasonCode,
        string? reason)
    {
        var treeId = Guid.NewGuid();
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var offers = offerState == 0 ? Array.Empty<Guid>() : new[] { offerId };
        var rerolls = offerState < 0 ? 0 : 1;
        var used = offerState == -1;
        var tree = new WorldDiscoveryTree(
            treeId, visible, mode, BigDouble.Zero, rerolls, used, Guid.Empty,
            offers, immediateRequired, false, Array.Empty<WorldDiscoveryTreeCost>(),
            Guid.Empty, Guid.Empty, 0, 0, false, 1, 1, 4, 2, true, true, false);
        var world = DiscoveryWorld(
            tree,
            timeRunes: new[]
            {
                new WorldTimeRune(
                    offerId, false, 0, 1, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            });

        var response = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 85),
            "discovery-trees",
            treeId.ToString("D")));

        if (mode == 2)
        {
            var reroll = response["row"]!["reroll"]!;
            Assert.False((bool)reroll["available"]!);
            Assert.Equal(reasonCode, (string?)reroll["reasonCode"]);
            Assert.Equal(reason, (string?)reroll["reason"]);
        }
        else
        {
            Assert.Null(response["row"]!["reroll"]);
        }
        if (offers.Length == 0) Assert.Null(response["row"]!["offers"]);
    }

    [Fact]
    public void McpPureJourneyNeedsNoNameJoinsOrPostMutationReadBacks()
    {
        DiscoveryTreeSO.All.Clear();
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            visible = true,
            rerollsLeft = 1,
        };
        tree.SetGuid(Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8"));
        var firstId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var secondId = Guid.Parse("81894d9f-4e91-43da-9f47-2a97d77a2294");
        var firstNative = new DiscoveryTestItemSO();
        firstNative.SetGuid(firstId);
        var secondNative = new DiscoveryTestItemSO();
        secondNative.SetGuid(secondId);
        tree.allDiscoverableItems.Add(firstNative);
        tree.allDiscoverableItems.Add(secondNative);
        DiscoveryTreeSO.All.Add(tree);
        const long lifecycle = 7;
        var calls = 0;
        var postMutationReadBacks = 0;
        var nameJoins = 0;

        try
        {
            using var action = new DiscoveryTreeOfferGameAction(
                () => lifecycle,
                () => true,
                () => string.Empty);

            var idle = DiscoveryWorld(new WorldDiscoveryTree(
                tree.GetGuid(), true, 0, BigDouble.Zero, 1, false, Guid.Empty,
                Array.Empty<Guid>(), false, true, Array.Empty<WorldDiscoveryTreeCost>(),
                Guid.Empty, Guid.Empty, 0, 0, false, 0, 2, 3, 3, true, true, false));
            var idleRead = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
                GameMcpTestHarness.Context(idle, generation: 90),
                "discovery-trees", tree.GetGuid().ToString("D")));
            calls++;
            Assert.True((bool)idleRead["row"]!["initiate"]!["available"]!);
            Assert.NotNull(idleRead["row"]!["name"]);
            var readTreeId = GameMcpTestHarness.ResolveHandle((string)idleRead["row"]!["uuid"]!);

            var initiated = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Initiate,
                readTreeId,
                Guid.Empty,
                lifecycle));
            calls++;
            Assert.True(initiated.Verified, initiated.Reason);

            // The native update loop materializes offers after Crafting. The fixture advances that
            // external transition, then every player decision below comes back from MCP reads.
            tree.actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Choice;
            tree.currentChoiceIds.Add(new GuidContainer(firstId));
            tree.currentChoiceIds.Add(new GuidContainer(secondId));
            var timeRuneRows = new[]
            {
                new WorldTimeRune(
                    firstId, false, 0, 1, BigDouble.Zero, 0, false, false,
                    BigDouble.Zero, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            };
            var glyphRows = new[]
            {
                new WorldGlyph(
                    secondId, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            };
            var choice = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 2, BigDouble.Zero, tree.rerollsLeft, false,
                    Guid.Empty, new[] { firstId, secondId }, false, false,
                    Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, 0, 2, 3, 3, true, true, false),
                timeRunes: timeRuneRows,
                glyphs: glyphRows);
            var choiceContext = GameMcpTestHarness.Context(choice, generation: 91);
            var initiatedResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    choiceContext, "discovery-trees", readTreeId));
            var readOffers = initiatedResponse["offers"]!
                .Values<JObject>()
                .Select(offer => GameMcpTestHarness.ResolveHandle((string)offer!["uuid"]!))
                .ToArray();
            Assert.Equal(new[] { firstId, secondId }, readOffers);
            Assert.All(initiatedResponse["offers"]!.Values<JObject>(), offer =>
                Assert.NotNull(offer!["name"]));

            var rerolled = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Reroll,
                readTreeId,
                Guid.Empty,
                lifecycle));
            calls++;
            Assert.True(rerolled.Verified, rerolled.Reason);
            Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);

            tree.actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Choice;
            tree.currentChoiceIds.Add(new GuidContainer(firstId));
            tree.currentChoiceIds.Add(new GuidContainer(secondId));
            var rerollWorld = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 2, BigDouble.Zero, tree.rerollsLeft, false,
                    Guid.Empty, new[] { firstId, secondId }, false, false,
                    Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, 1, 2, 4, 3, true, true, false),
                timeRunes: timeRuneRows,
                glyphs: glyphRows);
            var rerollContext = GameMcpTestHarness.Context(rerollWorld, generation: 92);
            var rerollResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    rerollContext, "discovery-trees", readTreeId));
            Assert.Equal("choice", (string?)rerollResponse["mode"]);
            Assert.False((bool)rerollResponse["reroll"]!["available"]!);

            foreach (var readOffer in readOffers)
            {
                var explanation = GameMcpTestHarness.Detail(rerollContext, readOffer);
                calls++;
                Assert.Null(explanation["status"]);
                Assert.NotNull(explanation["name"]);
                Assert.Equal(
                    readOffer == secondId,
                    !(bool)explanation["predicates"]!["available"]!["available"]!);
                if (readOffer == secondId)
                    Assert.Equal("ERR_LOCKED", (string?)explanation["predicates"]!["available"]!["reasonCode"]);
            }

            var selected = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Select,
                readTreeId,
                readOffers[0],
                lifecycle));
            calls++;
            Assert.True(selected.Verified, selected.Reason);
            var selectedWorld = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 2, BigDouble.Zero, tree.rerollsLeft, false,
                    tree.selectedChoiceId.guid, readOffers, false, false,
                    Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, 0, 2, 3, 3, true, true, false),
                timeRunes: timeRuneRows,
                glyphs: glyphRows);
            var selectedResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    GameMcpTestHarness.Context(selectedWorld, generation: 93),
                    "discovery-trees",
                    readTreeId));
            var selectedOffer = GameMcpTestHarness.ResolveHandle(
                (string)selectedResponse["selectedOffer"]!["uuid"]!);
            Assert.Equal(firstId.ToString("D"), selectedOffer.ToString("D"));

            // One entity spells its identity one way: the reference under selectedOffer says the
            // same handle and the same name the offer's own row says.
            var selectedRow = selectedResponse["offers"]!.Values<JObject>()
                .Single(offer =>
                    (string?)offer!["uuid"] == GameMcpTestHarness.Handle(firstId))!;
            Assert.NotNull(selectedRow["name"]);
            Assert.Equal(
                (string?)selectedRow["name"],
                (string?)selectedResponse["selectedOffer"]!["name"]);

            var confirmed = action.Submit(new DiscoveryTreeOfferAction(
                DiscoveryTreeOfferActionKind.Confirm,
                readTreeId,
                selectedOffer,
                lifecycle));
            calls++;
            Assert.True(confirmed.Verified, confirmed.Reason);

            var nextCostResource = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
            var confirmedWorld = DiscoveryWorld(
                new WorldDiscoveryTree(
                    tree.GetGuid(), true, 0, BigDouble.Zero, tree.rerollsLeft, false,
                    Guid.Empty, Array.Empty<Guid>(), false, true,
                    new[]
                    {
                        new WorldDiscoveryTreeCost(
                            nextCostResource,
                            new BigDouble(1.4d, 4),
                            new BigDouble(2d, 25)),
                    },
                    Guid.Empty, Guid.Empty, 0, 0, false, 1, 2, 4, 3, true, true, false));
            var confirmResponse = GameMcpTestHarness.Json(
                GameMcpWorldQuery.ProjectPostState(
                    GameMcpTestHarness.Context(confirmedWorld, generation: 94),
                    "discovery-trees",
                    readTreeId));
            Assert.Equal("idle", (string?)confirmResponse["mode"]);
            Assert.True((bool)confirmResponse["initiate"]!["available"]!);
            var nextCost = Assert.Single(confirmResponse["initiate"]!["costs"]!.Values<JObject>())!;
            Assert.Equal("Knowledge", (string?)nextCost["resource"]!["name"]);
            Assert.Equal("1.4e4", (string?)nextCost["cost"]);
            Assert.Equal("2e25", (string?)nextCost["spendableAmount"]);
            Assert.True((bool)nextCost["affordable"]!);

            static int CommittedBytes(JObject postState)
            {
                var response = (JObject)postState.DeepClone();
                response.AddFirst(new JProperty("status", "committed"));
                return System.Text.Encoding.UTF8.GetByteCount(
                    response.ToString(Newtonsoft.Json.Formatting.None));
            }
            Assert.Equal(
                new[] { 434, 493, 345 },
                new[]
                {
                    CommittedBytes(rerollResponse),
                    CommittedBytes(selectedResponse),
                    CommittedBytes(confirmResponse),
                });

            Assert.Equal(7, calls);
            Assert.Equal(0, postMutationReadBacks);
            Assert.Equal(0, nameJoins);
        }
        finally
        {
            DiscoveryTreeSO.All.Clear();
        }
    }

    [Fact]
    public void Localhost_mcp_owns_offer_family_only_during_one_operation()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var configuration = BepInExAutomataConfiguration.Bind(new ConfigFile()).Current;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.Refresh(configuration, lifecycleReady: true);
        Assert.False(ownership.OwnsDiscoveryTreeOffers);

        Assert.True(ownership.TryBeginGameMcpOperation(
            GameMcpCommandKind.DiscoveryTreeOffer,
            "reroll",
            out var scope,
            out var reason), reason);
        using (scope)
        {
            Assert.True(ownership.OwnsDiscoveryTreeOffers);
            Assert.True(ownership.TryCaptureDiscoveryTreeOfferMutationPermit());
        }
        Assert.False(ownership.OwnsDiscoveryTreeOffers);
        Assert.False(ownership.TryCaptureDiscoveryTreeOfferMutationPermit());
    }

    [Fact]
    public void CommittedConfirmReturnsIdleTreeWithNextDecisionFacts()
    {
        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offer = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var resource = Guid.Parse("eda26ca0-afcc-4fc3-9d8a-eb279123353d");
        var submission = new DiscoveryTreeOfferSubmission(
            DiscoveryTreeOfferPreflight.Proceeded,
            DiscoveryTreeOfferNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1),
            "verified confirm");
        var mapped = DiscoveryTreeOfferActionResultMapper.Map(in submission);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "confirm",
            tree, offer, "DiscoveryTreeSO", 1,
            string.Empty, string.Empty, false);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Confirm,
                in submission));
        var postState = DiscoveryWorld(new WorldDiscoveryTree(
            tree, true, 0, BigDouble.Zero, 0, false, Guid.Empty,
            Array.Empty<Guid>(), false, true,
            new[]
            {
                new WorldDiscoveryTreeCost(
                    resource, new BigDouble(7.5d, 6), new BigDouble(2.43d, 25)),
            },
            Guid.Empty, Guid.Empty, 0, 0, false, 5, 4, 8, 5, false, true, false));
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(postState, generation: 95),
            "discovery-trees",
            tree));

        var projected = GameMcpTestHarness.Json(terminal.Project(command));
        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            projected.ToString(Newtonsoft.Json.Formatting.None));
        Assert.True(responseBytes < 700);
        Assert.True(responseBytes < 1719);
        Assert.Equal(new[]
            {
                "status", "uuid", "name", "category",
                "mode", "rerollsLeft", "discoveredCount", "discoverableCount",
                "hasRemainingDiscoveries", "initiate",
            },
            projected.Properties().Select(property => property.Name));
        Assert.Null(projected["code"]);
        Assert.Equal("idle", (string?)projected["mode"]);
        Assert.Equal(5, (int)projected["discoveredCount"]!);
        Assert.True((bool)projected["initiate"]!["available"]!);
        var cost = Assert.Single(projected["initiate"]!["costs"]!.Values<JObject>())!;
        Assert.Equal("Knowledge", (string?)cost["resource"]!["name"]);
        Assert.Equal("7.5e6", (string?)cost["cost"]);
        Assert.Equal("2.43e25", (string?)cost["spendableAmount"]);
        Assert.Null(projected["discovered"]);
        Assert.Null(projected["totalDiscovered"]);
        Assert.Null(projected["mutationScope"]);
        Assert.Null(projected["receiptId"]);
    }

    [Fact]
    public void CommittedInitiateReturnsNamedOrderedOffersAndErasesPaymentCeremony()
    {
        var tree = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var submission = new DiscoveryTreeOfferSubmission(
            DiscoveryTreeOfferPreflight.Proceeded,
            DiscoveryTreeOfferNativeStage.Verification,
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(2, 2, 2),
            "verified initiate");
        var mapped = DiscoveryTreeOfferActionResultMapper.Map(in submission);
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "initiate",
            tree, Guid.Empty, "DiscoveryTreeSO", 1,
            string.Empty, string.Empty, false);
        var terminal = GameMcpCommandResult.FromAction(
            in mapped,
            command.Kind,
            9,
            3,
            submission.Reason,
            GameMcpDiscoveryTreeOfferProjection.Project(
                DiscoveryTreeOfferActionKind.Initiate,
                in submission));
        var firstOffer = Guid.Parse("168e3734-1ecb-4938-bd4a-d011ff13e201");
        var secondOffer = Guid.Parse("b0387ddd-2bd8-4799-8cd0-f8c624458930");
        var postState = DiscoveryWorld(
            new WorldDiscoveryTree(
                tree, true, 2, BigDouble.Zero, 1, false, Guid.Empty,
                new[] { firstOffer, secondOffer }, false, false,
                Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
                0, 0, false, 2, 2, 5, 3, false, true, false),
            glyphs: new[]
            {
                new WorldGlyph(
                    firstOffer, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
                new WorldGlyph(
                    secondOffer, 0, 0, 1, false, true, false, false, false, false,
                    0, BigDouble.Zero, BigDouble.Zero, BigDouble.Zero),
            });
        terminal = terminal.WithDetails(GameMcpWorldQuery.ProjectPostState(
            GameMcpTestHarness.Context(postState, generation: 96),
            "discovery-trees",
            tree));

        var projected = GameMcpTestHarness.Json(terminal.Project(command));
        var responseBytes = System.Text.Encoding.UTF8.GetByteCount(
            projected.ToString(Newtonsoft.Json.Formatting.None));

        Assert.True(responseBytes < 1721);
        Assert.True(responseBytes < 4096);
        Assert.Equal(new[]
            {
                "status", "uuid", "name", "category",
                "mode", "rerollsLeft", "discoveredCount", "discoverableCount",
                "hasRemainingDiscoveries", "offers", "reroll",
            },
            projected.Properties().Select(property => property.Name));
        Assert.Null(projected["code"]);
        Assert.Equal("choice", (string?)projected["mode"]);
        Assert.Equal(
            new[] { "Weak", "Magnified" },
            projected["offers"]!.Values<JObject>().Select(offer => (string?)offer!["name"]));
        Assert.True((bool)projected["reroll"]!["available"]!);
        Assert.Null(projected["payment"]);
        Assert.Null(projected["reason"]);
        Assert.Null(projected["nativeCallsAttempted"]);
        Assert.Null(projected["mutationAttempts"]);
        Assert.Null(projected["mutationsCommitted"]);
        Assert.Null(projected["decisionWorldGeneration"]);
        Assert.Null(projected["targetUuid"]);
        Assert.Null(projected["crafting"]);
        Assert.Null(projected["offersPending"]);
    }

    [Fact]
    public void Main_thread_runtime_delegates_to_the_shared_GameAction()
    {
        DiscoveryTreeSO.All.Clear();
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            visible = true,
        };
        DiscoveryTreeSO.All.Add(tree);
        var lifecycle = 7L;
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        };
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.NotReady("not used"),
            _ => null);
        var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycle));
        var feature = new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                resolver,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => false,
                runtimeDiagnostics: null,
                featureStatus: status));

        try
        {
            using var runtime = AutomataServiceCycleComposition.Create(
                configuration,
                new ConfigGeneration(1),
                new AutomataServiceCycleHostDependencies(
                    () => 1,
                    () => lifecycle,
                    new ServiceActionOutcomeWindowRegistry()),
                new IAutomataServiceCycleFeature[] { feature },
                new ManualLogSource(),
                createDiscoveryTreeOffers: () => new DiscoveryTreeOfferGameAction(
                    () => lifecycle,
                    () => true,
                    () => string.Empty));
            var command = new GameMcpCommand(
                1,
                GameMcpCommandKind.DiscoveryTreeOffer,
                lifecycle,
                1,
                "initiate",
                tree.GetGuid(),
                Guid.Empty,
                "DiscoveryTreeSO", 1,
                string.Empty,
                string.Empty,
                false);

            var result = runtime.ExecuteGameMcp(command);
            var projected = GameMcpTestHarness.Json(result.Project(command));

            Assert.Equal("committed", result.Status);
            Assert.Equal(1, tree.initiateCalls);
            Assert.Equal(new[] { "status" },
                projected.Properties().Select(property => property.Name));
            Assert.Null(projected["code"]);
            Assert.Null(projected["nativeCallsAttempted"]);
        }
        finally
        {
            DiscoveryTreeSO.All.Clear();
        }
    }

    [Fact]
    public void TwoClaimedOfferActionsShareOnePinnedContextAndSecondRevalidatesFirstMutation()
    {
        DiscoveryTreeSO.All.Clear();
        var tree = new DiscoveryTreeSO
        {
            actionMode = DiscoveryTreeSO.DiscoveryTreeModes.Idle,
            visible = true,
        };
        DiscoveryTreeSO.All.Add(tree);
        var lifecycle = 7L;
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        };
        var resolver = new TypedRegistryResolver(
            () => lifecycle,
            () => TypedRegistrySourceSnapshot.NotReady("not used"),
            _ => null);
        var status = new AutomataFeatureStatusReporter(
            new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, AutomataFeatureStatuses.AutoHarvestFeatureId),
                "Auto Harvest",
                true,
                FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.RegistryNotReady, "waiting"),
                lifecycle));
        var feature = new AutoHarvestServiceCycleFeature(
            new AutoHarvestFeatureDependencies(
                resolver,
                ownsActionFamily: () => false,
                tryCaptureMutationPermit: () => false,
                runtimeDiagnostics: null,
                featureStatus: status));

        try
        {
            using var runtime = AutomataServiceCycleComposition.Create(
                configuration,
                new ConfigGeneration(1),
                new AutomataServiceCycleHostDependencies(
                    () => 1,
                    () => lifecycle,
                    new ServiceActionOutcomeWindowRegistry()),
                new IAutomataServiceCycleFeature[] { feature },
                new ManualLogSource(),
                createDiscoveryTreeOffers: () => new DiscoveryTreeOfferGameAction(
                    () => lifecycle,
                    () => true,
                    () => string.Empty));
            var worldTree = new WorldDiscoveryTree(
                tree.GetGuid(), true, 0, BigDouble.Zero, 0, false, Guid.Empty,
                Array.Empty<Guid>(), false, true, Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty,
                Guid.Empty, 0, 0, false, 0, 1, 3, 2, true, true, false);
            var world = new GameWorldState
            {
                DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[] { worldTree }),
                CollectedAtEpoch = lifecycle,
                CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
            };
            var context = GameMcpTestHarness.Context(world, generation: 44);
            var inbox = new GameMcpFrameInbox();
            var operations = new[]
            {
                inbox.Submit(OfferRequest(tree.GetGuid())),
                inbox.Submit(OfferRequest(tree.GetGuid())),
            };
            var observedContexts = new List<GameMcpFrameContext>();
            var terminals = new List<GameMcpCommandResult>();
            var faults = new List<Exception>();

            var drained = GameMcpFrameBatchExecutor.Drain(
                inbox,
                _ => context,
                (operation, pinned) =>
                {
                    observedContexts.Add(pinned);
                    var command = new GameMcpCommand(
                        operation.Sequence,
                        GameMcpCommandKind.DiscoveryTreeOffer,
                        expectedLifecycleGeneration: lifecycle,
                        expectedConfigurationGeneration: 1,
                        mode: "initiate",
                        tree.GetGuid(),
                        Guid.Empty,
                        "DiscoveryTreeSO", 1,
                        string.Empty,
                        string.Empty,
                        false,
                        operation,
                        pinned);
                    var result = runtime.ExecuteGameMcp(command);
                    terminals.Add(result);
                    return new GameMcpToolExecution(
                        result.Project(command),
                        result.InlinePng,
                        result.IsProtocolError);
                },
                (_, _, exception) =>
                {
                    faults.Add(exception);
                    return GameMcpToolExecution.Error(new GameMcpObjectBuilder
                    {
                        ["status"] = "faulted",
                        ["reason"] = exception.Message,
                    }.Freeze());
                });

            Assert.Equal(2, drained);
            Assert.Empty(faults);
            Assert.Equal("committed", terminals[0].Status);
            Assert.Equal("refused", terminals[1].Status);
            Assert.Equal("wrong_mode", terminals[1].Code);
            Assert.Equal(1, tree.initiateCalls);
            Assert.Equal(DiscoveryTreeSO.DiscoveryTreeModes.Crafting, tree.actionMode);
            Assert.All(observedContexts, observed => Assert.Same(context, observed));
            Assert.All(operations, operation => Assert.True(operation.Completion.TryWait(
                TimeSpan.FromMilliseconds(50), out _)));
        }
        finally
        {
            DiscoveryTreeSO.All.Clear();
        }
    }

    private static GameMcpOperationRequest OfferRequest(Guid treeId) =>
        new GameMcpOperationRequestBuilder
        {
            ToolName = "game_discover",
            Classification = GameMcpOperationClass.Gameplay,
            RequiredData = GameMcpFrameData.World | GameMcpFrameData.Configuration,
            Uuid = treeId,
            Mode = "initiate",
        }.Freeze();

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
        Assert.Equal("ERR_LOCKED", (string?)blocked["reasonCode"]);
        Assert.Equal(
            "Nothing in this tree can be discovered right now: 12 of its 65 are discovered, and " +
            "none of the other 53 is in reach — a tree's pool is widened by its Recipe Books, and " +
            "an item in the pool is offered only once the game shows it.",
            (string?)blocked["reason"]);

        var finished = Initiate(treeId, discovered: 65, discoverable: 65);
        Assert.Equal("ERR_NOT_FOUND", (string?)finished["reasonCode"]);
        Assert.Equal(
            "Every one of this tree's 65 discoveries is made.", (string?)finished["reason"]);
    }

    private static JObject Initiate(Guid treeId, int discovered, int discoverable) =>
        (JObject)GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(
                DiscoveryWorld(new WorldDiscoveryTree(
                    treeId, true, 0, BigDouble.Zero, 1, false, Guid.Empty,
                    Array.Empty<Guid>(), false, false,
                    Array.Empty<WorldDiscoveryTreeCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, discovered, discovered, discoverable, discoverable,
                    false, false, discovered >= discoverable)),
                generation: 84),
            "discovery-trees",
            treeId.ToString("D")))["row"]!["initiate"]!;

    private static GameWorldState DiscoveryWorld(
        WorldDiscoveryTree tree,
        WorldTimeRune[]? timeRunes = null,
        WorldGlyph[]? glyphs = null) =>
        new()
        {
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[] { tree }),
            TimeRunes = timeRunes is null
                ? PublicationTable<WorldTimeRune>.Empty
                : PublicationTable<WorldTimeRune>.Create(timeRunes),
            AugmentGlyphs = glyphs is null
                ? PublicationTable<WorldGlyph>.Empty
                : PublicationTable<WorldGlyph>.Create(glyphs),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "discovery-trees",
                    WorldCategoryOutcome.Collected,
                    sampled: 1,
                    skipped: 0,
                    firstFailure: string.Empty),
                new WorldCollectionCategoryStatus(
                    "time runes",
                    WorldCategoryOutcome.Collected,
                    sampled: timeRunes?.Length ?? 0,
                    skipped: 0,
                    firstFailure: string.Empty),
                new WorldCollectionCategoryStatus(
                    "augment glyphs",
                    WorldCategoryOutcome.Collected,
                    sampled: glyphs?.Length ?? 0,
                    skipped: 0,
                    firstFailure: string.Empty),
            }),
            CollectedAtEpoch = 7,
            CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
        };

}

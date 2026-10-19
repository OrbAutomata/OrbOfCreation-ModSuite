using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;
using static OrbModding.ProfileTests.GameMcpDiscoveryTreeTestSupport;

namespace OrbModding.ProfileTests;

[Collection(GameMcpServiceCycleRuntimeCollection.Name)]
public sealed class GameMcpDiscoveryTreeOfferVerbTests
{
    [Fact]
    public void TheDiscoverVerbCarriesTheEventOfferModes()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "game_discover");

        Assert.False((bool)tool["annotations"]!["readOnlyHint"]!);
        var schema = tool["inputSchema"]!;
        Assert.Equal(
            new[] { "mode", "uuid" },
            schema["required"]!.Values<string>().ToArray());
        Assert.Equal(
            new[] { "offer_initiate", "offer_select", "offer_confirm", "offer_reroll" },
            schema["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.NotNull(schema["properties"]!["offerUuid"]);
    }

    [Fact]
    public void Conditional_offer_identity_errors_are_structured()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
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
            DiscoveryTreeOfferActionResultCodes.TreeUnavailable,
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
            NativeActionOutcome.Fault(
                DiscoveryTreeOfferActionResultCodes.VerificationFailed,
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(1, 1, 0),
                "the requested transition was not observed"));

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
            new[] { offerId }, false, true, Array.Empty<WorldResourceCost>(),
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
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "confirm", treeId, offerId,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2), generation: 41));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, discoveredCount: 1), generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(GameMcpTestHarness.Handle(offerId), (string?)delta["discovered"]!["uuid"]);
        Assert.Equal(0, (int)delta["treeDiscovered"]!["before"]!);
        Assert.Equal(1, (int)delta["treeDiscovered"]!["after"]!);
        // A count that moved says nothing about how much of the tree is left, which is the question
        // a caller confirmed an offer to make progress on.
        Assert.Equal(4, (int)delta["discoverableCount"]!);
        Assert.Equal("choice", (string?)delta["mode"]!["before"]);
        Assert.Equal("idle", (string?)delta["mode"]!["after"]);
        Assert.True((bool)delta["hasRemainingDiscoveries"]!);
    }

    /// <summary>
    /// A tree mid-craft counts seconds, so it says them the way every other duration on this
    /// surface does. It shipped as a bare magnitude and a live round watched it climb 0.22 → 2.72
    /// with nothing on the line saying what the number counted.
    /// </summary>
    [Fact]
    public void A_crafting_tree_says_its_elapsed_time_as_a_duration()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var world = Tree(
            treeId, actionMode: 1, collectedAtUtcTicks: DateTime.UtcNow.Ticks);

        var row = GameMcpTestHarness.Json(GameMcpWorldQuery.GetRow(
            GameMcpTestHarness.Context(world, generation: 91),
            "discovery-trees",
            treeId.ToString("D")))["row"]!;

        Assert.Equal("crafting", (string?)row["mode"]);
        Assert.Equal("0.00s", (string?)row["craftingFor"]);
        Assert.Null(row["actionTime"]);
    }

    /// <summary>
    /// A spell taken off a tree and a spell taken off a row are the same discovery, and the game
    /// loads either one the same way. The tree route answered with the tree's counts alone, so a
    /// caller that confirmed an offer had to read the loadout again to learn whether its new spell
    /// was already on the bar and where.
    /// </summary>
    [Fact]
    public void Confirming_a_spell_offer_ends_with_the_loadout_block_a_row_confirm_prints()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var spellId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "confirm", treeId, spellId,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2), generation: 41));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(
                    treeId,
                    actionMode: 0,
                    discoveredCount: 1,
                    discoveredSpellId: spellId,
                    loadedSlotIndex: 3),
                generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.True((bool)delta["loadout"]!["loaded"]!);
        // The bar the player looks at counts from one.
        Assert.Equal(4, (int)delta["loadout"]!["slot"]!);
    }

    /// <summary>
    /// The mode the tool is called with and the mode the projection switches on are the same word
    /// after the request's `offer_` prefix is stripped — so the press a caller makes is the press
    /// the confirm delta answers. Fixtures that spelled the mode the tool's way kept every delta
    /// test green while the live press fell through to the plain tree row.
    /// </summary>
    [Fact]
    public void The_prepared_confirm_command_is_the_one_the_confirm_delta_answers()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var operation = GameMcpToolCatalog.Shipping.BuildOperation(
            "game_discover",
            new JObject
            {
                ["mode"] = "offer_confirm",
                ["uuid"] = treeId.ToString("D"),
                ["offerUuid"] = offerId.ToString("D"),
            });

        Assert.True(GameMcpCommandPreparation.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(Tree(treeId, actionMode: 2), generation: 41),
            out var command,
            out var failure), failure?.Reason);

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, discoveredCount: 1), generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Equal(GameMcpTestHarness.Handle(offerId), (string?)delta["discovered"]!["uuid"]);
        Assert.Equal(1, (int)delta["treeDiscovered"]!["after"]!);
        Assert.Equal("idle", (string?)delta["mode"]!["after"]);
    }

    /// <summary>
    /// Nothing but a spell has a loadout, so nothing but a spell answers with one.
    /// </summary>
    [Fact]
    public void Confirming_an_offer_that_is_not_a_spell_says_nothing_about_a_loadout()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var offerId = Guid.Parse("a98e5e7d-3bf5-46cf-a6df-73747ed57797");
        var command = new GameMcpCommand(
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "confirm", treeId, offerId,
            "DiscoveryTreeSO", 1, string.Empty, string.Empty, false,
            frameContext: GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 2), generation: 41));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, discoveredCount: 1), generation: 42),
            command,
            GameMcpCommandResult.Committed("committed", 9, 3)));

        Assert.Null(delta["loadout"]);
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
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "reroll", treeId, Guid.Empty,
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
            1, GameMcpCommandKind.DiscoveryTreeOffer, 9, 3, "reroll", treeId, Guid.Empty,
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

    /// <summary>
    /// The press that starts a roll settles on the mode change it makes, and says the roll is
    /// still running.
    /// </summary>
    /// <remarks>
    /// A round read <c>mode: crafting</c> on the initiate press and <c>mode: choice</c> on its next
    /// read and took the word for a flip. The press settles on exactly what the press produces —
    /// the tree leaving idle for crafting — and no further: the game rolls the offers three seconds
    /// of game time later, which no frame-scale settlement budget can outwait, and waiting for the
    /// offers reported a timeout on every initiate that had plainly landed. So the press says what
    /// it is instead: the roll is running, and for how long.
    /// </remarks>
    [Fact]
    public void An_initiated_roll_settles_on_crafting_and_says_the_roll_is_running()
    {
        var treeId = Guid.Parse("d88aa06b-7a71-4db4-a293-d27ab21befd8");
        var completedAt = DateTime.UtcNow.Ticks;
        var operation = GameMcpToolCatalog.Shipping.BuildOperation(
            "game_discover",
            new JObject
            {
                ["mode"] = "offer_initiate",
                ["uuid"] = treeId.ToString("D"),
            });

        Assert.True(GameMcpCommandPreparation.TryPrepareGameMcpCommand(
            new GameMcpFrameOperation(1, operation),
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, collectedAtUtcTicks: completedAt - 1),
                generation: 41),
            out var command,
            out var failure), failure?.Reason);

        // Still idle a world later: the press has not landed, and the answer must not describe it.
        Assert.False(GameMcpPostStateSettlement.IsReady(
            GameMcpTestHarness.Context(
                Tree(treeId, actionMode: 0, collectedAtUtcTicks: completedAt + 1),
                generation: 42),
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));

        var rolling = GameMcpTestHarness.Context(
            Tree(
                treeId, actionMode: 1, collectedAtUtcTicks: completedAt + 1,
                actionTime: new BigDouble(0.1d)),
            generation: 42);
        Assert.True(GameMcpPostStateSettlement.IsReady(
            rolling,
            mutationWorld: 41,
            actionCompletedAtUtcTicks: completedAt,
            command));

        var delta = GameMcpTestHarness.Json(GameMcpWorldQuery.ProjectGameplayPostState(
            rolling, command, GameMcpCommandResult.Committed("committed", 9, 3)));
        Assert.Equal("crafting", (string?)delta["mode"]);
        Assert.Equal("0.10s", (string?)delta["craftingFor"]);
        Assert.Null(delta["actionTime"]);
        Assert.Equal(
            "This roll is still running. The game rolls its offers three seconds after the " +
            "press, and this tree has been rolling 0.10s.",
            (string?)delta["rolling"]);
    }

    private static GameWorldState Tree(
        Guid treeId,
        int actionMode,
        long collectedAtUtcTicks = 0,
        int discoveredCount = 0,
        int rerollsLeft = 2,
        Guid[]? offers = null,
        Guid discoveredSpellId = default,
        int loadedSlotIndex = -1,
        BigDouble actionTime = default) => new()
        {
            CollectedAtEpoch = 7,
            CollectedAtUtcTicks = collectedAtUtcTicks,
            SpellRecipes = discoveredSpellId == Guid.Empty
                ? PublicationTable<WorldSpellRecipe>.Empty
                : PublicationTable<WorldSpellRecipe>.Create(new[]
                {
                    new WorldSpellRecipe(
                        discoveredSpellId, true, 0, BigDouble.Zero, 0, false, false, false,
                        0, 1d, 1, false, BigDouble.One, BigDouble.One, BigDouble.One,
                        BigDouble.One, BigDouble.One, BigDouble.One, false),
                }),
            SpellSlots = loadedSlotIndex < 0
                ? PublicationTable<WorldSpellSlot>.Empty
                : PublicationTable<WorldSpellSlot>.Create(new[]
                {
                    new WorldSpellSlot(
                        loadedSlotIndex, discoveredSpellId, occupied: true, casting: false,
                        readyingCast: false, attuning: false, channeled: false, toggled: false,
                        chargeable: false, castReady: true, chargeAvailable: true,
                        resourcesCovered: true, currentCharges: 1, maximumCharges: 1,
                        cooldownRemaining: BigDouble.Zero),
                }),
            DiscoveryTrees = PublicationTable<WorldDiscoveryTree>.Create(new[]
            {
                new WorldDiscoveryTree(
                    treeId, true, actionMode, actionTime, rerollsLeft, false, Guid.Empty,
                    offers ?? Array.Empty<Guid>(), false, true,
                    Array.Empty<WorldResourceCost>(), Guid.Empty, Guid.Empty,
                    0, 0, false, discoveredCount, 3, discoveredCount + 3, 4, true, true, false),
            }),
            CollectionCategories = PublicationTable<WorldCollectionCategoryStatus>.Create(new[]
            {
                new WorldCollectionCategoryStatus(
                    "discovery-trees", WorldCategoryOutcome.Collected, 1, 0, string.Empty),
            }),
        };
}

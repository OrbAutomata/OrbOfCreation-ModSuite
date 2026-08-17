using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// One sentence generator, and no surface that ships a code without one.
/// </summary>
/// <remarks>
/// Thirty-four blocked decision blocks published <c>reasonCode</c> alone against six that carried
/// prose. A caller that cannot read why a verb is shut has one way left to find out — fire it — and
/// that is the loop the pre-decision surface exists to spare an unattended strategist on a verb
/// that spends. The rule is enforced where every response already passes, so a block written
/// tomorrow cannot reintroduce it.
/// </remarks>
public sealed class GameMcpDecisionReasonTests
{
    /// <summary>
    /// Three outcome words, three owners: the game said no (<c>refused</c>), the suite committed
    /// and its own post-check disagreed (<c>faulted</c>), or the suite tripped before the game was
    /// ever asked (<c>failed</c>).
    /// </summary>
    /// <remarks>
    /// The suite's own staging write failing to land used to ship as <c>refused</c>, which reads
    /// as "the game said no" and sent a caller looking for a game state to change. The word is
    /// derived from who owns the reason rather than from a fifth disposition, so the service-cycle
    /// contract keeps answering the one question it asks — did the mutation run.
    /// </remarks>
    [Theory]
    [InlineData("contract_unavailable", "failed")]
    [InlineData("feature_contract_unavailable", "failed")]
    [InlineData("pair_contract_unavailable", "failed")]
    [InlineData("wrong_thread", "failed")]
    [InlineData("staged_write_failed", "failed")]
    [InlineData("world_not_published", "failed")]
    [InlineData("entity_catalog_unavailable", "failed")]
    [InlineData("loadout_full", "refused")]
    [InlineData("usage_unaffordable", "refused")]
    [InlineData("screen_locked", "refused")]
    [InlineData("spell_recharging", "refused")]
    [InlineData("cast_in_progress", "refused")]
    [InlineData("augment_slots_exceeded", "refused")]
    [InlineData("unique_spell_conflict", "refused")]
    [InlineData("identity_unavailable", "refused")]
    [InlineData("lifecycle_replaced", "refused")]
    [InlineData("native_rejected", "refused")]
    // The permit is the suite working as designed, not the suite failing: another service holds
    // the family this instant and lets go of it on its own.
    [InlineData("action_family_unavailable", "refused")]
    public void The_outcome_word_names_who_stopped_the_call(string reasonCode, string expected)
    {
        Assert.Equal(expected == "failed", GameMcpDecisionReason.IsSuiteDefect(reasonCode));
    }

    /// <summary>
    /// The wire mapping itself: a committed mutation, a fault, a refusal the game gave, and a
    /// failure the suite owns, over the one command family this vocabulary was rewritten for.
    /// </summary>
    [Fact]
    public void The_status_word_table_is_the_disposition_plus_who_owns_the_reason()
    {
        Assert.Equal("committed", Word(ServiceActionResult.Committed(
            CommonActionResultCodes.Committed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.Verified,
                new NativeMutationCallOutcome(1, 1, 1)))));
        Assert.Equal("faulted", Word(ServiceActionResult.Faulted(
            SpellWorkbenchActionResultCodes.VerificationFailed,
            ServiceNativeMutationEvidence.Observed(
                NativeMutationOutcome.PostconditionFailed,
                new NativeMutationCallOutcome(1, 1, 0)))));
        Assert.Equal("refused", Word(
            ServiceActionResult.Rejected(SpellWorkbenchActionResultCodes.LoadoutFull)));
        Assert.Equal("failed", Word(
            ServiceActionResult.Rejected(SpellWorkbenchActionResultCodes.StagedWriteFailed)));
    }

    private static string Word(ServiceActionResult result) =>
        GameMcpCommandResult.FromAction(
            in result, GameMcpCommandKind.SpellWorkbench, 1, 1).Status;

    [Fact]
    public void No_decision_ships_a_code_without_the_sentence_that_reads_it()
    {
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["toggle"] = new GameMcpObjectBuilder
            {
                ["available"] = false,
                ["reasonCode"] = "not_available",
            },
            ["slots"] = new GameMcpArrayBuilder(
                new GameMcpObjectBuilder
                {
                    ["available"] = false,
                    ["reasonCode"] = "loadout_full",
                }.Freeze(),
                new GameMcpObjectBuilder
                {
                    ["remove"] = new GameMcpObjectBuilder
                    {
                        ["available"] = false,
                        ["reasonCode"] = "not_active",
                    },
                }.Freeze()),
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        var document = Assert.IsType<JObject>(encoded);
        var blocks = document.DescendantsAndSelf()
            .OfType<JObject>()
            .Where(item => item["reasonCode"] is not null)
            .ToArray();

        Assert.Equal(3, blocks.Length);
        Assert.All(blocks, block =>
            Assert.False(string.IsNullOrWhiteSpace((string?)block["reason"])));
        Assert.Equal(
            "The game has not unlocked this yet.",
            (string?)document["toggle"]!["reason"]);
        Assert.Equal(
            "Every slot in this loadout is in use.",
            (string?)document["slots"]![0]!["reason"]);
        Assert.Equal(
            "This is not active, so there is nothing to act on.",
            (string?)document["slots"]![1]!["remove"]!["reason"]);
    }

    /// <summary>
    /// The backstop stops at the edge of a table. Inside a page's rows, <c>available: no</c> is a
    /// declared column the header already names, and the pair the backstop invented there said the
    /// game had refused and would not say why — the same sentence on every row, in a column no
    /// reader gained anything from. Everywhere else it still fires, because everywhere else a bare
    /// no really does leave a caller with no axis.
    /// </summary>
    [Fact]
    public void A_row_of_a_page_answers_no_without_a_sentence_explaining_the_no()
    {
        var encoded = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["rows"] = new GameMcpArrayBuilder(
                    new GameMcpObjectBuilder
                    {
                        ["name"] = "Arcane Glyph",
                        ["available"] = false,
                        ["candidates"] = new GameMcpArrayBuilder(
                            new GameMcpObjectBuilder { ["available"] = false }.Freeze()),
                    }.Freeze()),
                ["decision"] = new GameMcpObjectBuilder { ["available"] = false },
                ["total"] = 1,
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog));

        var row = (JObject)encoded["rows"]![0]!;
        Assert.False((bool)row["available"]!);
        Assert.Null(row["reasonCode"]);
        Assert.Null(row["reason"]);

        // A cell of that row is still a cell of that row, however deep it is.
        Assert.Null(row["candidates"]![0]!["reasonCode"]);
        Assert.Null(row["candidates"]![0]!["reason"]);

        // The same bare no outside the table keeps the axis the backstop exists to supply, and the
        // axis is a lock: the game is holding this shut and has published no condition for it.
        Assert.Equal("ERR_LOCKED", (string?)encoded["decision"]!["reasonCode"]);
        Assert.Equal(
            "The game keeps this locked, and says nothing about what would unlock it.",
            (string?)encoded["decision"]!["reason"]);
    }

    /// <summary>
    /// One class per fact per response. The detail read published an entity's row and its
    /// evaluated predicates side by side and let both answer availability, with different classes:
    /// a caller branching on the row's code ran a different program than one branching on the
    /// predicate. The predicate block is the verdict surface and keeps the answer.
    /// </summary>
    [Fact]
    public void A_response_answering_one_fact_twice_keeps_the_predicate_and_drops_the_row_copy()
    {
        var encoded = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["row"] = new GameMcpObjectBuilder
                {
                    ["available"] = false,
                    ["level"] = 0,
                }.Freeze(),
                ["predicates"] = new GameMcpObjectBuilder
                {
                    ["available"] = new GameMcpObjectBuilder
                    {
                        ["available"] = false,
                        ["reasonCode"] = "native_unavailable",
                    }.Freeze(),
                }.Freeze(),
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog));

        var row = (JObject)encoded["row"]!;
        Assert.False((bool)row["available"]!);
        Assert.Null(row["reasonCode"]);
        Assert.Null(row["reason"]);

        var predicate = (JObject)encoded["predicates"]!["available"]!;
        Assert.Equal("ERR_LOCKED", (string?)predicate["reasonCode"]);
        Assert.Equal(
            "The game keeps this locked, and says nothing about what would unlock it.",
            (string?)predicate["reason"]);
    }

    /// <remarks>
    /// A site that holds the numbers writes the better sentence, and the fallback must not overwrite
    /// it — the shortfall names the resource, the price, and the holding, where the code alone can
    /// only say that something was short.
    /// </remarks>
    [Fact]
    public void A_sentence_written_from_the_numbers_survives_the_fallback()
    {
        var encoded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["available"] = false,
            ["reasonCode"] = "unaffordable",
            ["reason"] = "Needs 20 Arcana (have 1).",
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        Assert.Equal(
            "Needs 20 Arcana (have 1).",
            (string?)Assert.IsType<JObject>(encoded)["reason"]);

        // The ceiling codes are where producers most often hold the number, so the override has to
        // hold there too: the generic headroom sentence must not displace the live maximum.
        var bounded = GameMcpDocumentJsonEncoder.Encode(new GameMcpObjectBuilder
        {
            ["available"] = false,
            ["reasonCode"] = "amount_unavailable",
            ["reason"] = "The plot allows fewer than that.",
            ["maximumAmount"] = 3,
        }.Freeze(), GameMcpTestHarness.EntityCatalog);

        Assert.Equal(
            "The plot allows fewer than that.",
            (string?)Assert.IsType<JObject>(bounded)["reason"]);
    }

    /// <remarks>
    /// The world publishes a handful of reasons as its own free text rather than as a member of a
    /// closed set. Restating one is honest; dropping it puts a caller back in front of a bare code.
    /// </remarks>
    [Theory]
    [InlineData("not_available", "The game has not unlocked this yet.")]
    [InlineData("unaffordable", "The named resources fall short of the price.")]
    [InlineData(
        "amount_unavailable",
        "The game's own headroom for this is below what the call asked for.")]
    [InlineData("a_code_no_table_knows", "A code no table knows.")]
    [InlineData("", "The game does not admit this right now.")]
    public void Every_code_reads_as_a_sentence(string reasonCode, string expected) =>
        Assert.Equal(expected, GameMcpDecisionReason.For(reasonCode));

    /// <summary>
    /// One kind of no is one class wherever it happens. A value outside the range its setting takes
    /// answered ERR_INPUT on the casting dial and ERR_REFUSED on a configuration write, which taught
    /// a caller that the class described which tool it called rather than what went wrong.
    /// </summary>
    [Theory]
    [InlineData("level_out_of_range")]
    [InlineData("slot_out_of_range")]
    [InlineData("destination_out_of_range")]
    [InlineData("configuration_write_rejected")]
    public void A_value_outside_its_range_is_one_class_on_every_verb(string reasonCode) =>
        Assert.Equal(GameMcpDecisionReason.ClassInput, GameMcpDecisionReason.Class(reasonCode));

    /// <summary>
    /// A refusal whose argument was fine and whose state was the blocker is a state refusal. Two of
    /// them wore ERR_INPUT: a challenge that has already run — because one producer word,
    /// <c>invalid_state</c>, was spelled into the input arm as well as the state arm and the first
    /// arm won — and a dismiss call that named nothing at all while two modals happened to be open.
    /// </summary>
    [Theory]
    [InlineData("invalid_state")]
    [InlineData("already_ran")]
    [InlineData("multiple_modals_open")]
    public void A_valid_argument_blocked_by_the_state_is_a_state_refusal(string reasonCode) =>
        Assert.Equal(GameMcpDecisionReason.ClassState, GameMcpDecisionReason.Class(reasonCode));

    /// <summary>
    /// The caller's own filter word is still the caller's own filter word: a state filter naming a
    /// lifecycle word this surface has none of is an input refusal, under a code of its own so the
    /// two meanings can never share one again.
    /// </summary>
    [Fact]
    public void A_filter_word_this_surface_does_not_take_is_an_input_refusal() =>
        Assert.Equal(
            GameMcpDecisionReason.ClassInput,
            GameMcpDecisionReason.Class("invalid_state_filter"));

    /// <summary>
    /// The rule, not this press's effect: "does nothing" read as a shrug about the button just
    /// pressed, so a round spent a second mutation asking whether the next row behaved the same.
    /// </summary>
    [Fact]
    public void A_challenge_that_has_run_states_the_standing_rule() =>
        Assert.Equal(
            "A challenge that has already run cannot be queued again until the next reset.",
            GameMcpDecisionReason.For("already_ran"));

    /// <summary>
    /// Every code that reaches a <c>reasonCode</c> field ships the sentence that explains it. The
    /// one code that used to be exempt — <c>collector_not_listable</c> — no longer reaches that
    /// field at all: <c>world_categories</c> prints its sentence once under <c>unlistable:</c> and
    /// the rows that cannot be paged say that word back in their own reason cell, so the exemption
    /// and the mechanism that carried it are both gone.
    /// </summary>
    [Fact]
    public void Every_code_that_reaches_the_wire_ships_the_sentence_that_explains_it()
    {
        var encoded = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["available"] = false,
                ["reasonCode"] = "progression_locked",
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog));

        Assert.Equal("ERR_LOCKED", (string?)encoded["reasonCode"]);
        Assert.Equal(
            "The progression that unlocks this is not reached yet.", (string?)encoded["reason"]);
    }

    /// <summary>
    /// The sentence sits under the code it explains. A locked glyph published the two seven lines
    /// apart with three decisions wedged between them, and the trailing sentence read as though it
    /// belonged to the decision above it.
    /// </summary>
    [Fact]
    public void The_sentence_sits_immediately_under_the_code_it_explains()
    {
        var encoded = Assert.IsType<JObject>(GameMcpDocumentJsonEncoder.Encode(
            new GameMcpObjectBuilder
            {
                ["state"] = "locked",
                ["reasonCode"] = "undiscovered",
                ["paidLevel"] = 1,
                ["discover"] = new GameMcpObjectBuilder { ["available"] = true }.Freeze(),
            }.Freeze(),
            GameMcpTestHarness.EntityCatalog));

        Assert.Equal(
            new[] { "state", "reasonCode", "reason", "paidLevel", "discover" },
            encoded.Properties().Select(property => property.Name).ToArray());
    }

    [Fact]
    public void The_shortfall_names_every_resource_that_is_actually_short()
    {
        var sentence = GameMcpDecisionReason.Shortfall(new[]
        {
            ("Arcana", new BigDouble(20), new BigDouble(1)),
            ("Orb Advancement", new BigDouble(4), BigDouble.Zero),
        });

        Assert.Equal("Needs 20 Arcana (have 1); 4 Orb Advancement (have 0).", sentence);
    }
}

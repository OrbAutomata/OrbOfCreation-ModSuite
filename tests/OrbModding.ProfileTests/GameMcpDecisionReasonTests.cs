using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
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
    // The multi-buy multiplier the suite pins for one upgrade press. The pin is entirely the
    // suite's own step, and when it will not hold, the game is never shown the press — so the word
    // cannot be `refused`, which used to send a caller hunting for a game state to change.
    [InlineData("single_buy_unavailable", "failed")]
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
    /// The two configuration refusals used to explain themselves with internal counters — "expected
    /// configuration generation 41 but the main thread now has generation 42" — which no read on
    /// this surface publishes, so the numbers were unlookupable and the cause unsaid. Both now say
    /// what happened and what to do, and the same words wherever they are produced.
    /// </summary>
    [Theory]
    [InlineData("stale_configuration_generation")]
    [InlineData("configuration_not_available")]
    public void A_configuration_refusal_says_its_cause_rather_than_a_counter(string reasonCode)
    {
        var sentence = GameMcpDecisionReason.For(reasonCode);

        Assert.DoesNotContain("generation", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("main thread", sentence, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".", sentence, StringComparison.Ordinal);
        Assert.Equal(GameMcpDecisionReason.ClassUnavailable, GameMcpDecisionReason.Class(reasonCode));
    }

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
    /// The class has to match the sentence. <c>tree_unavailable</c> says the game is not showing
    /// this discovery tree — a shut screen, not a missing thing — and every producer of it holds the
    /// tree already: the read side reads visibility off the published row, and the action's
    /// preflight reaches it only after resolving the tree, and has its own code for a tree it could
    /// not resolve. It wore ERR_NOT_FOUND, which sent a caller looking for a different id.
    /// </summary>
    [Fact]
    public void A_tree_the_game_is_not_showing_is_locked_rather_than_missing()
    {
        Assert.Equal(
            GameMcpDecisionReason.ClassLocked, GameMcpDecisionReason.Class("tree_unavailable"));
        Assert.Contains("not showing", GameMcpDecisionReason.For("tree_unavailable"));
    }

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

    /// <summary>
    /// The four tables that make up the wire's vocabulary agree about every code in them.
    /// </summary>
    /// <remarks>
    /// A check that answered yes carries no class and no sentence — the encoder drops its code
    /// outright — so a sentence written for one is dead the day it is written, and three of them
    /// were: <c>passed</c>, <c>native_verdict_matched</c> and <c>native_develops_below_caps</c> all
    /// had prose no response could ever carry. The other direction is the defect that matters: a
    /// code the suite calls its own failure has to be classifiable, or the wire says <c>failed</c>
    /// beside a class that means the game refused.
    /// </remarks>
    [Fact]
    public void The_vocabulary_tables_agree_about_every_code_they_name()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(), "src", "Automata", "Runtime", "GameMcp", "GameMcpDecisionReason.cs"));
        var passing = Codes(source, "internal static bool IsPassing", "_ => false,");
        var suiteDefects = Codes(source, "internal static bool IsSuiteDefect", "_ => false,");
        var classified = Codes(
            source, "internal static string Class(string reasonCode)", "_ => ClassRefused,");
        var authored = Codes(
            source, "private static string? Authored(string reasonCode)", "_ => null,");

        Assert.NotEmpty(passing);
        Assert.NotEmpty(suiteDefects);
        Assert.All(passing, code => Assert.False(
            GameMcpDecisionReason.Knows(code),
            code + " answers yes, so its sentence can never reach a caller"));
        Assert.All(passing, code => Assert.DoesNotContain(code, classified));
        Assert.All(passing, code => Assert.DoesNotContain(code, authored));
        Assert.All(suiteDefects, code => Assert.Contains(code, classified));
    }

    /// <summary>
    /// Every comparison this build ships has a phrase in the screen's words, and no phrase is
    /// written for a comparison that does not exist.
    /// </summary>
    /// <remarks>
    /// Concept coverage, not type bookkeeping: the two halves are authored apart on purpose — the
    /// vocabulary turns a condition class's own <c>reqType</c> ordinal into the comparison the game
    /// performs, and the explainer words that comparison for a player — so nothing but this pairing
    /// stops a newly mapped ordinal from reaching a caller with no sentence. There is no fallback
    /// wording to fall back to: <c>RequirementNeeds</c> throws on an unworded comparison, and this
    /// test is what makes that throw a build failure rather than a live one.
    /// </remarks>
    [Fact]
    public void Every_requirement_comparison_the_game_ships_has_a_player_phrase()
    {
        var shipped = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var (kind, ordinals) in ComparisonOrdinals)
        {
            foreach (var reqType in ordinals)
            {
                var check = GameMcpNativeVocabulary.RequirementCheck(kind, reqType);
                Assert.True(
                    check is { Length: > 0 },
                    kind + " ordinal " + reqType + " maps to no comparison");
                shipped.Add(check!);
            }
        }

        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot(),
            "src", "Automata", "Runtime", "GameMcp", "GameMcpEntityExplainer.cs"));
        var start = source.IndexOf(
            "var check = GameMcpNativeVocabulary.RequirementCheck(", StringComparison.Ordinal);
        Assert.True(start >= 0, "could not find the requirement phrase switch");
        var end = source.IndexOf(
            "_ => throw new InvalidOperationException(", start, StringComparison.Ordinal);
        Assert.True(end > start, "the requirement phrase switch has no throwing default");
        var worded = new SortedSet<string>(
            Regex.Matches(source.Substring(start, end - start), "\"([a-z][a-z-]*[a-z])\" =>")
                .Select(match => match.Groups[1].Value),
            StringComparer.Ordinal);

        Assert.Equal(shipped, worded);
    }

    /// <summary>
    /// Every <c>reqType</c> ordinal the game's condition classes declare, read off the enums the
    /// contract suite pins against the audited build.
    /// </summary>
    private static IEnumerable<(WorldRequirementConditionKind Kind, int[] Ordinals)>
        ComparisonOrdinals =>
        new[]
        {
            // ResearchRequirement is declared over UpgradeRequirementType, so both read the same map.
            (WorldRequirementConditionKind.Upgrade,
                Ordinals<Requirements.UpgradeRequirementType>()),
            (WorldRequirementConditionKind.Research,
                Ordinals<Requirements.UpgradeRequirementType>()),
            (WorldRequirementConditionKind.Structure,
                Ordinals<Requirements.StructureRequirementType>()),
            (WorldRequirementConditionKind.Spell, Ordinals<Requirements.SpellRequirementType>()),
            (WorldRequirementConditionKind.AlchemyRecipe,
                Ordinals<Requirements.AlchemyRecipeType>()),
            (WorldRequirementConditionKind.Ritual, Ordinals<Requirements.RitualRequirementType>()),
            (WorldRequirementConditionKind.Number, Ordinals<Requirements.NumberRequirementType>()),
            (WorldRequirementConditionKind.Generic,
                Ordinals<Requirements.GenericRequirementType>()),
            (WorldRequirementConditionKind.PrerequisiteLink,
                Ordinals<Requirements.PrerequisiteLinkType>()),
            (WorldRequirementConditionKind.List, Ordinals<Requirements.ListRequirementType>()),
        };

    private static int[] Ordinals<TEnum>() where TEnum : struct, Enum =>
        Enum.GetValues<TEnum>().Select(value => Convert.ToInt32(value)).Distinct().ToArray();

    private static IReadOnlyCollection<string> Codes(string source, string from, string to)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);
        Assert.True(start >= 0, "could not find " + from);
        var end = source.IndexOf(to, start, StringComparison.Ordinal);
        Assert.True(end > start, "could not find " + to);
        return new SortedSet<string>(
            Regex.Matches(source.Substring(start, end - start), "\"([a-z][a-z0-9_]*)\"")
                .Select(match => match.Groups[1].Value));
    }

    /// <summary>
    /// A stop no producer accounted for is the suite failing to say anything, not the game saying
    /// no — and where the read side already has the sentence, both halves say the same thing.
    /// </summary>
    /// <remarks>
    /// The last-resort formatter named the suite's own machinery in eleven spellings — "the spell
    /// workbench boundary refused and gave no reason of its own" — and called it <c>refused</c>,
    /// which told a caller the game had said no and sent them looking for a game state to change.
    /// Most of what reached it was not even unaccounted for: <c>loadout_full</c> has had a sentence
    /// on the read side all along, and the mutation half simply never asked for it.
    /// </remarks>
    [Fact]
    public void A_stop_no_producer_accounted_for_is_the_suites_failure()
    {
        var shared = GameMcpCommandResult.FromAction(
            ServiceActionResult.Rejected(SpellWorkbenchActionResultCodes.LoadoutFull),
            GameMcpCommandKind.SpellWorkbench,
            1,
            1);

        Assert.Equal("refused", shared.Status);
        Assert.Equal(GameMcpDecisionReason.For("loadout_full"), shared.Reason);

        var unaccounted = GameMcpCommandResult.FromAction(
            ServiceActionResult.Rejected(SpellWorkbenchActionResultCodes.CompositionUnsupported),
            GameMcpCommandKind.SpellWorkbench,
            1,
            1);

        Assert.Equal("failed", unaccounted.Status);
        Assert.Equal(GameMcpActionResultCodeNames.NoAccount, unaccounted.Reason);
        Assert.DoesNotContain("boundary", unaccounted.Reason, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The two answers the router itself writes say what happened and what to do about it.
    /// </summary>
    /// <remarks>
    /// One named the inbox's own states and the wait in milliseconds; the other put a raw .NET
    /// exception message on the wire, which names types no caller can look up and is the one thing
    /// whoever fixes it needs — so it moved to the suite log under a reference the caller is
    /// handed.
    /// </remarks>
    [Fact]
    public void The_routers_own_answers_say_what_happened_and_what_to_do()
    {
        Assert.DoesNotContain(
            "canceled before execution",
            GameMcpProtocolRouter.ClaimTimeoutReason,
            StringComparison.Ordinal);
        Assert.Contains(
            "Nothing was applied.",
            GameMcpProtocolRouter.ClaimTimeoutReason,
            StringComparison.Ordinal);

        var internalError = GameMcpProtocolRouter.InternalErrorReason("tools/call", "MCP-0A1B2C3D");

        Assert.Contains("tools/call", internalError, StringComparison.Ordinal);
        Assert.Contains("nothing was applied", internalError, StringComparison.Ordinal);
        Assert.Contains("MCP-0A1B2C3D", internalError, StringComparison.Ordinal);
        Assert.DoesNotContain("internal MCP failure", internalError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The UI gadgets' refusals carry the class that matches what went wrong.
    /// </summary>
    /// <remarks>
    /// None of these codes was in <see cref="GameMcpDecisionReason.Class"/> at all, so every
    /// <c>game_navigate</c>, <c>game_tooltip</c>, <c>game_probe</c> and <c>game_continue</c>
    /// refusal fell to the default and told a caller branching on the class that the game had refused and
    /// the world did not explain why — for a stale page marker, for a miss, and for a reading this
    /// build does not take alike. The two that really are the game's own no keep the default and
    /// are named here so that stays a decision rather than an omission.
    /// </remarks>
    [Theory]
    [InlineData("tooltip_offset_invalid", GameMcpDecisionReason.ClassInput)]
    [InlineData("plot_destination_mismatch", GameMcpDecisionReason.ClassInput)]
    [InlineData("tooltip_match_failed", GameMcpDecisionReason.ClassNotFound)]
    [InlineData("tooltip_content_unavailable", GameMcpDecisionReason.ClassNotFound)]
    [InlineData("native_plot_not_resolved", GameMcpDecisionReason.ClassNotFound)]
    [InlineData("native_navigation_unavailable", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("native_plot_navigation_unavailable", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("native_plot_list_unavailable", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("native_probe_unavailable", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("tooltip_contract_unavailable", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("tooltip_read_faulted", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("navigation_request_invalid", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("unsupported_probe", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("continue_contract_unavailable", GameMcpDecisionReason.ClassUnavailable)]
    [InlineData("continue_wrong_scene", GameMcpDecisionReason.ClassState)]
    // The game's own selector said no and gave its own reason; the default is the right answer.
    [InlineData("native_tab_rejected", GameMcpDecisionReason.ClassRefused)]
    [InlineData("subtab_selection_failed", GameMcpDecisionReason.ClassRefused)]
    public void A_gadget_refusal_carries_the_class_of_what_went_wrong(
        string reasonCode,
        string expected) =>
        Assert.Equal(expected, GameMcpDecisionReason.Class(reasonCode));

    /// <summary>
    /// Every reason code the suite itself writes, and ships with no prose beside it, has a sentence
    /// of its own rather than its own spelling with the underscores taken out.
    /// </summary>
    /// <remarks>
    /// <c>Restate</c> exists for one input and one only: the world publishes a handful of refusals
    /// as free text of its own, the encoder snake-cases those on the way in, and by the time the
    /// table sees one it is indistinguishable from a code out of a closed set. One step earlier
    /// they are not: the suite's codes are string literals in <c>src</c> and the game's words never are.
    /// So the sweep reads the source rather than the table. A producer that writes its own sentence
    /// beside the code answers for itself and needs no entry; a passing code is a yes and explains
    /// nothing. Everything else reached a caller as "Bandwidth blocked." until this pin, and a
    /// fifteenth code added tomorrow would have joined them with nothing to say so.
    /// </remarks>
    [Fact]
    public void Every_code_the_suite_ships_bare_has_a_sentence_of_its_own()
    {
        var sourceRoot = Path.Combine(RepositoryRoot(), "src");
        var written = new Regex(
            "(?:\\[\"reasonCode\"\\]\\s*=|reasonCode:)\\s*\"([a-z][a-z0-9_]*)\"");
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(
                     sourceRoot, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceRoot, path).Replace('\\', '/');
            if (relative.StartsWith("bin", StringComparison.Ordinal) ||
                relative.StartsWith("obj", StringComparison.Ordinal))
            {
                continue;
            }

            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var match = written.Match(lines[index]);
                if (!match.Success) continue;
                var code = match.Groups[1].Value;
                if (GameMcpDecisionReason.IsPassing(code)) continue;
                if (WritesItsOwnSentence(lines, index)) continue;
                if (GameMcpDecisionReason.Knows(code)) continue;
                offenders.Add(relative + ":" + (index + 1) + " (" + code + ")");
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A code the suite writes reaches a caller as its own spelling: " +
            string.Join(", ", offenders));
    }

    private static bool WritesItsOwnSentence(string[] lines, int index)
    {
        var first = Math.Max(0, index - 4);
        var last = Math.Min(lines.Length - 1, index + 5);
        for (var line = first; line <= last; line++)
        {
            if (lines[line].Contains("[\"reason\"]", StringComparison.Ordinal)) return true;
        }

        return false;
    }

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "src", "OrbModSuite.csproj")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the repository source directory.");
    }
}

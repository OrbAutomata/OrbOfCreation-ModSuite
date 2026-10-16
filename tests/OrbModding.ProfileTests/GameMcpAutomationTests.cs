using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpAutomationTests
{
    [Fact]
    public void The_tool_offers_exactly_the_seven_on_off_buttons_the_player_sees()
    {
        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "suite_breakers");

        Assert.Equal(
            new[] { "list", "set" },
            tool["inputSchema"]!["properties"]!["mode"]!["enum"]!.Values<string>().ToArray());
        Assert.Equal(
            new[]
            {
                "auto_buy", "auto_cast", "auto_concept", "auto_harvest",
                "auto_items", "auto_scribe", "mentor",
            },
            tool["inputSchema"]!["properties"]!["feature"]!["enum"]!.Values<string>().ToArray());
    }

    [Fact]
    public void Every_feature_is_named_in_the_tool_description_so_one_read_explains_the_surface()
    {
        var description = (string)Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "suite_breakers")["description"]!;

        Assert.All(
            GameMcpAutomationFeatures.Names(),
            name => Assert.Contains(name, description, StringComparison.Ordinal));
        Assert.Contains("suite_emergency_stop", description, StringComparison.Ordinal);
        Assert.Contains("suite_config_set", description, StringComparison.Ordinal);
        Assert.Contains("seven green/gray breakers", description, StringComparison.Ordinal);
    }

    /// <summary>
    /// One feature, one write door. <c>AutoHarvest/Mode</c> was writable twice — once as a breaker
    /// and once as a settings line — and a live round called both in one breath to be sure they
    /// moved the same switch. The refusal is on the settings verb, not on the store: the store is
    /// the path the breaker itself writes through, so closing it there would close both doors.
    /// </summary>
    [Fact]
    public void The_seven_settings_behind_the_breakers_are_the_breakers_own()
    {
        Assert.All(
            GameMcpAutomationFeatures.All,
            feature => Assert.True(
                GameMcpAutomationFeatures.IsBreakerSetting(feature.Section, feature.Key)));

        // Everything else a feature is configured with stays on the settings pen.
        Assert.False(GameMcpAutomationFeatures.IsBreakerSetting("AutoCast", "ManualPauseSeconds"));
        Assert.False(GameMcpAutomationFeatures.IsBreakerSetting("AutoBuy", "LeaveQueueSlots"));
        Assert.False(GameMcpAutomationFeatures.IsBreakerSetting("Reserves", "AbsoluteReserve"));

        // The pairing is the whole key, not either half of it: a section with no breaker and a key
        // spelled Mode is not one, and neither is a breaker's section under another key.
        Assert.False(GameMcpAutomationFeatures.IsBreakerSetting("Safety", "Mode"));
        Assert.False(GameMcpAutomationFeatures.IsBreakerSetting("AutoHarvest", "Modes"));
    }

    /// <summary>
    /// The refused write is not a malformed value and not a locked feature: the setting is real,
    /// readable and unchanged, and what is wrong is the door it was named at. That is the same
    /// class a value outside its range answers, for the same reason — one kind of no is one class
    /// wherever it happens.
    /// </summary>
    [Fact]
    public void A_breaker_write_at_the_settings_pen_is_sent_to_the_one_door_that_flips_it()
    {
        Assert.Equal(
            GameMcpDecisionReason.ClassInput,
            GameMcpDecisionReason.Class("wrong_configuration_surface"));
        Assert.Equal(
            "This setting is one of the seven breakers, and suite_breakers is the one door that " +
            "flips it; suite_configuration still reads its value.",
            GameMcpDecisionReason.For("wrong_configuration_surface"));

        // A caller reading the settings verb learns where the seven went before spending a call.
        Assert.Equal(
            "Write one allowlisted setting through the single committed configuration-store " +
            "publication path. The seven Mode settings behind the breakers are refused here and " +
            "flipped with suite_breakers; suite_configuration still reads their values alongside " +
            "every other setting's.",
            (string)Assert.Single(
                GameMcpAcceptanceFixture.Tools(),
                candidate => (string?)candidate["name"] == "suite_config_set")["description"]!);
    }

    [Fact]
    public void Listing_returns_every_feature_named_with_whether_it_is_on()
    {
        var rows = Rows(Configuration(autoBuy: true, mentor: true));

        Assert.Equal(7, rows.Count);
        Assert.Equal("auto_buy", (string?)rows[0]["feature"]);
        // Every row names its feature the way the Mods rail names it. The page used to publish a
        // name only where it was not the id in title case, so six of seven read `-` — which says
        // the suite could not name the feature — and the reader was left deriving the other six by
        // a rule the page never stated.
        Assert.Equal("Auto Buy", (string?)rows[0]["name"]);
        Assert.True((bool)rows[0]["on"]!);
        Assert.All(rows, row => Assert.NotNull(row["name"]));
        Assert.Equal("Orb Mentor", (string?)rows[6]["name"]);
        Assert.True((bool)rows[6]["on"]!);
        Assert.All(
            rows.Skip(1).Take(5),
            row => Assert.False((bool)row["on"]!));
    }

    /// <summary>
    /// The <c>on</c> column is a config value, and a row whose runtime disagrees with it says so on
    /// the same line — so config-on plus progression-locked is one answer rather than two calls and
    /// a name-by-name join against <c>suite_health</c>.
    /// </summary>
    [Fact]
    public void A_feature_the_runtime_is_holding_says_so_beside_the_switch()
    {
        var rows = Assert.IsType<JArray>(
            GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationFeatures(
                GameMcpTestHarness.Context(
                    configuration: Configuration(autoBuy: true, mentor: true),
                    features: new[]
                    {
                        Status(
                            "Auto Buy",
                            FeatureStatusState.Locked,
                            FeatureStatusReasonCode.ProgressionLocked,
                            "the Mods rail has not unlocked this yet"),
                        Status("Orb Mentor", FeatureStatusState.Operational),
                    })))["features"]);

        Assert.Equal("auto_buy", (string?)rows[0]["feature"]);
        Assert.True((bool)rows[0]["on"]!);
        Assert.Equal("locked (progression_locked)", (string?)rows[0]["runtime"]);

        // A runtime that agrees with the switch, and a feature the runtime never reported, both
        // leave the row exactly as it was: the qualifier is worth a column only where it disagrees.
        Assert.Equal("mentor", (string?)rows[6]["feature"]);
        Assert.True((bool)rows[6]["on"]!);
        Assert.Null(rows[6]["runtime"]);
        Assert.All(rows.Skip(1).Take(5), row => Assert.Null(row["runtime"]));
    }

    [Fact]
    public void The_two_switches_that_silence_every_feature_at_once_ride_on_the_list()
    {
        var quiet = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationFeatures(
            GameMcpTestHarness.Context(
                configuration: Configuration(autoBuy: true) with
                {
                    General = new SuiteGeneralConfiguration { Enabled = false },
                    Safety = new SuiteSafetyConfiguration { EmergencyDisable = true },
                })));

        Assert.True((bool)quiet["emergencyStop"]!);
        Assert.False((bool)quiet["automationEnabled"]!);

        // An ordinary running suite says neither: the caller asked which buttons are on.
        var ordinary = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationFeatures(
            GameMcpTestHarness.Context(configuration: Configuration(autoBuy: true))));
        Assert.Null(ordinary["emergencyStop"]);
        Assert.Null(ordinary["automationEnabled"]);
    }

    [Fact]
    public void A_flip_committed_under_an_engaged_stop_says_so_in_the_same_answer()
    {
        Assert.True(GameMcpAutomationFeatures.TryGet("auto_buy", out var feature));

        var quiet = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationCommit(
            feature,
            wasOn: false,
            Configuration(autoBuy: true) with
            {
                General = new SuiteGeneralConfiguration { Enabled = false },
                Safety = new SuiteSafetyConfiguration { EmergencyDisable = true },
            }));

        Assert.Equal("auto_buy", (string?)quiet["feature"]);
        Assert.False((bool)quiet["on"]!["before"]!);
        Assert.True((bool)quiet["on"]!["after"]!);
        Assert.True((bool)quiet["emergencyStop"]!);
        Assert.False((bool)quiet["automationEnabled"]!);

        // A running suite says neither, exactly as the list does not.
        var ordinary = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationCommit(
            feature,
            wasOn: false,
            Configuration(autoBuy: true)));
        Assert.Null(ordinary["emergencyStop"]);
        Assert.Null(ordinary["automationEnabled"]);
    }

    /// <summary>
    /// A breaker is an instruction, and "on: yes" is not an answer to it: the operator wanted to
    /// know what is now running. The settings that decide that were already read to commit the
    /// flip, so the write says them instead of sending the caller to the settings pen and back.
    /// </summary>
    [Fact]
    public void Arming_a_breaker_says_what_that_feature_will_now_do()
    {
        Assert.True(GameMcpAutomationFeatures.TryGet("auto_buy", out var buy));
        var armed = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationCommit(
            buy,
            wasOn: false,
            Configuration(autoBuy: true) with
            {
                AutoBuy = new AutoBuyConfiguration
                {
                    Mode = AutoBuyOperationMode.Active,
                    StructureAffordability = AutoBuyAffordabilityMode.Excess100,
                    UpgradeAffordability = AutoBuyAffordabilityMode.BuyAll,
                    IncludeStructures = true,
                    IncludeUpgrades = true,
                    AutoLevelSpells = true,
                    LeaveQueueSlots = 1,
                },
            }));

        Assert.Equal(
            "Buys structures costing at most a hundredth of the resources on hand and upgrades " +
            "at any price you can afford, and levels ready spells, leaving one slot free in the " +
            "action queue for you.",
            (string?)armed["policy"]);

        Assert.True(GameMcpAutomationFeatures.TryGet("auto_cast", out var cast));
        var casting = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationCommit(
            cast,
            wasOn: false,
            Configuration() with
            {
                AutoCast = new AutoCastConfiguration
                {
                    Mode = AutoCastOperationMode.Active,
                    StartResourcePercent = 25f,
                    ManualPauseSeconds = 2f,
                    FullCharge = true,
                },
            }));

        Assert.Equal(
            "Fires equipped spells once every capped resource they draw on is at least 25% full, " +
            "holding chargeable ones to full charge, and it stays quiet for 2s after you cast one " +
            "by hand.",
            (string?)casting["policy"]);
    }

    /// <summary>
    /// The reading that costs an operator a session: every kind of work switched off reads exactly
    /// like a working feature, because the breaker is genuinely closed and the service genuinely
    /// runs. Only the policy line can say that nothing will come of it.
    /// </summary>
    [Fact]
    public void A_breaker_armed_over_settings_that_do_nothing_says_so()
    {
        Assert.True(GameMcpAutomationFeatures.TryGet("auto_buy", out var buy));
        var armed = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationCommit(
            buy,
            wasOn: false,
            Configuration(autoBuy: true)));

        Assert.True((bool)armed["on"]!["after"]!);
        Assert.Equal(
            "Structures, upgrades, and spell levelling are all switched off, so Auto Buy has " +
            "nothing to buy until one of them is turned back on.",
            (string?)armed["policy"]);
    }

    [Fact]
    public void Opening_a_breaker_carries_no_policy_line()
    {
        Assert.True(GameMcpAutomationFeatures.TryGet("auto_buy", out var buy));
        var opened = GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationCommit(
            buy,
            wasOn: true,
            Configuration()));

        Assert.False((bool)opened["on"]!["after"]!);
        Assert.Null(opened["policy"]);
    }

    [Fact]
    public void Every_breaker_can_say_what_it_does_under_any_settings()
    {
        Assert.All(GameMcpAutomationFeatures.All, feature =>
        {
            var policy = feature.Policy(new SuiteRuntimeConfiguration());
            Assert.NotEqual(string.Empty, policy);
            Assert.EndsWith(".", policy, StringComparison.Ordinal);
            Assert.Equal(char.ToUpperInvariant(policy[0]), policy[0]);
        });
    }

    [Fact]
    public void Every_listed_feature_names_a_setting_the_committed_write_path_accepts()
    {
        Assert.All(GameMcpAutomationFeatures.All, feature =>
        {
            Assert.Equal("Active", feature.SerializedValue(true));
            Assert.Equal("Disabled", feature.SerializedValue(false));
            Assert.Equal("Mode", feature.Key);
        });
    }

    [Fact]
    public void Setting_one_feature_names_the_feature_and_the_flip_it_made()
    {
        var request = GameMcpProtocolRouter.BuildOperation(
            "suite_breakers",
            new JObject
            {
                ["mode"] = "set",
                ["feature"] = "mentor",
                ["on"] = true,
            });

        Assert.Equal("set", request.Mode);
        Assert.Equal("mentor", request.Key);
        Assert.Equal("Active", request.SerializedValue);
        Assert.Equal(
            GameMcpOperationClass.SuiteAdministration,
            request.Classification);
    }

    [Fact]
    public void Listing_never_claims_mutation_ownership()
    {
        var request = GameMcpProtocolRouter.BuildOperation(
            "suite_breakers",
            new JObject { ["mode"] = "list" });

        Assert.Equal(GameMcpOperationClass.ReadOnly, request.Classification);
        Assert.Equal(string.Empty, request.Key);
    }

    [Fact]
    public void A_set_that_names_no_feature_and_a_list_that_names_one_are_both_refused()
    {
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "suite_breakers",
                new JObject { ["mode"] = "set", ["on"] = true }));
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "suite_breakers",
                new JObject { ["mode"] = "set", ["feature"] = "auto_buy" }));
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "suite_breakers",
                new JObject { ["mode"] = "set", ["feature"] = "auto_everything", ["on"] = true }));
    }

    private static FeatureStatusSnapshot Status(
        string displayName,
        FeatureStatusState state,
        FeatureStatusReasonCode code = FeatureStatusReasonCode.None,
        string summary = "") => new(
        new FeatureStatusKey("OrbAutomata", displayName),
        displayName,
        configuredEnabled: state != FeatureStatusState.ConfigurationDisabled,
        state,
        new FeatureStatusReason(code, summary));

    private static JArray Rows(SuiteRuntimeConfiguration configuration) =>
        Assert.IsType<JArray>(
            GameMcpTestHarness.Json(OrbModding.Plugin.ProjectGameMcpAutomationFeatures(
                GameMcpTestHarness.Context(configuration: configuration)))["features"]);

    private static SuiteRuntimeConfiguration Configuration(
        bool autoBuy = false,
        bool mentor = false) => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        AutoBuy = new AutoBuyConfiguration
        {
            Mode = autoBuy ? AutoBuyOperationMode.Active : AutoBuyOperationMode.Disabled,
        },
        Mentor = new MentorConfiguration
        {
            Mode = mentor ? MentorOperationMode.Active : MentorOperationMode.Disabled,
        },
    };
}

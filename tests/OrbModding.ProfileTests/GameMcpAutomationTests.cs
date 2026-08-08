using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
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
            candidate => (string?)candidate["name"] == "suite_automation");

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
            candidate => (string?)candidate["name"] == "suite_automation")["description"]!;

        Assert.All(
            GameMcpAutomationFeatures.Names(),
            name => Assert.Contains(name, description, StringComparison.Ordinal));
        Assert.Contains("suite_emergency_stop", description, StringComparison.Ordinal);
        Assert.Contains("suite_config_set", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Listing_returns_every_feature_named_with_whether_it_is_on()
    {
        var rows = Rows(Configuration(autoBuy: true, mentor: true));

        Assert.Equal(7, rows.Count);
        Assert.Equal("auto_buy", (string?)rows[0]["feature"]);
        Assert.Equal("Auto Buy", (string?)rows[0]["name"]);
        Assert.True((bool)rows[0]["on"]!);
        Assert.Equal("Orb Mentor", (string?)rows[6]["name"]);
        Assert.True((bool)rows[6]["on"]!);
        Assert.All(
            rows.Skip(1).Take(5),
            row => Assert.False((bool)row["on"]!));
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
            "suite_automation",
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
            "suite_automation",
            new JObject { ["mode"] = "list" });

        Assert.Equal(GameMcpOperationClass.ReadOnly, request.Classification);
        Assert.Equal(string.Empty, request.Key);
    }

    [Fact]
    public void A_set_that_names_no_feature_and_a_list_that_names_one_are_both_refused()
    {
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "suite_automation",
                new JObject { ["mode"] = "set", ["on"] = true }));
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "suite_automation",
                new JObject { ["mode"] = "set", ["feature"] = "auto_buy" }));
        Assert.Throws<GameMcpInvalidParamsException>(() =>
            GameMcpProtocolRouter.BuildOperation(
                "suite_automation",
                new JObject { ["mode"] = "set", ["feature"] = "auto_everything", ["on"] = true }));
    }

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

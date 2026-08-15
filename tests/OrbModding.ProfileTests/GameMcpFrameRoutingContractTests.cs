using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpFrameRoutingContractTests
{
    [Fact]
    public void EveryAdvertisedToolBuildsOneImmutableOperationForTheSoleInbox()
    {
        var tools = GameMcpAcceptanceFixture.Tools();
        Assert.Equal(42, tools.Count);
        var inbox = new GameMcpFrameInbox();
        var operations = tools
            .Select(tool => GameMcpProtocolRouter.BuildOperation(
                (string)tool["name"]!,
                Arguments((string)tool["name"]!)))
            .Select(inbox.Submit)
            .ToArray();

        Assert.Equal(tools.Count, operations.Length);
        Assert.Equal(operations, inbox.ClaimPending());
        Assert.Empty(inbox.ClaimPending());
        Assert.All(operations, operation =>
            Assert.Equal(operation.Request.ToolName, operation.Request.ToolName.Trim()));
    }

    [Fact]
    public void ProtocolOnlyBypassSetIsFiniteAndNeverTouchesTheFrameInbox()
    {
        Assert.Equal(
            new[]
            {
                "initialize",
                "ping",
                "tools/list",
                "resources/list",
                "resources/templates/list",
            },
            GameMcpProtocolRouter.ProtocolOnlyMethods());

        var inbox = new GameMcpFrameInbox();
        var router = new GameMcpProtocolRouter(inbox);
        foreach (var method in GameMcpProtocolRouter.ProtocolOnlyMethods())
        {
            var parameters = method == "initialize"
                ? new JObject
                {
                    ["protocolVersion"] = GameMcpProtocolRouter.LatestProtocolVersion,
                    ["capabilities"] = new JObject(),
                    ["clientInfo"] = new JObject(),
                }
                : new JObject();
            var response = router.Handle(GameMcpAcceptanceFixture.Request(1, method, parameters));
            Assert.Equal(200, response.StatusCode);
            Assert.Null(response.Body?["error"]);
            Assert.Empty(inbox.ClaimPending());
        }

        var notification = new JObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized",
        };
        Assert.Equal(202, router.Handle(notification).StatusCode);
        Assert.Empty(inbox.ClaimPending());
    }

    [Fact]
    public void ReadUiAdministrationAndGameplayHaveExactDataAndMutationClasses()
    {
        var read = GameMcpProtocolRouter.BuildOperation(
            "world_get",
            Arguments("world_get"));
        Assert.Equal(GameMcpOperationClass.ReadOnly, read.Classification);
        Assert.Equal(GameMcpFrameData.World, read.RequiredData);

        var catalog = GameMcpProtocolRouter.BuildOperation(
            "entity_catalog",
            Arguments("entity_catalog"));
        Assert.Equal(GameMcpFrameData.None, catalog.RequiredData);

        var health = GameMcpProtocolRouter.BuildOperation(
            "suite_health",
            Arguments("suite_health"));
        Assert.Equal(
            GameMcpFrameData.World | GameMcpFrameData.Configuration |
            GameMcpFrameData.FeatureHealth | GameMcpFrameData.ServiceHealth |
            GameMcpFrameData.Scene | GameMcpFrameData.NativeContractHealth,
            health.RequiredData);

        var ui = GameMcpProtocolRouter.BuildOperation(
            "game_navigate",
            Arguments("game_navigate"));
        Assert.Equal(GameMcpOperationClass.UiState, ui.Classification);
        Assert.Equal(GameMcpFrameData.World | GameMcpFrameData.Scene, ui.RequiredData);

        var administration = GameMcpProtocolRouter.BuildOperation(
            "suite_emergency_stop",
            Arguments("suite_emergency_stop"));
        Assert.Equal(GameMcpOperationClass.SuiteAdministration, administration.Classification);
        Assert.Equal(GameMcpFrameData.Configuration, administration.RequiredData);

        var gameplay = GameMcpProtocolRouter.BuildOperation(
            "game_discover",
            new JObject
            {
                ["mode"] = "offer_initiate",
                ["uuid"] = Guid.NewGuid().ToString("D"),
            });
        Assert.Equal(GameMcpOperationClass.Gameplay, gameplay.Classification);
        Assert.Equal(
            GameMcpFrameData.World | GameMcpFrameData.Configuration,
            gameplay.RequiredData);
    }

    [Fact]
    public void OptionalDiagnosticWritesEscalateOnlyTheirOwnOperation()
    {
        var tooltipRead = GameMcpProtocolRouter.BuildOperation(
            "game_tooltip",
            new JObject { ["path"] = "Canvas[0]/Button[0]" });
        var screenshot = GameMcpProtocolRouter.BuildOperation(
            "game_screenshot",
            new JObject());
        var savedScreenshot = GameMcpProtocolRouter.BuildOperation(
            "game_screenshot",
            new JObject { ["save"] = true });

        Assert.Equal(GameMcpOperationClass.ReadOnly, tooltipRead.Classification);

        // One tool, one status word: a screenshot is a capture the server performs either way, so
        // the save flag must not move it between classifications.
        Assert.Equal(GameMcpOperationClass.SuiteAdministration, screenshot.Classification);
        Assert.Equal(GameMcpOperationClass.SuiteAdministration, savedScreenshot.Classification);
        Assert.Equal(GameMcpFrameData.Configuration, screenshot.RequiredData);
        Assert.Equal(GameMcpFrameData.Configuration, savedScreenshot.RequiredData);
    }

    /// <summary>
    /// The parity check the Runtime page's action runs is reachable as a verb, takes nothing, and
    /// captures no frame data: it reads the live game itself rather than a published world, so a
    /// world capture beside it would only be a second, staler answer.
    /// </summary>
    [Fact]
    public void CheckingGameMathIsAVerbThatTakesNothingAndCapturesNoWorld()
    {
        var tool = Assert.Single(GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "suite_check_game_math");
        var operation = GameMcpProtocolRouter.BuildOperation(
            "suite_check_game_math",
            new JObject());

        Assert.Null(tool["inputSchema"]!["required"]);
        Assert.Empty(Assert.IsType<JObject>(tool["inputSchema"]!["properties"]).Properties());
        Assert.Equal(GameMcpOperationClass.SuiteAdministration, operation.Classification);
        Assert.Equal(GameMcpFrameData.None, operation.RequiredData);
    }

    /// <summary>
    /// The tooltip catalog claims the world, because a casting-bar row says which loadout slot
    /// holds the spell it shows and the loadout is a published fact. The single tooltip read claims
    /// none: it is screen prose about one element and joins against nothing.
    /// </summary>
    [Fact]
    public void TheTooltipCatalogClaimsTheWorldItJoinsAgainstAndTheSingleReadClaimsNone()
    {
        Assert.Equal(
            GameMcpFrameData.World,
            GameMcpProtocolRouter.BuildOperation("game_screen_elements", new JObject()).RequiredData);
        Assert.Equal(
            GameMcpFrameData.None,
            GameMcpProtocolRouter.BuildOperation(
                "game_tooltip",
                new JObject { ["path"] = "CastingBar[3]/Button[0]" }).RequiredData);
    }

    private static JObject Arguments(string tool) => tool switch
    {
        "world_overview" or "world_categories" or "suite_configuration" or
            "trace_health" or "game_screenshot" or "game_screen_catalog" or
            "game_continue" or "game_return_to_menu" => new JObject(),
            "game_modal" => new JObject { ["mode"] = "dismiss" },
        "world_list" => new JObject { ["category"] = "resources" },
        "world_get" => new JObject
        {
            ["category"] = "resources",
            ["uuids"] = new JArray(Guid.NewGuid().ToString("D")),
        },
        "entity_catalog" or "world_search" => new JObject { ["query"] = "mana" },
        "suite_health" or "suite_check_game_math" => new JObject(),
        "game_purchase" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["amount"] = 1,
        },
        "game_cast" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "fire",
            ["slot"] = 1,
        },
        "game_concept" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "add",
            ["amount"] = 1,
        },
        "game_agromancy" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["actionUuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "add_plot_action",
            ["amount"] = 1,
        },
        "game_structure" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "disable",
        },
        "game_spell_mastery" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "single",
        },
        "game_discover" => new JObject
        {
            ["mode"] = "preview",
            ["surface"] = "spellcraft",
            ["components"] = new JArray(new JObject
            {
                ["uuid"] = Guid.NewGuid().ToString("D"),
                ["count"] = 1,
            }),
        },
        "game_equipment" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "equip",
            ["amount"] = 1,
        },
        "game_alchemy" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "add",
            ["amount"] = 1,
        },
        "game_ritual" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "select",
        },
        "game_level_up" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "purchase",
            ["amount"] = 1,
        },
        "game_loadout" => new JObject
        {
            ["loadout"] = 1,
            ["mode"] = "select",
        },
        "time_challenge" => new JObject
        {
            ["mode"] = "reroll",
        },
        "time_prestige" => new JObject
        {
            ["confirm"] = true,
        },
        "game_research" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "develop",
            ["amount"] = 1,
        },
        "game_consumable" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
            ["mode"] = "use",
        },
        "game_craft" => new JObject
        {
            ["uuid"] = Guid.NewGuid().ToString("D"),
        },
        "game_casting_dial" => new JObject
        {
            ["dial"] = "output",
            ["value"] = 2,
        },
        "game_spell_loadout" => new JObject
        {
            ["slot"] = 1,
            ["mode"] = "remove",
        },
        "game_targeting" => new JObject
        {
            ["mode"] = "randomize",
        },
        "suite_config_set" => new JObject
        {
            ["section"] = "AutoCast",
            ["key"] = "Mode",
            ["serializedValue"] = "Disabled",
        },
        "suite_breakers" => new JObject
        {
            ["mode"] = "set",
            ["feature"] = "auto_cast",
            ["on"] = false,
        },
        "suite_emergency_stop" => new JObject
        {
            ["mode"] = "engage",
        },
        "game_navigate" => new JObject
        {
            ["screen"] = "Magic",
        },
        "game_probe" => new JObject { ["probe"] = "runtime" },
        "game_screen_elements" => new JObject(),
        "game_tooltip" => new JObject { ["path"] = "Canvas[0]/Button[0]" },
        _ => throw new InvalidOperationException("no operation fixture exists for " + tool),
    };
}

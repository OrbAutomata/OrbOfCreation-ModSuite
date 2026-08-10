using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpGadgetTests
{
    [Fact]
    public void Array_read_success_has_no_aggregate_status_or_code()
    {
        var result = GameMcpTestHarness.Json(new GameMcpObjectBuilder
        {
            ["status"] = "committed",
            ["code"] = "tooltip_catalog_read",
            ["rows"] = new GameMcpArrayBuilder(),
        });

        Assert.Null(result["status"]);
        Assert.Null(result["code"]);
        Assert.Empty(result["rows"]!);
    }

    [Fact]
    public void EveryRequestTimeGadgetHasOneDistinctAccessorAndGameplayHasNone()
    {
        var mappings = new[]
        {
            (GameMcpCommandKind.Screenshot, GameMcpGadgetAccess.Framebuffer),
            (GameMcpCommandKind.Navigation, GameMcpGadgetAccess.Navigation),
            (GameMcpCommandKind.Probe, GameMcpGadgetAccess.Probe),
            (GameMcpCommandKind.ScreenCatalog, GameMcpGadgetAccess.ScreenCatalog),
            (GameMcpCommandKind.TooltipCatalog, GameMcpGadgetAccess.TooltipCatalog),
            (GameMcpCommandKind.TooltipRead, GameMcpGadgetAccess.TooltipRead),
            (GameMcpCommandKind.ContinueRun, GameMcpGadgetAccess.ContinueRun),
        };

        Assert.Equal(7, mappings.Select(mapping => mapping.Item2).Distinct().Count());
        Assert.All(mappings, mapping =>
            Assert.Equal(mapping.Item2, GameMcpGadgetPolicy.AccessFor(mapping.Item1)));
        Assert.Throws<System.ArgumentException>(() =>
            GameMcpGadgetPolicy.AccessFor(GameMcpCommandKind.Purchase));
    }

    [Theory]
    [InlineData("runtime", true)]
    [InlineData("action_queue_room", true)]
    [InlineData("navigation", true)]
    [InlineData("SaveStateManager", false)]
    [InlineData("System.IO.File.Delete", false)]
    public void ProbeVocabularyIsClosed(string probe, bool expected)
    {
        Assert.Equal(expected, GameMcpGadgetPolicy.IsAllowlistedProbe(probe));
    }

    [Theory]
    [InlineData("Canvas[0]/ContentArea[2]/MainContentContainer[2]/SubviewRadio[1]/Tab[0]", true)]
    [InlineData("Canvas[0]/PopupContainer[6]/Modal(Clone)[25]/Tab[0]", false)]
    [InlineData("", false)]
    public void SubtabCatalogIncludesOnlyTheCurrentContentHierarchy(
        string path,
        bool expected)
    {
        Assert.Equal(expected, GameMcpGadgetPolicy.IsCurrentContentSubtabPath(path));
    }

    [Fact]
    public void ScreenshotHasNoRequiredParametersOrCallerFilename()
    {
        var screenshot = Tool("game_screenshot");
        Assert.Null(screenshot["inputSchema"]!["required"]);
        var properties = (JObject)screenshot["inputSchema"]!["properties"]!;
        Assert.Equal(new[] { "save" }, properties.Properties().Select(p => p.Name));
    }

    /// <summary>
    /// A capture is priced in 28-pixel patches, so its width is the whole cost of reading it. One
    /// width is the narrowest every class of on-screen text survives, snapped onto that patch grid,
    /// and it is the answer for every caller — so no tool takes a width to be told it again.
    /// </summary>
    [Fact]
    public void A_capture_arrives_at_the_readable_width_and_no_tool_asks_for_one()
    {
        Assert.Equal(896, GameMcpGadgetPolicy.CaptureWidth);
        Assert.Equal(0, GameMcpGadgetPolicy.CaptureWidth % 28);
        foreach (var tool in Tools())
            Assert.Null(tool["inputSchema"]!["properties"]!["maxWidth"]);
    }

    [Fact]
    public void ContinueHasNoCallerSelectedSaveOrNativeSurface()
    {
        var run = Tool("game_continue");
        Assert.Null(run["inputSchema"]!["required"]);
        Assert.Empty((JObject)run["inputSchema"]!["properties"]!);
        Assert.False((bool)run["annotations"]!["readOnlyHint"]!);
    }

    [Fact]
    public void TooltipDiscoveryIsBoundedAndSelectorFree()
    {
        var tooltips = Tool("game_tooltips");
        Assert.Null(tooltips["inputSchema"]!["required"]);
        var properties = (JObject)tooltips["inputSchema"]!["properties"]!;
        Assert.Equal(
            new[] { "offset", "limit" },
            properties.Properties().Select(property => property.Name));
        Assert.Null(properties["path"]);
        Assert.Equal(200, (int)properties["limit"]!["maximum"]!);
    }

    [Fact]
    public void TooltipReadAdvertisesCompactProseAndVolatileScreenAddressing()
    {
        var tooltip = Tool("game_tooltip");
        var description = (string?)tooltip["description"];

        Assert.Contains("plain screen text", description, System.StringComparison.Ordinal);
        Assert.Contains("current game_tooltips catalog", description, System.StringComparison.Ordinal);
        Assert.Equal(new[] { "path" },
            tooltip["inputSchema"]!["properties"]!.Children<JProperty>()
                .Select(property => property.Name));
        Assert.Null(tooltip["inputSchema"]!["properties"]!["capture"]);
    }

    [Fact]
    public void NavigationUsesGenericCatalogSelectorsOnly()
    {
        var navigation = Tool("game_navigate");
        var properties = (JObject)navigation["inputSchema"]!["properties"]!;
        Assert.Equal(
            new[] { "screen", "subtab", "uuid" },
            properties.Properties().Select(property => property.Name));
        Assert.Null(properties["operation"]);
        Assert.Null(properties["tabIndex"]);
        Assert.Equal("string", (string?)properties["screen"]!["type"]);
        Assert.Equal("string", (string?)properties["subtab"]!["type"]);
        Assert.Equal(
            "UI-only, no gameplay/save mutation",
            (string?)navigation["classification"]);
        Assert.StartsWith(
            "UI-only, no gameplay/save mutation.",
            (string?)navigation["description"]);
        Assert.False((bool)navigation["annotations"]!["readOnlyHint"]!);
    }

    [Theory]
    [InlineData("World", "Agromancy", true)]
    [InlineData("World", "Aspects", false)]
    [InlineData("Magic", "Agromancy", false)]
    [InlineData("World", null, false)]
    public void PlotSelectionIsAdmittedOnlyOnItsOwningScreen(
        string screen,
        string? subtab,
        bool expected)
    {
        Assert.Equal(
            expected,
            GameMcpGadgetPolicy.IsPlotDestination(screen, subtab));
    }

    [Fact]
    public void NavigationReturnsPerStripDestinationStateWithoutMutationCeremony()
    {
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.Navigation,
            0,
            0,
            "navigate",
            System.Guid.Empty,
            System.Guid.Empty,
            string.Empty, 1,
            string.Empty,
            string.Empty,
            saveCapture: false);
        var terminal = GameMcpCommandResult.Committed(
            "navigation_arrived",
            observedLifecycleGeneration: 12,
            observedConfigurationGeneration: 34,
            details: new GameMcpObjectBuilder
            {
                ["activeScreen"] = "Research",
                ["subtabStrips"] = new GameMcpArrayBuilder(
                    new GameMcpObjectBuilder
                    {
                        ["active"] = "Discover",
                        ["labels"] = new GameMcpArrayBuilder("Discover", "Development"),
                    },
                    new GameMcpObjectBuilder
                    {
                        ["active"] = "Inventory",
                        ["labels"] = new GameMcpArrayBuilder("Inventory", "Concepts"),
                    }),
            }.Freeze());

        var projected = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal("committed", (string?)projected["status"]);
        Assert.Null(projected["mutationScope"]);
        Assert.Null(projected["uiStateMutationAttempts"]);
        Assert.Null(projected["uiStateMutationsCommitted"]);
        Assert.Null(projected["mutationAttempts"]);
        Assert.Null(projected["mutationsCommitted"]);
        Assert.Null(projected["worldGeneration"]);
        Assert.Null(projected["observedLifecycleGeneration"]);
        Assert.Null(projected["observedConfigurationGeneration"]);
        Assert.Null(projected["operation"]);
        Assert.Equal("Research", (string?)projected["activeScreen"]);
        Assert.Null(projected["activeSubtab"]);
        Assert.Null(projected["subtabs"]);
        var strips = projected["subtabStrips"]!.OfType<JObject>().ToArray();
        Assert.Equal(2, strips.Length);
        Assert.Equal("Discover", (string?)strips[0]["active"]);
        Assert.Equal(new[] { "Discover", "Development" }, strips[0]["labels"]!.Values<string>());
        Assert.Equal("Inventory", (string?)strips[1]["active"]);
        Assert.Equal(new[] { "Inventory", "Concepts" }, strips[1]["labels"]!.Values<string>());

        var partial = GameMcpCommandResult.Rejected(
                "subtab_selection_failed",
                "the tab committed before the subtab refused")
            .WithDetails(
                new GameMcpObjectBuilder
                {
                    ["activeScreen"] = "Research",
                    ["subtabCandidates"] = new GameMcpArrayBuilder("Discover", "Development"),
                }.Freeze());
        var partialProjection = GameMcpTestHarness.Json(partial.Project(command));
        Assert.Equal("refused", (string?)partialProjection["status"]);
        Assert.Null(partialProjection["uiStateMutationAttempts"]);
        Assert.Equal("Research", (string?)partialProjection["activeScreen"]);
        Assert.Equal(new[] { "Discover", "Development" },
            partialProjection["subtabCandidates"]!.Values<string>());
    }

    [Fact]
    public void ScreenCatalogGroupsSubtabsUnderTheActiveNamedTab()
    {
        var projected = Plugin.ProjectGameMcpScreenCatalog(
            "Main",
            navigationAvailable: true,
            new[] { ("Magic", false), ("Scholar", true), ("Mods", false) },
            new[]
            {
                ("primary", "Loadout", false),
                ("primary", "Discover", true),
                ("primary", "Research", false),
                ("secondary", "Inventory", true),
                ("secondary", "Concepts", false),
            });

        var json = GameMcpTestHarness.Json(projected);
        Assert.Equal("available", (string?)json["status"]);
        Assert.Equal("Main", (string?)json["scene"]);
        var tabs = json["screens"]!.Values<JObject>().ToArray();
        Assert.Equal(new[] { "Magic", "Scholar", "Mods" },
            tabs.Select(tab => (string)tab["label"]!).ToArray());
        var scholar = tabs[1];
        Assert.True((bool)scholar["active"]!);
        var strips = scholar["subtabStrips"]!.Values<JObject>().ToArray();
        Assert.All(strips, strip => Assert.Null(strip["id"]));
        Assert.Equal("Discover", (string?)strips[0]["active"]);
        Assert.Equal("Inventory", (string?)strips[1]["active"]);
        var encoded = json.ToString(Newtonsoft.Json.Formatting.None);
        Assert.DoesNotContain("index", encoded, System.StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Canvas", encoded, System.StringComparison.Ordinal);
    }

    [Fact]
    public void ContinueSuccessIsFlatSceneAndRuntimePostState()
    {
        var command = new GameMcpCommand(
            1,
            GameMcpCommandKind.ContinueRun,
            0,
            0,
            "continue",
            System.Guid.Empty,
            System.Guid.Empty,
            string.Empty, 1,
            string.Empty,
            string.Empty,
            saveCapture: false);
        var terminal = GameMcpCommandResult.Committed(
            "continue_invoked",
            observedLifecycleGeneration: 2,
            observedConfigurationGeneration: 3,
            details: new GameMcpObjectBuilder
            {
                ["scene"] = "Main",
                ["runtimeAvailable"] = true,
            }.Freeze());

        var projected = GameMcpTestHarness.Json(terminal.Project(command));

        Assert.Equal(new[] { "status", "scene", "runtimeAvailable" },
            projected.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)projected["status"]);
        Assert.Null(projected["code"]);
        Assert.Equal("Main", (string?)projected["scene"]);
        Assert.True((bool)projected["runtimeAvailable"]!);
    }

    [Fact]
    public void ReadAndMutationCommandsUseDisjointStatusVocabulary()
    {
        var inbox = new GameMcpFrameInbox();
        var readOperation = inbox.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = "game_probe",
            Classification = GameMcpOperationClass.ReadOnly,
            Mode = "runtime",
        }.Freeze());
        var mutationOperation = inbox.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = "game_navigate",
            Classification = GameMcpOperationClass.UiState,
            Mode = "navigate",
        }.Freeze());
        var read = Command(GameMcpCommandKind.Probe, "runtime", readOperation);
        var mutation = Command(GameMcpCommandKind.Navigation, "navigate", mutationOperation);

        Assert.Equal("available", (string?)GameMcpTestHarness.Json(
            GameMcpCommandResult.Committed("probe_read", 1, 1).Project(read))["status"]);
        Assert.Equal("unavailable", (string?)GameMcpTestHarness.Json(
            GameMcpCommandResult.Rejected("probe_unavailable", "no data").Project(read))["status"]);
        Assert.Equal("committed", (string?)GameMcpTestHarness.Json(
            GameMcpCommandResult.Committed("navigation_arrived", 1, 1)
                .Project(mutation))["status"]);
        Assert.Equal("refused", (string?)GameMcpTestHarness.Json(
            GameMcpCommandResult.Rejected("screen_match_failed", "no match")
                .Project(mutation))["status"]);
    }

    private static GameMcpCommand Command(
        GameMcpCommandKind kind,
        string mode,
        GameMcpFrameOperation operation) =>
        new(
            operation.Sequence,
            kind,
            0,
            0,
            mode,
            System.Guid.Empty,
            System.Guid.Empty,
            string.Empty, 1,
            string.Empty,
            string.Empty,
            saveCapture: false,
            sourceOperation: operation);

    private static JObject Tool(string name) =>
        Assert.Single(Tools(), value => (string?)value["name"] == name);

    private static JObject[] Tools()
    {
        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox());
        var response = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/list",
            new JObject()));
        return response.Body!["result"]!["tools"]!.Values<JObject>().ToArray()!;
    }
}

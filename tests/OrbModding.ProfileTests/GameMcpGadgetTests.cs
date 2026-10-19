using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using OrbAutomata.GameMcp;
using OrbModding;
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
    /// and it is the answer for every caller.
    /// </summary>
    [Fact]
    public void A_capture_arrives_at_the_readable_width()
    {
        Assert.Equal(896, GameMcpGadgetPolicy.CaptureWidth);
        Assert.Equal(0, GameMcpGadgetPolicy.CaptureWidth % 28);
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
        var tooltips = Tool("game_screen_elements");
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
        Assert.Contains(
            "refresh game_screen_elements after navigation or mutation",
            description,
            System.StringComparison.Ordinal);
        Assert.Equal(new[] { "path", "uuid" },
            tooltip["inputSchema"]!["properties"]!.Children<JProperty>()
                .Select(property => property.Name));
        Assert.Null(tooltip["inputSchema"]!["properties"]!["capture"]);
    }

    /// <summary>
    /// Two address forms, exactly one per call, and neither declared required in the schema —
    /// the pair is a choice the validator enforces, which is the shape <c>world_get</c> already
    /// uses for <c>uuid</c> against <c>uuids</c>.
    /// </summary>
    [Fact]
    public void TooltipReadTakesAPathOrAUuidAndNeitherIsSchemaRequired()
    {
        var tooltip = Tool("game_tooltip");
        Assert.Null(tooltip["inputSchema"]!["required"]);
        Assert.Equal(
            "A published entity id, as any row or link line of this surface prints it. The one " +
            "element on this screen about that entity answers, the entity's own tooltip answers " +
            "where none is, and several refuse naming their paths.",
            (string?)tooltip["inputSchema"]!["properties"]!["uuid"]!["description"]);
    }

    /// <summary>
    /// The schema said "a path exactly as the catalog returned it", which cannot resolve for a row
    /// under a <c>pathComponent</c>: that row's <c>path</c> is a bare <c>[3]</c>, and no live path
    /// contains <c>/[</c>, so the requested string can never match. The maintained doc already
    /// stated the join rule; the sentence a caller actually reads now states it too.
    /// </summary>
    [Fact]
    public void TheAddressSchemaStatesTheJoinRuleAComponentRowIsAddressedBy()
    {
        Assert.Equal(
            "A path as game_screen_elements returned it, joined onto what its row did not " +
            "repeat: a row under a pathComponent is addressed by that component and its own " +
            "index joined, never by the bare index. Any longer tail of the same path is " +
            "accepted, including the whole path.",
            (string?)Tool("game_tooltip")["inputSchema"]!["properties"]!["path"]!["description"]);
    }

    /// <summary>
    /// The catalog is an address book and says so: it lists what can be hovered, it mints the only
    /// address the reader takes, and it carries none of the text. A round that read the two names
    /// as a plural and a singular of one idea called this one twice and the reader zero times.
    /// </summary>
    [Fact]
    public void TheCatalogAdvertisesThatItMintsTheAddressesAndCarriesNoText()
    {
        var description = (string?)Tool("game_screen_elements")["description"];

        Assert.Contains(
            "an address book of paths, names, ids and slots, with none of their text",
            description,
            System.StringComparison.Ordinal);
        Assert.Contains(
            "this is the only verb that mints a path, and game_tooltip reads what one of them says",
            description,
            System.StringComparison.Ordinal);
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
    [InlineData("World", "Agromancy", "Plot")]
    [InlineData("World", "Aspects", "None")]
    [InlineData("Magic", "Agromancy", "None")]
    [InlineData("World", null, "None")]
    [InlineData("Magic", "Unlock", "None")]
    [InlineData("Magic", "Spells", "Spell")]
    [InlineData("Magic", "Loadout", "None")]
    [InlineData("Loadout", "Spells", "None")]
    [InlineData("Magic", null, "None")]
    public void TileSelectionIsAdmittedOnlyOnItsOwningScreen(
        string screen,
        string? subtab,
        string expected)
    {
        Assert.Equal(expected, GameMcpGadgetPolicy.TileAt(screen, subtab).ToString());
    }

    /// <summary>
    /// A nested destination is one path, and the leaf is the page the caller asked to be on.
    /// </summary>
    /// <remarks>
    /// Spellbook > Loadout and Augments > Glyphcraft each cost round fourteen two calls with a
    /// guess between them, because a strip is only in the catalog while its parent is active:
    /// asking for Loadout from the Augments strip refused with eight candidates, none of them
    /// Loadout. Parent first, child second, inside one action.
    /// </remarks>
    [Theory]
    [InlineData("Loadout", new[] { "Loadout" }, "Loadout")]
    [InlineData("Spellbook/Loadout", new[] { "Spellbook", "Loadout" }, "Loadout")]
    [InlineData("Augments / Glyphcraft", new[] { "Augments", "Glyphcraft" }, "Glyphcraft")]
    [InlineData("/Spellbook//Unlock/", new[] { "Spellbook", "Unlock" }, "Unlock")]
    public void ANestedSubtabIsOnePathOfClicksWithTheLeafAsTheDestination(
        string asked,
        string[] expectedSegments,
        string expectedLeaf)
    {
        var selector = new GameMcpNavigationSelector(asked);

        Assert.Equal(asked, selector.Label);
        Assert.Equal(expectedSegments, selector.Labels.ToArray());
        Assert.Equal(expectedLeaf, selector.Leaf);
    }

    [Fact]
    public void NavigationReturnsPerStripDestinationState()
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

        Assert.Equal(new[] { "status", "activeScreen", "subtabStrips" },
            projected.Properties().Select(property => property.Name));
        Assert.Equal("committed", (string?)projected["status"]);
        Assert.Equal("Research", (string?)projected["activeScreen"]);
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
        var projected = GameMcpScreenCatalogProjection.ProjectGameMcpScreenCatalog(
            "Main",
            navigationAvailable: true,
            new[]
            {
                ("Magic", false, false),
                ("Scholar", true, false),
                ("Mods", false, false),
            },
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
    }

    /// <summary>
    /// Every screen row says whether the game has unlocked it, open rows included.
    /// </summary>
    /// <remarks>
    /// A young run's rail holds a button for all eight screens while the game draws three, so a
    /// catalog of labels alone reported five destinations the game will not show — and round
    /// fourteen's <c>game_navigate</c> repeated all eight back as candidates. The column is on
    /// every row rather than only the locked ones: a header that promised it owes it to each row,
    /// and a missing marker would otherwise read as "unknown".
    /// </remarks>
    [Fact]
    public void EveryScreenRowSaysWhetherTheGameHasUnlockedIt()
    {
        var projected = GameMcpScreenCatalogProjection.ProjectGameMcpScreenCatalog(
            "Main",
            navigationAvailable: true,
            new[]
            {
                ("Magic", true, false),
                ("Rituals", false, true),
                ("Mods", false, false),
            },
            System.Array.Empty<(string, string, bool)>());

        var json = GameMcpTestHarness.Json(projected);
        var tabs = json["screens"]!.Values<JObject>().ToArray();
        Assert.Equal(new[] { "Magic", "Rituals", "Mods" },
            tabs.Select(tab => (string)tab["label"]!).ToArray());
        Assert.All(tabs, tab => Assert.NotNull(tab["locked"]));
        Assert.Equal(new[] { false, true, false },
            tabs.Select(tab => (bool)tab["locked"]!).ToArray());
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

    /// <summary>
    /// A load slower than the verb's wait says the press landed. The answer that got somewhere is
    /// unchanged: a scene that has moved is its own proof and pays nothing for the clause.
    /// </summary>
    /// <remarks>
    /// A round called Continue during a slow load, read back the scene it had called from, and
    /// could not tell the press from a no-op. It spent a health round-trip on that and wrote a
    /// wrong finding which stood for a day, until a screenshot showed the press had landed and the
    /// screen was already black behind the loading spinner.
    /// </remarks>
    [Fact]
    public void A_continue_whose_scene_has_not_moved_yet_still_says_the_press_landed()
    {
        var loading = GameMcpTestHarness.Json(
            GameMcpContinueProjection.Project("Start", false));
        var loaded = GameMcpTestHarness.Json(
            GameMcpContinueProjection.Project("Main", true));

        // The press that landed no longer carries a sentence about the suite not having read the
        // game yet: the load this very press started is what makes that read possible, so it was
        // false by the time anyone could read it.
        Assert.Equal(
            new[] { "pressed", "scene", "runtimeAvailable" },
            loading.Properties().Select(property => property.Name));
        Assert.Equal(
            "Continue landed; the scene has not changed yet", (string?)loading["pressed"]);
        Assert.Equal(
            new[] { "scene", "runtimeAvailable" },
            loaded.Properties().Select(property => property.Name));
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

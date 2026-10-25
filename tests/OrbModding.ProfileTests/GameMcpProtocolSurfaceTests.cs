using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpProtocolSurfaceTests
{
    [Fact]
    public void TheToolListAdvertisesTheVisualAndDetailVerbs()
    {
        var names = GameMcpAcceptanceFixture.ToolNames();
        Assert.Contains("trace_health", names);
        Assert.Contains("game_screen_catalog", names);
        Assert.Contains("game_navigate", names);
        Assert.Contains("game_screen_elements", names);
        Assert.Contains("game_tooltip", names);
        Assert.Contains("game_screenshot", names);
        Assert.Contains("world_get", names);
    }

    /// <summary>
    /// Four verbs were named for something other than the button they press.
    /// </summary>
    [Fact]
    public void Every_renamed_verb_title_says_what_it_presses()
    {
        var tools = GameMcpAcceptanceFixture.Tools();

        Assert.Equal(
            "Level a glyph, artifact type, resource type or Time Rune",
            Title(tools, "game_level_up"));
        Assert.Equal("Confirm a spell's mastery", Title(tools, "game_spell_mastery"));

        // The pair teaches itself: one lists what is on the screen and mints the addresses, the
        // other reads what one of them says.
        Assert.Equal("List the screen's hoverable elements", Title(tools, "game_screen_elements"));
        Assert.Equal("Read one element's tooltip text", Title(tools, "game_tooltip"));

        Assert.Equal("Read or flip the suite's seven breakers", Title(tools, "suite_breakers"));
    }

    /// <summary>
    /// The verb is named for Mastery Lv and says so: Spell Lv is the global casting dial, and a
    /// description that let the two share a word is what put the wrong screen number on the tool.
    /// </summary>
    [Fact]
    public void Confirming_mastery_names_the_button_it_presses_and_the_dial_it_does_not_move()
    {
        var description = Description(GameMcpAcceptanceFixture.Tools(), "game_spell_mastery");

        Assert.Equal(
            "Press Confirm Mastery for one spell, which raises the Mastery Lv its card shows. " +
            "mode=all presses the native Level All Spells sweep instead, which walks the whole " +
            "spellbook and skips only the spells it cannot afford. Spell Lv is the global casting " +
            "dial and is never moved here; that is game_casting_dial.",
            description);
    }

    private static string Title(IReadOnlyList<JObject> tools, string name) =>
        (string)Assert.Single(tools, tool => (string?)tool["name"] == name)["title"]!;

    private static string Description(IReadOnlyList<JObject> tools, string name) =>
        (string)Assert.Single(tools, tool => (string?)tool["name"] == name)["description"]!;

    [Fact]
    public void ThePurchaseVerbRequiresTheIdAndTheAmount()
    {
        var tools = GameMcpAcceptanceFixture.Tools();
        var purchase = Assert.Single(
            tools,
            tool => (string?)tool["name"] == "game_purchase");
        Assert.Equal("Purchase an attribute or upgrade", (string?)purchase["title"]);
        Assert.Contains("native StructureSO", (string?)purchase["description"]);
        var required = purchase["inputSchema"]!["required"]!.Values<string>().ToArray();
        Assert.Equal(new[] { "uuid", "amount" }, required);

        var screenshot = Assert.Single(
            tools,
            tool => (string?)tool["name"] == "game_screenshot");
        Assert.Null(screenshot["inputSchema"]!["required"]);
    }

    /// <summary>
    /// The detail read takes an id and nothing else is required.
    /// </summary>
    [Fact]
    public void DetailReadRequiresNoCategoryAndHasNoIdAlias()
    {
        var detail = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            tool => (string?)tool["name"] == "world_get");
        Assert.Null(detail["inputSchema"]!["required"]);
        var properties = Assert.IsType<JObject>(detail["inputSchema"]!["properties"]);
        Assert.NotNull(properties["uuid"]);
        Assert.NotNull(properties["uuids"]);
        Assert.NotNull(properties["category"]);
        Assert.Null(properties["id"]);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var invalid = router.Handle(GameMcpAcceptanceFixture.Request(
            1,
            "tools/call",
            new JObject
            {
                ["name"] = "world_get",
                ["arguments"] = new JObject { ["uuid"] = "not-a-guid" },
            }));
        Assert.Equal(
            "refused (ERR_INPUT): uuid must be a whole canonical UUID or an id handle " +
            "that names one published entity", GameMcpTestHarness.Page(invalid));
    }

    [Fact]
    public void TraceHealthIsWriterHealthOnly()
    {
        var result = GameMcpAcceptanceFixture.CallText("trace_health");
        Assert.StartsWith("unavailable\n", result, StringComparison.Ordinal);
        Assert.Contains("reason: the decision journal writer is not active", result,
            StringComparison.Ordinal);
        Assert.DoesNotContain("scope", result, StringComparison.Ordinal);
        Assert.DoesNotContain("events", result, StringComparison.Ordinal);
        Assert.DoesNotContain("worldGeneration", result, StringComparison.Ordinal);
        Assert.DoesNotContain("cursor", result, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthSaysWhyAHeldAutomationIsIdle()
    {
        var concept = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "AutoConcept"),
            "Auto Concept",
            configuredEnabled: true,
            FeatureStatusState.TemporarilyBlocked,
            new FeatureStatusReason(
                FeatureStatusReasonCode.TemporarySafetyBlock,
                "Auto Concept would rotate Analyze Space out for Analyze Essence, but Psi is at zero."),
            lifecycleGeneration: 9);
        var stopped = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "AutoBuy"),
            "Auto Buy",
            configuredEnabled: true,
            FeatureStatusState.TemporarilyBlocked,
            new FeatureStatusReason(
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active."),
            lifecycleGeneration: 9);

        var compact = GameMcpHealthProjection.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(features: new[] { concept, stopped }));

        Assert.Contains(
            "\nauto_concept reason: Auto Concept would rotate Analyze Space out for Analyze Essence, but Psi is at zero.",
            compact,
            StringComparison.Ordinal);
        Assert.DoesNotContain("auto_buy reason: ", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthSaysWhyAFaultedAutomationStopped()
    {
        var mentor = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "Mentor"),
            "Orb Mentor",
            configuredEnabled: true,
            FeatureStatusState.Faulted,
            new FeatureStatusReason(
                FeatureStatusReasonCode.PostconditionFailed,
                "The experience grant left Mentor experience unchanged."),
            lifecycleGeneration: 9);

        var compact = GameMcpHealthProjection.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(features: new[] { mentor }));

        Assert.Contains(
            "\nmentor reason: The experience grant left Mentor experience unchanged.",
            compact,
            StringComparison.Ordinal);
    }

    [Fact]
    public void HealthHasOneCanonicalShapeAndRejectsDetailOptions()
    {
        var feature = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "AutoBuy"),
            "Auto Buy",
            configuredEnabled: false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(
                FeatureStatusReasonCode.ConfigurationDisabled,
                "disabled"),
            lifecycleGeneration: 9);
        var mentor = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "Mentor"),
            "Orb Mentor",
            configuredEnabled: true,
            FeatureStatusState.Operational,
            new FeatureStatusReason(FeatureStatusReasonCode.None, string.Empty),
            lifecycleGeneration: 9);
        var context = GameMcpTestHarness.Context(features: new[] { feature, mentor });
        var compact = GameMcpHealthProjection.ProjectGameMcpHealthText(context);
        Assert.StartsWith("available\n", compact, StringComparison.Ordinal);
        // The fingerprint answers "same DLL or not", which twelve hex characters settle as well as
        // sixty-four did — and this is a line every health call pays for.
        Assert.Matches(
            @"(?m)^build: \S+ dll sha256 [0-9a-f]{12}$",
            compact);
        // One name per feature across the two verbs that list features, so the seven suite_breakers
        // takes as arguments are recognisable inside the nine health reports on.
        // The whole roster is one line: the state groups are the facts, the repeated leading word
        // was not.
        Assert.Matches(
            @"(?m)^features configuration_disabled: auto_buy; operational: mentor$",
            compact);
        // With no runtime, its one line speaks for every verb the runtime answers.
        Assert.Contains("runtime: unavailable", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("game_craft", compact, StringComparison.Ordinal);
        Assert.Contains("game_modal: unavailable", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("Orb Mentor", compact, StringComparison.Ordinal);
        Assert.DoesNotContain("mailbox", compact, StringComparison.Ordinal);

        var modalAvailable = new GameMcpFrameContext(
            world: null,
            runtime: null,
            configuration: context.Configuration,
            lifecycleGeneration: 9,
            sceneName: "Main",
            nativeContractsAvailable: true,
            featureStatuses: Array.Empty<FeatureStatusSnapshot>(),
            traceWriterStatus: DecisionJournalStatus.Unavailable,
            traceWriterRevision: 0,
            writableConfiguration: Array.Empty<GameMcpWritableSettingDescriptor>(),
            modalDismissAvailable: true);
        var withoutRuntime = GameMcpHealthProjection.ProjectGameMcpHealthText(modalAvailable);
        // Health names exceptions, the way it already does for features and services. A capability
        // that works is covered by the leading verdict, so it costs no line at all.
        Assert.DoesNotContain("game_modal", withoutRuntime, StringComparison.Ordinal);
        Assert.DoesNotContain("native contracts", withoutRuntime, StringComparison.Ordinal);
        // The settings the load normalizes are ones no caller chose, so a normalization that landed
        // says nothing and one that did not names itself here rather than only in the log.
        Assert.DoesNotContain("agent settings", withoutRuntime, StringComparison.Ordinal);
        Assert.Contains(
            "agent settings: Research Queue Mode did not stay on after the setting was written",
            GameMcpHealthProjection.ProjectGameMcpHealthText(new GameMcpFrameContext(
                world: null,
                runtime: null,
                configuration: context.Configuration,
                lifecycleGeneration: 9,
                sceneName: "Main",
                nativeContractsAvailable: true,
                featureStatuses: Array.Empty<FeatureStatusSnapshot>(),
                traceWriterStatus: DecisionJournalStatus.Unavailable,
                traceWriterRevision: 0,
                writableConfiguration: Array.Empty<GameMcpWritableSettingDescriptor>(),
                modalDismissAvailable: true,
                agentSettingsFailure:
                    "Research Queue Mode did not stay on after the setting was written")),
            StringComparison.Ordinal);

        // The runtime outlives every scene change, so a scene name alone answered the same question
        // both ways in one session. The absent runtime is named as a session fact, and the world the
        // verdict describes is identified.
        Assert.Contains("world: not published", withoutRuntime, StringComparison.Ordinal);
        Assert.Contains(
            "runtime reason: the suite has not begun reading the game in this session; " +
            "open a save and try again.",
            withoutRuntime,
            StringComparison.Ordinal);
        // A collected world always carries the moment it was read; health answers published exactly
        // when the world readers do, so the fixture has to be a world they would serve.
        var withWorld = GameMcpHealthProjection.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(
                new GameWorldState
                {
                    CollectedAtUtcTicks = DateTime.UtcNow.Ticks,
                    IntVariables = WorldTable.Create(new WorldNumberVariable(
                        KnownEntities.WorldResets.Uuid, new BigDouble(14d), isPercent: false)),
                },
                generation: 1207));
        // Two counters, two names: the suite's transitions since the plugin loaded, and the save's
        // own reset count under the word the game prints over it.
        Assert.Contains(
            "lifecycle: Playing, lifecycleTransitions 9, worldResets 14",
            withWorld,
            StringComparison.Ordinal);
        Assert.Contains("world: publication 1207", withWorld, StringComparison.Ordinal);
        Assert.DoesNotContain("world: generation", withWorld, StringComparison.Ordinal);

        var tool = Assert.Single(
            GameMcpAcceptanceFixture.Tools(),
            candidate => (string?)candidate["name"] == "suite_health");
        Assert.Empty((JObject)tool["inputSchema"]!["properties"]!);

        var router = new GameMcpProtocolRouter(new GameMcpFrameInbox(), GameMcpToolCatalog.Shipping);
        var rejected = router.Handle(GameMcpAcceptanceFixture.Request(
            99,
            "tools/call",
            new JObject
            {
                ["name"] = "suite_health",
                ["arguments"] = new JObject { ["detail"] = "AutoBuy" },
            }));
        Assert.Equal(
            "refused (ERR_INPUT): tool arguments failed schema validation: field " +
            "'detail' is not accepted by suite_health", GameMcpTestHarness.Page(rejected));
    }

    /// <summary>
    /// An Auto Buy invariant trip read only "faulted (invariant_violation): auto_buy" would leave the
    /// player guessing which purchase the game would not take. The sentence the latch logged names
    /// the candidate, the refusal and the bundle, so health carries it.
    /// </summary>
    [Fact]
    public void HealthSaysWhyAFeatureHoldsOffForTheLifecycle()
    {
        const string summary =
            "Auto Buy planned a purchase the game would not take (Upgrade Improved Brewing " +
            "[ImprovedBrewing] (3357c64a-f19b-4a03-8819-c1622081dd7b)): refused by an unreadable " +
            "admission term. Diagnostic bundle: trace/diagnostics/autobuy-refusal-20261017-063254172.txt. " +
            "Auto Buy holds off until the next load, reset or prestige checks the game again.";
        var autoBuy = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "AutoBuy"),
            "Auto Buy",
            configuredEnabled: true,
            FeatureStatusState.Faulted,
            new FeatureStatusReason(FeatureStatusReasonCode.InvariantViolation, summary),
            lifecycleGeneration: 9);
        var autoHarvest = new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, "AutoHarvest"),
            "Auto Harvest",
            configuredEnabled: false,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "disabled"),
            lifecycleGeneration: 9);

        var health = GameMcpHealthProjection.ProjectGameMcpHealthText(
            GameMcpTestHarness.Context(features: new[] { autoBuy, autoHarvest }));

        Assert.Matches(
            @"(?m)^features configuration_disabled: auto_harvest; " +
            @"faulted \(invariant_violation\): auto_buy$",
            health);
        Assert.Contains("\nauto_buy reason: " + summary, health, StringComparison.Ordinal);
        Assert.DoesNotContain("auto_harvest reason", health, StringComparison.Ordinal);
    }

    [Fact]
    public void TextToolsReturnOnlyTheAgentReadableTextContent()
    {
        var result = GameMcpToolExecution.Text(
            "scene: Main\ntabs:\n    Magic\n  * Scholar\n    subtabs:\n      * Discover")
            .ToProtocolResult();

        Assert.Null(result["structuredContent"]);
        Assert.Null(result["isError"]);
        var content = Assert.Single(result["content"]!.Values<JObject>())!;
        Assert.Equal("text", (string?)content["type"]);
        Assert.Equal(
            "scene: Main\ntabs:\n    Magic\n  * Scholar\n    subtabs:\n      * Discover",
            (string?)content["text"]);
    }
}

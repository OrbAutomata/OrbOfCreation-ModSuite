#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using OrbModding.Common.Runtime.World;
using static OrbAutomata.GameMcp.GameMcpOperationClass;
using static OrbAutomata.GameMcp.GameMcpProtocolRouter;
using static OrbAutomata.GameMcp.GameMcpToolRoute;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpToolCatalog
{
    private const GameMcpFrameData World = GameMcpFrameData.World;
    private const GameMcpFrameData None = GameMcpFrameData.None;
    private const GameMcpFrameData GameplayData =
        GameMcpFrameData.World | GameMcpFrameData.Configuration;
    private const GameMcpFrameData HealthData = GameMcpFrameData.World |
        GameMcpFrameData.Configuration | GameMcpFrameData.FeatureHealth |
        GameMcpFrameData.ServiceHealth | GameMcpFrameData.Scene |
        GameMcpFrameData.NativeContractHealth;
    private const GameMcpFrameData ConfigurationData =
        GameMcpFrameData.Configuration | GameMcpFrameData.WritableConfiguration;

    // The world too: what each category cost the pass that produced it travels on the
    // publication, and that is the half of trace health a session driving the game can act on.
    private const GameMcpFrameData TraceData =
        GameMcpFrameData.TraceWriterHealth | GameMcpFrameData.World;
    private const GameMcpFrameData UiData = GameMcpFrameData.World | GameMcpFrameData.Scene;
    private const string CategoryUriPrefix = "orb://world/category/";

    private readonly Dictionary<string, GameMcpToolDescriptor> _tools =
        new(StringComparer.Ordinal);

    internal GameMcpToolCatalog(
        IReadOnlyList<GameMcpToolDescriptor> tools,
        IReadOnlyList<GameMcpResourceDescriptor> resources)
    {
        Tools = tools ?? throw new ArgumentNullException(nameof(tools));
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        for (var index = 0; index < tools.Count; index++)
            if (!_tools.ContainsKey(tools[index].Name)) _tools.Add(tools[index].Name, tools[index]);
    }

    private static readonly Action<JObject, GameMcpOperationRequestBuilder> NoArguments =
        static (_, _) => { };

    internal static GameMcpToolCatalog Shipping { get; } =
        new(ShippingTools(), ShippingResources());

    internal IReadOnlyList<GameMcpToolDescriptor> Tools { get; }
    internal IReadOnlyList<GameMcpResourceDescriptor> Resources { get; }

    internal bool TryFind(string name, out GameMcpToolDescriptor tool) =>
        _tools.TryGetValue(name, out tool!);

    internal GameMcpOperationRequest BuildOperation(string name, JObject arguments)
    {
        if (!TryFind(name, out var tool))
            throw new GameMcpInvalidParamsException(
                "unknown tool '" + name + "'; call tools/list",
                "unknown_tool");
        var builder = new GameMcpOperationRequestBuilder
        {
            ToolName = name,
            Limit = GameMcpWorldQuery.DefaultLimit,
            Amount = 1,
        };
        tool.Bind(arguments, builder);
        builder.Route = tool.RouteFor(builder);
        return builder.Freeze();
    }

    internal bool TryFindResource(string uri, out GameMcpResourceDescriptor resource)
    {
        for (var index = 0; index < Resources.Count; index++)
        {
            if (!Resources[index].Matches(uri)) continue;
            resource = Resources[index];
            return true;
        }
        resource = null!;
        return false;
    }

    internal string ToolFor(GameMcpCommandKind kind)
    {
        var reachedByMode = string.Empty;
        for (var index = 0; index < Tools.Count; index++)
        {
            var tool = Tools[index];
            for (var route = 0; route < tool.Routes.Count; route++)
            {
                if (tool.Routes[route].Kind != kind) continue;
                if (tool.Routes[route].IsDefault) return tool.Name;
                if (reachedByMode.Length == 0) reachedByMode = tool.Name;
            }
        }
        return reachedByMode;
    }

    internal IReadOnlyList<string> Problems(IEnumerable<string> advertisedTools)
    {
        var problems = new List<string>();
        var registered = new HashSet<string>(StringComparer.Ordinal);
        var advertised = new HashSet<string>(advertisedTools, StringComparer.Ordinal);
        for (var index = 0; index < Tools.Count; index++)
        {
            var tool = Tools[index];
            if (!registered.Add(tool.Name)) problems.Add(tool.Name + " is registered twice");
            if (!advertised.Contains(tool.Name))
                problems.Add(tool.Name + " is registered but tools/list does not advertise it");
            if (tool.Routes.Count == 0) problems.Add(tool.Name + " has no route");
            for (var route = 0; route < tool.Routes.Count; route++)
                AddRouteProblems(problems, tool, route);
        }
        foreach (var name in advertised)
        {
            if (!registered.Contains(name))
                problems.Add(name + " is advertised by tools/list but has no descriptor");
        }

        var uris = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Resources.Count; index++)
        {
            if (!uris.Add(Resources[index].Uri))
                problems.Add("resource " + Resources[index].Uri + " is registered twice");
        }
        return problems;
    }

    private static void AddRouteProblems(
        List<string> problems,
        GameMcpToolDescriptor tool,
        int index)
    {
        var route = tool.Routes[index];
        var last = index == tool.Routes.Count - 1;
        if (last && !route.IsDefault)
            problems.Add(tool.Name + " ends on a conditional route, so a request it skips has none");
        if (!last && route.IsDefault)
            problems.Add(tool.Name + " has an unconditional route before its last, so the routes " +
                "after it are never taken");
        if (route.Kind is not { } kind)
        {
            if (route.Classification == Gameplay)
                problems.Add(tool.Name + " declares a read Gameplay, but only a command kind " +
                    "an action family names can be gameplay");
            return;
        }
        var familyNamed = GameMcpCommandKinds.IsGameplayAction(kind);
        if (familyNamed == (route.Classification == Gameplay)) return;
        problems.Add(familyNamed
            ? tool.Name + " declares " + kind + " " + route.Classification +
                ", but a gameplay action family names " + kind + ", so it must be Gameplay"
            : tool.Name + " declares " + kind + " Gameplay, but no gameplay action family names " +
                kind);
    }

    private static GameMcpToolDescriptor[] ShippingTools() => new[]
    {
        new GameMcpToolDescriptor(
            "world_overview",
            NoArguments,
            FrameRead(ReadOnly, World, Overview)),
        new GameMcpToolDescriptor(
            "world_categories",
            NoArguments,
            FrameRead(ReadOnly, World, Categories)),
        new GameMcpToolDescriptor(
            "world_list",
            static (arguments, builder) =>
            {
                builder.Category = RequireString(arguments, "category");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                builder.LimitFromCaller = arguments.ContainsKey("limit");
                builder.AffordableOnly = OptionalBool(arguments, "affordable", false);
                if (arguments.ContainsKey("discovered"))
                    builder.DiscoveredFilter = RequireBool(arguments, "discovered");
            },
            FrameRead(ReadOnly, World, static (request, context) =>
                GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                    context,
                    request.Category,
                    request.Offset,
                    request.Limit,
                    request.AffordableOnly,
                    request.LimitFromCaller,
                    request.DiscoveredFilter).Freeze()))),
        new GameMcpToolDescriptor(
            "world_get",
            static (arguments, builder) =>
            {
                if (arguments.ContainsKey("category"))
                    builder.Category = RequireString(arguments, "category");
                if (arguments.ContainsKey("uuids"))
                    builder.Uuids = ReadEntityIdArray(
                        arguments, "uuids", GameMcpWorldQuery.MaximumBatchSize);
                else
                    builder.Uuids = new[] { RequireUuid(arguments, "uuid").ToString("D") };
            },
            static (arguments, errors) =>
            {
                var hasBatch = arguments.ContainsKey("uuids");
                var hasSingle = arguments.ContainsKey("uuid");
                if (!hasBatch && !hasSingle)
                    errors.Add("world_get requires uuids (array) or uuid (singular alias)");
                else if (hasBatch && hasSingle)
                    errors.Add("world_get accepts uuid or uuids, not both");
            },
            FrameRead(ReadOnly, World, static (request, context) =>
                GameMcpToolExecution.Read(
                    GameMcpWorldQuery.GetRows(
                        context,
                        request.Category,
                        request.Uuids).Freeze()))),
        new GameMcpToolDescriptor(
            "world_search",
            static (arguments, builder) =>
            {
                // A query is one way to name what you want and a filter is another, so neither is
                // required on its own. Demanding a query to reach a filter taught a round to invent
                // one-letter searches whose reach nobody could state.
                if (arguments.ContainsKey("query"))
                    builder.Query = RequireString(arguments, "query");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                builder.LimitFromCaller = arguments.ContainsKey("limit");
                if (arguments.ContainsKey("category"))
                    builder.Category = RequireString(arguments, "category");
                if (arguments.ContainsKey("state"))
                {
                    builder.StateFilter = RequireOneOf(
                        arguments, "state", "locked", "available", "completed");
                }
                if (arguments.ContainsKey("run"))
                {
                    builder.RunFilter = RequireOneOf(
                        arguments, "run", "idle", "queued", "active", "passed", "failed");
                }
                if (arguments.ContainsKey("discovered"))
                    builder.DiscoveredFilter = RequireBool(arguments, "discovered");
                if (arguments.ContainsKey("keyword"))
                    builder.KeywordFilter = RequireString(arguments, "keyword");
                if (arguments.ContainsKey("modifies"))
                    builder.ModifiesFilter = RequireString(arguments, "modifies");
            },
            FrameRead(ReadOnly, World, static (request, context) =>
                GameMcpToolExecution.Read(
                    GameMcpWorldQuery.Search(
                        context,
                        request.Query,
                        request.Offset,
                        request.Limit,
                        request.Category,
                        request.StateFilter,
                        request.RunFilter,
                        request.KeywordFilter,
                        request.LimitFromCaller,
                        request.DiscoveredFilter,
                        request.ModifiesFilter).Freeze()))),
        new GameMcpToolDescriptor(
            "strategist",
            static (arguments, builder) =>
            {
                if (arguments.ContainsKey("section"))
                    builder.Section = RequireOneOf(
                        arguments, "section", "goals", "position", "demand", "facts");
            },
            FrameRead(
                ReadOnly,
                GameMcpFrameData.World | GameMcpFrameData.ServiceHealth,
                static (request, context) => GameMcpStrategistQuery.Read(context, request.Section))),
        new GameMcpToolDescriptor(
            "suite_health",
            NoArguments,
            FrameRead(ReadOnly, HealthData, SuiteHealth)),
        new GameMcpToolDescriptor(
            "suite_configuration",
            static (arguments, builder) =>
            {
                builder.Mode = arguments.ContainsKey("mode")
                    ? RequireOneOf(arguments, "mode", "list", "describe")
                    : "list";
                if (arguments.ContainsKey("section"))
                    builder.Section = RequireString(arguments, "section");
            },
            FrameRead(ReadOnly, ConfigurationData, SuiteConfiguration)),
        new GameMcpToolDescriptor(
            "trace_health",
            NoArguments,
            FrameRead(ReadOnly, TraceData, TraceHealth)),
        new GameMcpToolDescriptor(
            "suite_check_game_math",
            NoArguments,
            ExecutorRead(
                SuiteAdministration,
                None,
                static (executor, _) => executor.RunGameMcpGameMathCheck())),
        new GameMcpToolDescriptor(
            "game_purchase",
            static (arguments, builder) =>
            {
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Purchase)),
        new GameMcpToolDescriptor(
            "game_cast",
            static (arguments, builder) =>
            {
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Mode = RequireOneOf(
                    arguments, "mode", "fire", "release", "toggle_off");
                builder.SlotIndex = RequiredInt(arguments, "slot", 1, 256);
                if (builder.Mode == "fire" && OptionalBool(arguments, "charge", false))
                    builder.SerializedValue = "charge";
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Cast)),
        new GameMcpToolDescriptor(
            "game_concept",
            static (arguments, builder) =>
            {
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Mode = RequireOneOf(arguments, "mode", "add", "remove");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1_000_000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Concept)),
        new GameMcpToolDescriptor(
            "game_agromancy",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "add_plot_action", "remove_plot_action",
                    "add_element", "remove_element", "add_element_action",
                    "remove_element_action", "set_druidry_level");
                if (builder.Mode == "set_druidry_level")
                {
                    builder.Key = "druidry";
                    builder.Amount = RequiredInt(
                        arguments, "level", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
                    return;
                }
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.SecondaryUuid = builder.Mode is "add_plot_action" or
                    "remove_plot_action" or "add_element_action" or
                    "remove_element_action"
                    ? RequireUuid(arguments, "actionUuid")
                    : Guid.Empty;
                builder.Amount = RequiredInt(arguments, "amount", 1, 10_000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Harvest,
                static request => request.Mode is "add_plot_action" or "remove_plot_action"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellComposition,
                static request => request.Mode == "set_druidry_level"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.HarvestLifecycle)),
        new GameMcpToolDescriptor(
            "game_structure",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "enable", "disable");
                builder.Uuid = RequireUuid(arguments, "uuid");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.StructureLifecycle)),
        new GameMcpToolDescriptor(
            "game_spell_mastery",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "single", "all");
                builder.Uuid = builder.Mode == "single"
                    ? RequireUuid(arguments, "uuid")
                    : Guid.Empty;
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellLevel)),
        new GameMcpToolDescriptor(
            "game_casting_dial",
            static (arguments, builder) =>
            {
                builder.Key = RequireOneOf(arguments, "dial", "output", "reserve");
                builder.Mode = builder.Key == "output" ? "set_output_level" : "set_reserve_level";
                builder.Amount = RequiredInt(
                    arguments, "value", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellComposition)),
        new GameMcpToolDescriptor(
            "game_spell_loadout",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(
                    arguments, "mode", "staged", "preview", "add", "remove", "move");
                if (builder.Mode is "preview" or "add")
                {
                    builder.Uuid = RequireUuid(arguments, "uuid");
                    builder.UuidCounts = RequireGlyphLayout(arguments);
                }
                else if (builder.Mode is "remove" or "move")
                    builder.Amount = RequiredInt(arguments, "slot", 1, 256);
                if (builder.Mode == "move")
                    builder.SlotIndex = RequiredInt(arguments, "destination", 1, 256);
            },
            ExecutorRead(ReadOnly, GameplayData,
                static (executor, request) => executor.PreviewSpellWorkbench(request),
                static request => request.Mode == "preview"),
            ExecutorRead(ReadOnly, None,
                static (executor, _) => executor.ReadStagedSpellWorkbench(),
                static request => request.Mode == "staged"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellWorkbench,
                static request => request.Mode == "add"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellLoadout)),
        new GameMcpToolDescriptor(
            "game_targeting",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "submit", "randomize");
                if (builder.Mode == "submit") builder.Uuid = RequireUuid(arguments, "uuid");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Targeting)),
        new GameMcpToolDescriptor(
            "game_consumable",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(
                    arguments,
                    "mode",
                    "use",
                    "cancel",
                    "discard",
                    "set_randomization",
                    "move");
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "discard")
                    builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
                if (builder.Mode == "set_randomization")
                    builder.SerializedValue = OptionalBool(arguments, "enabled", false)
                        ? "true"
                        : "false";
                if (builder.Mode == "move")
                {
                    builder.Key = RequireOneOf(arguments, "list", "inventory", "hotbar");
                    builder.SlotIndex = RequiredInt(arguments, "destination", 1, int.MaxValue);
                }
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Consumable)),
        new GameMcpToolDescriptor(
            "game_craft",
            static (arguments, builder) =>
            {
                builder.Mode = arguments.ContainsKey("mode")
                    ? RequireOneOf(arguments, "mode", "craft", "automate",
                        "cancel_manual", "cancel_automation")
                    : "craft";
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "craft" &&
                    arguments.TryGetValue("amount", out var craftAmount) &&
                    craftAmount.Type == JTokenType.String)
                {
                    if (craftAmount.Value<string>() != "max")
                        throw new GameMcpInvalidParamsException(
                            "amount must be a whole number of crafts or \"max\"");
                    builder.SerializedValue = "max";
                    builder.Amount = 1;
                }
                else
                {
                    builder.Amount = builder.Mode == "craft"
                        ? OptionalIntInRange(arguments, "amount", 1, 1, int.MaxValue)
                        : 1;
                }
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Crafting)),
        new GameMcpToolDescriptor(
            "game_discover",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "offer_initiate", "offer_select", "offer_confirm", "offer_reroll");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.SecondaryUuid = OptionalUuid(arguments, "offerUuid");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.DiscoveryTreeOffer)),
        new GameMcpToolDescriptor(
            "game_equipment",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "equip", "unequip");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.EquipmentLoadout)),
        new GameMcpToolDescriptor(
            "game_alchemy",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "add", "remove", "set_alchemy_level");
                if (builder.Mode == "set_alchemy_level")
                {
                    builder.Key = "alchemy";
                    builder.Amount = RequiredInt(
                        arguments, "level", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
                    return;
                }
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.SpellComposition,
                static request => request.Mode == "set_alchemy_level"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.AlchemyLoadout)),
        new GameMcpToolDescriptor(
            "game_ritual",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "select", "deselect", "set_level", "activate", "cancel_duration", "end");
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "set_level")
                    builder.Amount = checked(RequiredInt(
                        arguments,
                        "level",
                        WorldRitualDecision.NativeMinimumStartingLevel,
                        int.MaxValue - 1) + 1);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.RitualLifecycle)),
        new GameMcpToolDescriptor(
            "game_level_up",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "purchase", "bonus");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1000);
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.GenericLevel)),
        new GameMcpToolDescriptor(
            "game_loadout",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "select", "set_section",
                    "rename", "next_icon", "next_color", "snapshot_save",
                    "snapshot_load", "snapshot_clear");
                if (builder.Mode.StartsWith("snapshot_", StringComparison.Ordinal))
                {
                    builder.Key = RequireOneOf(arguments, "section", "equipment", "alchemy");
                    builder.SlotIndex = RequiredInt(arguments, "slot", 1, int.MaxValue);
                }
                else
                {
                    builder.Amount = RequiredInt(arguments, "loadout", 1, int.MaxValue);
                    if (builder.Mode == "set_section")
                    {
                        builder.Key = RequireOneOf(arguments, "section", "equipment", "alchemy");
                        builder.SerializedValue = OptionalBool(arguments, "enabled", false)
                            ? "true"
                            : "false";
                    }
                    if (builder.Mode == "rename")
                        builder.SerializedValue = RequireString(arguments, "name");
                }
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Loadout)),
        new GameMcpToolDescriptor(
            "time_challenge",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "select", "queue", "abandon", "reroll", "state");
                builder.Uuid = OptionalUuid(arguments, "uuid");
                if (builder.Mode is "select" or "queue" or "abandon" && builder.Uuid == Guid.Empty)
                    throw new GameMcpInvalidParamsException("uuid is required for " + builder.Mode);
                if (builder.Mode is "reroll" or "state" && builder.Uuid != Guid.Empty)
                    throw new GameMcpInvalidParamsException(
                        "uuid is accepted only for select, queue, or abandon");
            },
            FrameRead(ReadOnly, GameplayData,
                static (_, context) => GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ChallengeStateRead(context).Freeze()),
                static request => request.Mode == "state"),
            Command(Gameplay, GameplayData, GameMcpCommandKind.Challenge)),
        new GameMcpToolDescriptor(
            "time_prestige",
            static (arguments, builder) =>
            {
                builder.Mode = "reset";
                if (!OptionalBool(arguments, "confirm", false))
                    throw new GameMcpInvalidParamsException(
                        "confirm must be true to request the irreversible persistent reset");
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Prestige)),
        new GameMcpToolDescriptor(
            "game_research",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode",
                    "develop", "pause", "resume", "cancel", "bonus");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = builder.Mode == "develop"
                    ? OptionalIntInRange(arguments, "amount", 1, 1, int.MaxValue)
                    : 1;
            },
            Command(Gameplay, GameplayData, GameMcpCommandKind.Research)),
        new GameMcpToolDescriptor(
            "suite_config_set",
            static (arguments, builder) =>
            {
                builder.Section = RequireString(arguments, "section");
                builder.Key = RequireString(arguments, "key");
                builder.SerializedValue = RequireRawString(arguments, "serializedValue");
            },

            // A write captures the world because one of the values it may set is only safe or unsafe
            // against a live game number: reserving the whole action queue is a number Auto Buy can
            // never queue under, and the queue's capacity is the game's to say.
            Command(
                SuiteAdministration,
                GameMcpFrameData.World | ConfigurationData,
                GameMcpCommandKind.ConfigurationSet)),
        new GameMcpToolDescriptor(
            "suite_breakers",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "list", "set");
                if (builder.Mode == "set")
                {
                    builder.Key = RequireOneOf(
                        arguments, "feature", GameMcpAutomationFeatures.Names());
                    builder.SerializedValue = RequireBool(arguments, "on") ? "Active" : "Disabled";
                }
            },
            FrameRead(ReadOnly, GameMcpFrameData.Configuration,
                static (_, context) => GameMcpToolExecution.Read(
                    GameMcpAutomationFeatures.ProjectGameMcpAutomationFeatures(context)),
                static request => request.Mode == "list"),
            Command(
                SuiteAdministration,
                GameMcpFrameData.Configuration,
                GameMcpCommandKind.AutomationSet)),
        new GameMcpToolDescriptor(
            "suite_emergency_stop",
            static (arguments, builder) =>
                builder.Mode = RequireOneOf(arguments, "mode", "engage", "resume"),
            Command(
                SuiteAdministration,
                GameMcpFrameData.Configuration,
                GameMcpCommandKind.EmergencyStop)),
        new GameMcpToolDescriptor(
            "game_screenshot",
            static (arguments, builder) =>
                builder.SaveCapture = OptionalBool(arguments, "save", false),

            // A screenshot is a capture the server performs, not a read of published state, whether or
            // not the caller also asks for it on disk. One classification keeps one status word.
            Command(
                SuiteAdministration,
                GameMcpFrameData.Configuration,
                GameMcpCommandKind.Screenshot)),
        new GameMcpToolDescriptor(
            "game_continue",
            NoArguments,
            Command(UiState, UiData, GameMcpCommandKind.ContinueRun)),
        new GameMcpToolDescriptor(
            "game_return_to_menu",
            NoArguments,
            Command(Gameplay, GameplayData, GameMcpCommandKind.ReturnToMenu)),
        new GameMcpToolDescriptor(
            "game_modal",
            static (arguments, builder) =>
            {
                builder.Mode = RequireOneOf(arguments, "mode", "dismiss", "open");
                if (string.Equals(builder.Mode, "open", StringComparison.Ordinal))
                    builder.Title = RequireString(arguments, "title");
            },
            Command(UiState, UiData, GameMcpCommandKind.Modal)),
        new GameMcpToolDescriptor(
            "game_screen_catalog",
            NoArguments,
            ExecutorRead(ReadOnly, None, static (executor, _) => executor.CaptureScreenCatalog())),
        new GameMcpToolDescriptor(
            "game_navigate",
            static (arguments, builder) =>
            {
                builder.Tab = ParseNavigationSelector(RequireSelector(arguments, "screen"));
                if (arguments.TryGetValue("subtab", out _))
                    builder.Subtab = ParseNavigationSelector(RequireSelector(arguments, "subtab"));
                builder.Uuid = OptionalUuid(arguments, "uuid");
            },
            Command(UiState, UiData, GameMcpCommandKind.Navigation)),
        new GameMcpToolDescriptor(
            "game_screen_elements",
            static (arguments, builder) =>
            {
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
            },

            // The tooltip catalog joins the spell a casting-bar button is about to the loadout slot the
            // cast verbs address it by, and the loadout is a published world fact. With no world there
            // is no join and those rows carry no slot; the catalog still answers, because what the
            // player can hover is a screen fact rather than a save one.
            Command(ReadOnly, World, GameMcpCommandKind.TooltipCatalog)),
        new GameMcpToolDescriptor(
            "game_tooltip",
            static (arguments, builder) =>
            {
                builder.Path = OptionalString(arguments, "path");
                builder.Uuid = OptionalUuid(arguments, "uuid");
            },

            // Two address forms, exactly one per call. A path names a place on the screen and a uuid
            // names a thing; sending both would be two questions in one call, and answering either
            // silently would be a guess about which the caller meant.
            static (arguments, errors) =>
            {
                var hasPath = arguments.ContainsKey("path");
                var hasEntity = arguments.ContainsKey("uuid");
                if (!hasPath && !hasEntity)
                    errors.Add("game_tooltip requires path (a screen address) or uuid (a published entity)");
                else if (hasPath && hasEntity)
                    errors.Add("game_tooltip accepts path or uuid, not both");
            },

            // A uuid no element on this screen shows answers with the entity's own tooltip unless the
            // world marks the entity hidden, so a uuid read takes the world. A path read reads no world
            // at all and keeps taking none — what the player can hover is a screen fact.
            Command(ReadOnly, World, GameMcpCommandKind.TooltipRead,
                static request => request.Uuid != Guid.Empty),
            Command(ReadOnly, None, GameMcpCommandKind.TooltipRead)),
        new GameMcpToolDescriptor(
            "game_probe",
            static (arguments, builder) =>
                builder.Probe = RequireOneOf(
                    arguments, "probe", "runtime", "action_queue_room", "navigation"),
            Command(ReadOnly, None, GameMcpCommandKind.Probe)),
    };

    private static GameMcpResourceDescriptor[] ShippingResources() => new[]
    {
        new GameMcpResourceDescriptor(
            "orb://world/overview",
            "world-overview",
            "world overview",
            "Compact published-world strategy overview.",
            World,
            Overview),
        new GameMcpResourceDescriptor(
            "orb://world/categories",
            "world-categories",
            "world categories",
            "Discoverable world table inventory and collection status.",
            World,
            Categories),
        new GameMcpResourceDescriptor(
            "orb://suite/health",
            "suite-health",
            "suite health",
            "Compact feature, service, emergency, collection, and MCP health.",
            HealthData,
            SuiteHealth),
        new GameMcpResourceDescriptor(
            "orb://suite/configuration",
            "suite-configuration",
            "suite configuration",
            "Committed suite configuration and writable setting catalog.",
            ConfigurationData,
            SuiteConfiguration),
        new GameMcpResourceDescriptor(
            "orb://trace/health",
            "trace-health",
            "trace health",
            "Trace-writer health and retained volume.",
            TraceData,
            TraceHealth),
        new GameMcpResourceDescriptor(
            CategoryUriPrefix + "{category}",
            "world-category",
            "Published world category",
            "First page of an exact category returned by world_categories.",
            World,
            static (request, context) => GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                context,
                Uri.UnescapeDataString(request.ResourceUri.Substring(CategoryUriPrefix.Length)),
                0,
                GameMcpWorldQuery.DefaultLimit,
                limitFromCaller: false).Freeze())),
    };

    private static GameMcpToolExecution Overview(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Read(GameMcpWorldQuery.Overview(context).Freeze());

    private static GameMcpToolExecution Categories(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Read(GameMcpWorldQuery.ListCategories(context).Freeze());

    private static GameMcpToolExecution SuiteHealth(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Text(GameMcpHealthProjection.ProjectGameMcpHealthText(context));

    private static GameMcpToolExecution SuiteConfiguration(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Read(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            context, request.Mode == "describe", request.Section));

    private static GameMcpToolExecution TraceHealth(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Text(GameMcpHealthProjection.ProjectGameMcpTraceHealthText(context));

    private static GameMcpNavigationSelector ParseNavigationSelector(string label) => new(label);
}
#endif

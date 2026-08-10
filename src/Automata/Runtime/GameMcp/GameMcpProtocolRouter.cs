#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

/// <summary>Stateless MCP 2025-11-25 JSON-RPC application layer.</summary>
internal sealed class GameMcpProtocolRouter
{
    internal const string LatestProtocolVersion = "2025-11-25";
    internal const string ServerName = "orb-of-creation-modsuite";
    internal const string ServerVersion = "0.5.0-game-mcp";
    internal const int TerminalWaitMilliseconds = 2_000;

    private static readonly HashSet<string> SupportedProtocolVersions = new(StringComparer.Ordinal)
    {
        "2025-11-25",
        "2025-06-18",
        "2025-03-26",
    };
    private static readonly string[] ProtocolOnlyMethodNames =
    {
        "initialize",
        "ping",
        "tools/list",
        "resources/list",
        "resources/templates/list",
    };

    private readonly GameMcpFrameInbox _operations;

    internal GameMcpProtocolRouter(GameMcpFrameInbox operations)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
    }

    internal GameMcpProtocolResponse Handle(JObject request)
    {
        GameMcpFrameThreadBoundary.AssertTransportWorkAllowed("MCP protocol routing");
        if ((string?)request["jsonrpc"] != "2.0")
            return GameMcpProtocolResponse.Json(Error(null, -32600, "jsonrpc must be exactly '2.0'"));

        var method = (string?)request["method"];
        if (string.IsNullOrWhiteSpace(method))
            return GameMcpProtocolResponse.Json(Error(request["id"], -32600, "method is required"));

        var hasId = request.TryGetValue("id", out var id);
        if (!hasId)
            return GameMcpProtocolResponse.Accepted();

        try
        {
            var result = method switch
            {
                "initialize" => Initialize(request),
                "ping" => new JObject(),
                "tools/list" => ListTools(),
                "tools/call" => CallToolNew(request),
                "resources/list" => ListResources(),
                "resources/templates/list" => ListResourceTemplates(),
                "resources/read" => ReadResourceNew(request),
                _ => null,
            };
            return result is null
                ? GameMcpProtocolResponse.Json(Error(id, -32601, "method not found: " + method))
                : GameMcpProtocolResponse.Json(Success(id, result));
        }
        catch (GameMcpInvalidParamsException exception)
        {
            return GameMcpProtocolResponse.Json(
                Error(id, -32602, exception.Message));
        }
        catch (Exception exception)
        {
            return GameMcpProtocolResponse.Json(
                Error(id, -32603, "internal MCP failure: " + exception.GetBaseException().Message));
        }
    }

    internal static bool IsSupportedProtocolVersion(string? value) =>
        value is not null && SupportedProtocolVersions.Contains(value);

    internal static string[] ProtocolOnlyMethods() =>
        (string[])ProtocolOnlyMethodNames.Clone();

    private static JObject Initialize(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var requested = RequireString(parameters, "protocolVersion");
        var negotiated = SupportedProtocolVersions.Contains(requested)
            ? requested
            : LatestProtocolVersion;
        RequireObject(parameters, "capabilities");
        RequireObject(parameters, "clientInfo");

        return new JObject
        {
            ["protocolVersion"] = negotiated,
            ["capabilities"] = new JObject
            {
                ["tools"] = new JObject { ["listChanged"] = false },
                ["resources"] = new JObject
                {
                    ["subscribe"] = false,
                    ["listChanged"] = false,
                },
            },
            ["serverInfo"] = new JObject
            {
                ["name"] = ServerName,
                ["title"] = "Orb Of Creation strategist server",
                ["version"] = ServerVersion,
                ["description"] =
                    "Local perf-debug access to one published world, suite health, and audited player commands.",
            },
            ["instructions"] =
                "Every read names one published world. Actions are live-revalidated on Unity's " +
                "main thread and return their terminal outcome plus observed post-state inline.",
        };
    }

    private JObject CallToolNew(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var name = RequireString(parameters, "name");

        // One transport for one kind of answer. A refusal about what a known tool was asked is a
        // tool result like every other refusal, so a caller branches on what went wrong rather than
        // on which of two shapes it arrived in. Only a request that never named a tool this server
        // has — an unknown method or an unknown name — is still a protocol error.
        try
        {
            var argumentsToken = parameters["arguments"];
            var arguments = argumentsToken switch
            {
                null => new JObject(),
                JObject value => value,
                _ => throw new GameMcpInvalidParamsException("arguments must be an object"),
            };
            ValidateToolArguments(name, arguments);
            return SubmitAndWait(BuildOperation(name, arguments)).ToProtocolResult();
        }
        catch (GameMcpInvalidParamsException exception)
            when (exception.Code != "unknown_tool")
        {
            return ArgumentRefusal(exception).ToProtocolResult();
        }
    }

    /// <summary>The refusal body an argument the server will not run answers with.</summary>
    private static GameMcpToolExecution ArgumentRefusal(GameMcpInvalidParamsException exception) =>
        GameMcpToolExecution.Read(new GameMcpObjectBuilder
        {
            ["status"] = "rejected",
            ["code"] = exception.Code,
            ["reason"] = exception.Message,
        });

    private JObject ReadResourceNew(JObject request)
    {
        var parameters = RequireObject(request, "params");
        var uri = RequireString(parameters, "uri");
        if (!IsKnownResourceUri(uri))
            throw new GameMcpInvalidParamsException(
                "unknown resource URI '" + uri + "'; call resources/list");
        var operation = new GameMcpOperationRequestBuilder
        {
            ToolName = "resource_read",
            Classification = GameMcpOperationClass.ReadOnly,
            RequiredData = ResourceData(uri),
            ResourceUri = uri,
        }.Freeze();
        var execution = SubmitAndWait(operation);
        if (execution.TextContent is not null)
        {
            return new JObject
            {
                ["contents"] = new JArray
                {
                    new JObject
                    {
                        ["uri"] = uri,
                        ["mimeType"] = "text/plain",
                        ["text"] = execution.TextContent,
                    },
                },
            };
        }
        var encoded = GameMcpDocumentJsonEncoder.Encode(
            execution.Payload!, execution.EntityIdentities);
        return new JObject
        {
            ["contents"] = new JArray
            {
                new JObject
                {
                    ["uri"] = uri,
                    ["mimeType"] = "application/json",
                    ["text"] = encoded.ToString(Formatting.None),
                },
            },
        };
    }

    private GameMcpToolExecution SubmitAndWait(GameMcpOperationRequest request)
    {
        var operation = _operations.Submit(request);
        if (!operation.Completion.TryWait(
                TimeSpan.FromMilliseconds(TerminalWaitMilliseconds),
                out var terminal))
        {
            var canceled = GameMcpToolExecution.Error(new GameMcpObjectBuilder
            {
                ["status"] = "rejected",
                ["code"] = "request_canceled_before_claim",
                ["reason"] = "Unity did not claim " + request.ToolName +
                    " within " + TerminalWaitMilliseconds +
                    " ms; it was canceled before execution",
            }.Freeze());
            if (operation.Completion.TryCancelBeforeClaim(canceled)) return canceled;
            terminal = operation.Completion.WaitForClaimedTerminal();
        }
        return terminal;
    }

    internal static GameMcpOperationRequest BuildOperation(string name, JObject arguments)
    {
        var builder = new GameMcpOperationRequestBuilder
        {
            ToolName = name,
            Limit = GameMcpWorldQuery.DefaultLimit,
            Amount = 1,
        };
        switch (name)
        {
            case "world_overview":
            case "world_categories":
            case "trace_health":
            case "game_continue":
            case "game_return_to_menu":
            case "game_screen_catalog":
                break;
            case "game_modal":
                builder.Mode = RequireOneOf(arguments, "mode", "dismiss");
                break;
            case "suite_configuration":
                builder.Mode = arguments.ContainsKey("mode")
                    ? RequireOneOf(arguments, "mode", "list", "describe")
                    : "list";
                break;
            case "world_list":
                builder.Category = RequireString(arguments, "category");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                builder.AffordableOnly = OptionalBool(arguments, "affordable", false);
                break;
            case "world_get":
                builder.Category = RequireString(arguments, "category");
                if (arguments.ContainsKey("uuids"))
                    builder.Uuids = ReadEntityIdArray(
                        arguments, "uuids", GameMcpWorldQuery.MaximumBatchSize);
                else
                    builder.Uuids = new[] { RequireUuid(arguments, "uuid").ToString("D") };
                break;
            case "entity_catalog":
            case "world_search":
                builder.Query = RequireString(arguments, "query");
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                break;
            case "explain_entity":
                builder.Uuid = RequireUuid(arguments, "uuid");
                break;
            case "suite_health":
            case "suite_check_game_math":
                break;
            case "game_purchase":
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1000);
                break;
            case "game_cast":
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Mode = RequireOneOf(
                    arguments, "mode", "fire", "release", "toggle_off");
                builder.SlotIndex = RequiredInt(arguments, "slot", 1, 256);
                if (builder.Mode == "fire" && OptionalBool(arguments, "charge", false))
                    builder.SerializedValue = "charge";
                break;
            case "game_concept":
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Mode = RequireOneOf(arguments, "mode", "add", "remove_owned");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1_000_000);
                break;
            case "game_agromancy":
                builder.Mode = RequireOneOf(arguments, "mode",
                    "add_plot_action", "remove_plot_action",
                    "add_element", "remove_element", "add_element_action",
                    "remove_element_action");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.SecondaryUuid = builder.Mode is "add_plot_action" or
                    "remove_plot_action" or "add_element_action" or
                    "remove_element_action"
                    ? RequireUuid(arguments, "actionUuid")
                    : Guid.Empty;
                builder.Amount = RequiredInt(arguments, "amount", 1, 10_000);
                break;
            case "game_structure":
                builder.Mode = RequireOneOf(arguments, "mode", "enable", "disable");
                builder.Uuid = RequireUuid(arguments, "uuid");
                break;
            case "game_spell_level":
                builder.Mode = RequireOneOf(arguments, "mode", "single", "all");
                builder.Uuid = builder.Mode == "single"
                    ? RequireUuid(arguments, "uuid")
                    : Guid.Empty;
                break;
            case "game_casting_dial":
                builder.Key = RequireOneOf(arguments, "dial", "output", "reserve");
                builder.Mode = builder.Key == "output" ? "set_output_level" : "set_reserve_level";
                builder.Amount = RequiredInt(
                    arguments, "value", WorldSpellWorkbench.MinimumDialLevel, int.MaxValue);
                break;
            case "game_spell_loadout":
                builder.Mode = RequireOneOf(
                    arguments, "mode", "staged", "preview", "add", "remove", "move");
                if (builder.Mode is "preview" or "add")
                {
                    builder.Uuid = RequireUuid(arguments, "uuid");
                    builder.UuidCounts = RequireUuidCountArray(arguments, "glyphs", 64);
                }
                else if (builder.Mode is "remove" or "move")
                    builder.Amount = RequiredInt(arguments, "slot", 1, 256);
                if (builder.Mode == "move")
                    builder.SlotIndex = RequiredInt(arguments, "destination", 1, 256);
                break;
            case "game_targeting":
                builder.Mode = RequireOneOf(arguments, "mode", "submit", "randomize");
                if (builder.Mode == "submit") builder.Uuid = RequireUuid(arguments, "uuid");
                break;
            case "game_consumable":
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
                break;
            case "game_craft":
                builder.Mode = arguments.ContainsKey("mode")
                    ? RequireOneOf(arguments, "mode", "craft", "automate",
                        "cancel_manual", "cancel_automation")
                    : "craft";
                builder.Uuid = RequireUuid(arguments, "uuid");
                break;
            case "game_discover":
                builder.Mode = RequireOneOf(arguments, "mode", "preview", "confirm",
                    "offer_initiate", "offer_select", "offer_confirm", "offer_reroll");
                if (builder.Mode is "preview" or "confirm")
                {
                    builder.Key = builder.Mode == "preview" && !arguments.ContainsKey("surface")
                        ? string.Empty
                        : RequireOneOf(arguments, "surface", "spellcraft", "glyphcraft",
                            "devote", "runecraft", "alchemy", "artifacts", "concepts");
                    builder.UuidCounts = RequireUuidCountArray(arguments, "components", 64);
                }
                else
                {
                    builder.Uuid = RequireUuid(arguments, "uuid");
                    builder.SecondaryUuid = OptionalUuid(arguments, "offerUuid");
                }
                break;
            case "game_equipment":
                builder.Mode = RequireOneOf(arguments, "mode", "equip", "unequip");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
                break;
            case "game_alchemy":
                builder.Mode = RequireOneOf(arguments, "mode", "add", "remove");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, int.MaxValue);
                break;
            case "game_ritual":
                builder.Mode = RequireOneOf(arguments, "mode",
                    "select", "deselect", "set_level", "activate", "cancel_duration", "end");
                builder.Uuid = RequireUuid(arguments, "uuid");
                if (builder.Mode == "set_level")
                    builder.Amount = checked(RequiredInt(
                        arguments,
                        "level",
                        WorldRitualDecision.NativeMinimumStartingLevel,
                        int.MaxValue - 1) + 1);
                break;
            case "game_level":
                builder.Mode = RequireOneOf(arguments, "mode", "purchase", "bonus");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = RequiredInt(arguments, "amount", 1, 1000);
                break;
            case "game_loadout":
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
                break;
            case "time_challenge":
                builder.Mode = RequireOneOf(arguments, "mode",
                    "select", "queue", "abandon", "reroll", "state");
                builder.Uuid = OptionalUuid(arguments, "uuid");
                if (builder.Mode is "select" or "queue" or "abandon" && builder.Uuid == Guid.Empty)
                    throw new GameMcpInvalidParamsException("uuid is required for " + builder.Mode);
                if (builder.Mode is "reroll" or "state" && builder.Uuid != Guid.Empty)
                    throw new GameMcpInvalidParamsException(
                        "uuid is accepted only for select, queue, or abandon");
                break;
            case "time_prestige":
                builder.Mode = "reset";
                if (!OptionalBool(arguments, "confirm", false))
                    throw new GameMcpInvalidParamsException(
                        "confirm must be true to request the irreversible persistent reset");
                break;
            case "game_research":
                builder.Mode = RequireOneOf(arguments, "mode",
                    "develop", "pause", "resume", "cancel", "bonus");
                builder.Uuid = RequireUuid(arguments, "uuid");
                builder.Amount = builder.Mode == "develop"
                    ? OptionalIntInRange(arguments, "amount", 1, 1, int.MaxValue)
                    : 1;
                break;
            case "suite_config_set":
                builder.Section = RequireString(arguments, "section");
                builder.Key = RequireString(arguments, "key");
                builder.SerializedValue = RequireRawString(arguments, "serializedValue");
                break;
            case "suite_automation":
                builder.Mode = RequireOneOf(arguments, "mode", "list", "set");
                if (builder.Mode == "set")
                {
                    builder.Key = RequireOneOf(
                        arguments, "feature", GameMcpAutomationFeatures.Names());
                    builder.SerializedValue = RequireBool(arguments, "on") ? "Active" : "Disabled";
                }
                break;
            case "suite_emergency_stop":
                builder.Mode = RequireOneOf(arguments, "mode", "engage", "resume");
                break;
            case "game_screenshot":
                builder.SaveCapture = OptionalBool(arguments, "save", false);
                break;
            case "game_navigate":
                builder.Tab = ParseNavigationSelector(RequireSelector(arguments, "screen"));
                if (arguments.TryGetValue("subtab", out _))
                    builder.Subtab = ParseNavigationSelector(RequireSelector(arguments, "subtab"));
                builder.Uuid = OptionalUuid(arguments, "uuid");
                break;
            case "game_tooltips":
                builder.Offset = OptionalInt(arguments, "offset", 0);
                builder.Limit = OptionalInt(
                    arguments, "limit", GameMcpWorldQuery.DefaultLimit);
                break;
            case "game_tooltip":
                builder.Path = RequireString(arguments, "path");
                break;
            case "game_probe":
                builder.Probe = RequireOneOf(
                    arguments, "probe", "runtime", "action_queue_room", "navigation");
                break;
            default:
                throw new GameMcpInvalidParamsException(
                    "unknown tool '" + name + "'; call tools/list",
                    "unknown_tool");
        }
        builder.Classification = Classification(name, builder);
        builder.RequiredData = RequiredData(name, builder);
        return builder.Freeze();
    }

    private static GameMcpNavigationSelector ParseNavigationSelector(string label) => new(label);

    private static bool IsKnownResourceUri(string uri) =>
        uri is "orb://world/overview" or
            "orb://world/categories" or
            "orb://suite/health" or
            "orb://suite/configuration" or
            "orb://trace/health" ||
        uri.StartsWith("orb://world/category/", StringComparison.Ordinal);

    private static GameMcpFrameData ResourceData(string uri) => uri switch
    {
        "orb://suite/health" => RequiredData("suite_health"),
        "orb://suite/configuration" => RequiredData("suite_configuration"),
        "orb://trace/health" => RequiredData("trace_health"),
        _ => GameMcpFrameData.World,
    };

    private static GameMcpOperationClass Classification(
        string name,
        GameMcpOperationRequestBuilder request) => name switch
    {
        "game_purchase" or "game_cast" or "game_concept" or "game_agromancy" or
            "game_structure" or "game_return_to_menu" or
            "game_spell_level" or "game_casting_dial" or "game_spell_loadout" or "game_targeting" or
            "game_consumable" or "game_craft" or "game_discover" or "game_equipment" or
            "time_challenge" or "time_prestige" or "game_research" or "game_alchemy" or
            "game_ritual" or "game_level" or "game_loadout" when
                !(name == "game_discover" && request.Mode == "preview") &&
                !(name == "time_challenge" && request.Mode == "state") &&
                !(name == "game_spell_loadout" && request.Mode is "preview" or "staged") =>
                GameMcpOperationClass.Gameplay,
        "game_navigate" or "game_continue" or "game_modal" => GameMcpOperationClass.UiState,

        // A screenshot is a capture the server performs, not a read of published state, whether or
        // not the caller also asks for it on disk. One classification keeps one status word.
        "game_screenshot" or "suite_config_set" or "suite_emergency_stop" or
            "suite_check_game_math" =>
            GameMcpOperationClass.SuiteAdministration,
        "suite_automation" when request.Mode == "set" =>
            GameMcpOperationClass.SuiteAdministration,
        _ => GameMcpOperationClass.ReadOnly,
    };

    private static GameMcpFrameData RequiredData(
        string name,
        GameMcpOperationRequestBuilder? request = null) => name switch
    {
        "entity_catalog" => GameMcpFrameData.None,
        "world_overview" or "world_categories" or "world_list" or "world_get" or
            "world_search" or "explain_entity" => GameMcpFrameData.World,
        "suite_health" => GameMcpFrameData.World | GameMcpFrameData.Configuration |
            GameMcpFrameData.FeatureHealth | GameMcpFrameData.ServiceHealth |
            GameMcpFrameData.Scene | GameMcpFrameData.NativeContractHealth,
        "suite_configuration" =>
            GameMcpFrameData.Configuration | GameMcpFrameData.WritableConfiguration,

        // A write captures the world because one of the values it may set is only safe or unsafe
        // against a live game number: reserving the whole action queue is a number Auto Buy can
        // never queue under, and the queue's capacity is the game's to say.
        "suite_config_set" => GameMcpFrameData.World | GameMcpFrameData.Configuration |
            GameMcpFrameData.WritableConfiguration,
        "trace_health" => GameMcpFrameData.TraceWriterHealth,
        "suite_emergency_stop" or "suite_automation" => GameMcpFrameData.Configuration,
        "game_spell_loadout" when request?.Mode == "staged" => GameMcpFrameData.None,
        "game_purchase" or "game_cast" or "game_concept" or "game_agromancy" or
            "game_structure" or "game_return_to_menu" or
            "game_spell_level" or "game_casting_dial" or "game_spell_loadout" or "game_targeting" or
            "game_consumable" or "game_craft" or "game_discover" or "game_equipment" or
            "time_challenge" or "time_prestige" or "game_research" or "game_alchemy" or
            "game_ritual" or "game_level" or "game_loadout" =>
            GameMcpFrameData.World | GameMcpFrameData.Configuration,
        "game_screenshot" => GameMcpFrameData.Configuration,
        "game_navigate" or "game_continue" or "game_modal" =>
            GameMcpFrameData.World | GameMcpFrameData.Scene,
        "game_probe" or
            "game_screen_catalog" or "game_tooltips" or "game_tooltip" or
            "suite_check_game_math" =>
            GameMcpFrameData.None,
        _ => throw new InvalidOperationException("no frame-data policy exists for tool " + name),
    };


    private static JObject ListTools() => new()
    {
        ["tools"] = new JArray
        {
            Tool(
                "world_overview",
                "Published world overview",
                "Read the compact facts a strategist normally needs before choosing a detailed query.",
                ObjectSchema()),
            Tool(
                "world_categories",
                "Discover world categories",
                "List every collected world table, native type, row count, and exact availability reason.",
                ObjectSchema()),
            Tool(
                "world_list",
                "List exact world rows",
                "Page through one discoverable category from one immutable published world. limit is an upper bound: a page also stops at a 12 KB response budget, so wide rows come back short. nextOffset is present exactly when more rows remain, and is the offset to resume from. Set affordable=true on a category whose rows carry a price to page only the rows you can buy right now.",
                ObjectSchema(
                    new JObject
                    {
                        ["category"] = StringSchema("Exact name returned by world_categories."),
                        ["offset"] = IntegerSchema(0, int.MaxValue),
                        ["limit"] = IntegerSchema(1, 200),
                        ["affordable"] = BooleanSchema(
                            "Return only rows whose price is met right now. Accepted on priced "
                                + "categories; other categories refuse it rather than ignore it."),
                    },
                    "category")),
            Tool(
                "world_get",
                "Get exact world rows",
                "Read one or more stable UUIDs as one ordered result list from one immutable published world.",
                WorldGetSchema()),
            Tool(
                "entity_catalog",
                "Search live entity catalog",
                "Search every UUID loaded in the game's runtime registry by native type, internal asset name, and available player-facing display name, including loaded entities hidden by progression. Rows page in UUID order, which is not world_search's category order. limit is an upper bound: a page also stops at a 12 KB response budget, so wide rows come back short. nextOffset is present exactly when more rows remain, and is the offset to resume from.",
                ObjectSchema(
                    new JObject
                    {
                        ["query"] = StringSchema("Case-insensitive UUID, native type, internal name, or player-facing display name fragment."),
                        ["offset"] = IntegerSchema(0, int.MaxValue),
                        ["limit"] = IntegerSchema(1, 200),
                    },
                    "query")),
            Tool(
                "explain_entity",
                "Explain one entity",
                "Evaluate visibility, availability, player verbs, recursive prerequisites, thresholds, exact costs, affordability, and typed blockers for one UUID from one immutable published world.",
                ObjectSchema(
                    new JObject
                    {
                        ["uuid"] = StringSchema("Canonical stable entity UUID."),
                    },
                    "uuid")),
            Tool(
                "world_search",
                "Search published entities",
                "Search stable-UUID entity categories only: an entity the published world has no row for is not here, and a query also matches a category's own name and native type. Composite diagnostic categories are intentionally excluded; use world_list for those rows and their localized partiality evidence. Rows page category by category in world_list order, which is not entity_catalog's UUID order. limit is an upper bound: a page also stops at a 12 KB response budget, so wide rows come back short. nextOffset is present exactly when more rows remain, and is the offset to resume from.",
                ObjectSchema(
                    new JObject
                    {
                        ["query"] = StringSchema("Case-insensitive text or UUID fragment."),
                        ["offset"] = IntegerSchema(0, int.MaxValue),
                        ["limit"] = IntegerSchema(1, 200),
                    },
                    "query")),
            Tool(
                "suite_health",
                "Read suite runtime health",
                "Read one compact scene/runtime/STOP/native-contract line plus feature and service names grouped by state.",
                ObjectSchema()),
            Tool(
                "suite_configuration",
                "Read committed configuration",
                "Read every writable setting as section/key and its committed value. mode=describe " +
                "adds each setting's type, the values it accepts, and what it does.",
                ObjectSchema(
                    new JObject { ["mode"] = EnumSchema("list", "describe") })),
            Tool(
                "trace_health",
                "Read trace-writer health",
                "Read bounded segment, record, and byte counters. Individual decisions remain in trace files for offline analysis.",
                ObjectSchema()),
            Tool(
                "suite_check_game_math",
                "Check the suite's math against the game",
                "Run the Mods>Runtime \"Check game math\" differential check and return one verdict line per pass. Every entity in every registry is compared against the game's own answer, and all of it runs inside this call's frame, so the game stalls for seconds and the call takes that long to answer. It is a diagnostic, not a read: run it when a number looks wrong, not on a schedule.",
                ObjectSchema()),
            Tool(
                "game_purchase",
                "Purchase an attribute or upgrade",
                "Live-revalidate and apply one UUID-addressed attribute (native StructureSO) or upgrade purchase; the settled level change is returned inline.",
                ActionSchema(
                    new JObject
                    {
                        ["uuid"] = StringSchema("Canonical UUID from structures (shown in game as attributes) or upgrades; kind is derived."),
                        ["amount"] = IntegerSchema(1, 1000),
                    },
                    "uuid", "amount")),
            Tool(
                "game_cast",
                "Cast an equipped spell",
                "Live-revalidate an equipped slot and fire it, release a charge hold, or press an active toggle spell's native cast button again to turn it off. A fire on a spell that is already running is refused rather than pressed: the game answers that press with a warning or by ending the cast, never by starting one. A committed fire means the press started a cast now. charge=true holds the cast button down the way the player does, so the spell charges instead of firing at once; release it with mode=release, and the longer it was held the more power the cast lands with. Charging needs the Charged Spells research on a spell type that scales with it, and a spell the game will not charge is refused rather than fired uncharged.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("fire", "release", "toggle_off"),
                        ["slot"] = IntegerSchema(1, 256),
                        ["uuid"] = StringSchema("Spell recipe UUID currently occupying the slot."),
                        ["charge"] = BooleanSchema(
                            "Hold the cast button down so the spell charges; release with mode=release."),
                    },
                    "mode", "slot", "uuid"),
                    ModeRule("release", forbidden: new[] { "charge" }),
                    ModeRule("toggle_off", forbidden: new[] { "charge" }))),
            Tool(
                "game_concept",
                "Assign or remove a concept",
                "Apply one exact concept assignment change and return its terminal native result inline.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("add", "remove_owned"),
                        ["uuid"] = StringSchema("Alchemy recipe UUID."),
                        ["amount"] = IntegerSchema(1, 1_000_000),
                    },
                    "mode", "uuid", "amount")),
            Tool(
                "game_agromancy",
                "Use the Agromancy screen",
                "Add or remove a plot action, harvest element, or element action shown on World > Agromancy.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "add_plot_action", "remove_plot_action",
                            "add_element", "remove_element", "add_element_action",
                            "remove_element_action"),
                        ["uuid"] = StringSchema("Published plot or harvest-element UUID."),
                        ["actionUuid"] = StringSchema(
                            "Published action UUID offered by that plot or element."),
                        ["amount"] = IntegerSchema(1, 10_000),
                    },
                    "mode", "uuid"),
                    ModeRule("add_plot_action", new[] { "actionUuid", "amount" }),
                    ModeRule("remove_plot_action", new[] { "actionUuid", "amount" }),
                    ModeRule("add_element", new[] { "amount" }, new[] { "actionUuid" }),
                    ModeRule("remove_element", new[] { "amount" }, new[] { "actionUuid" }),
                    ModeRule("add_element_action", new[] { "actionUuid", "amount" }),
                    ModeRule("remove_element_action", new[] { "actionUuid", "amount" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_structure",
                "Enable or disable an attribute",
                "Apply the same native enable or disable control shown for a published StructureSO attribute.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("enable", "disable"),
                        ["uuid"] = StringSchema("Published structure UUID; shown in game as an attribute."),
                    },
                    "mode", "uuid"),
                    ModeRule("enable"),
                    ModeRule("disable")),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_spell_level",
                "Buy spell mastery",
                "Apply one exact mastery purchase or the native level-all operation inline.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("single", "all"),
                        ["uuid"] = StringSchema("Required only for single; a published spell-recipe UUID."),
                    },
                    "mode"),
                    ModeRule("single", new[] { "uuid" }),
                    ModeRule("all", forbidden: new[] { "uuid" }))),
            Tool(
                "game_casting_dial",
                "Set a global casting dial",
                "Set the global Output Level or Reserve Level shown together on the Casting screen.",
                ActionSchemaWithoutIdentity(
                    new JObject
                    {
                        ["dial"] = EnumSchema("output", "reserve"),
                        ["value"] = IntegerSchema(
                            WorldSpellWorkbench.MinimumDialLevel, int.MaxValue),
                    },
                    "dial", "value"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_spell_loadout",
                "Read staging; preview, add, remove, or move a spell",
                "Read the exact staged Spellcraft core and augment layout; preview an explicit layout's native price without changing staging; add that layout baked into a new spell; or remove or move an equipped spell. remove and move name the slot on the loadout bar, counted from 1 as the screen shows it. Success returns the settled slot change.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("staged", "preview", "add", "remove", "move"),
                        ["uuid"] = StringSchema("A discovered recipe UUID, for preview and add."),
                        ["glyphs"] = ArraySchema(
                            ObjectSchema(new JObject
                            {
                                ["uuid"] = StringSchema("Published augment GlyphSO UUID."),
                                ["count"] = IntegerSchema(1, int.MaxValue),
                            }, "uuid", "count"), 0, 64),
                        ["slot"] = IntegerSchema(1, 256),
                        ["destination"] = IntegerSchema(1, 256),
                    },
                    "mode"),
                    ModeRule("staged", forbidden: new[]
                    {
                        "uuid", "glyphs", "slot", "destination",
                    }),
                    ModeRule("preview", new[] { "uuid", "glyphs" },
                        new[] { "slot", "destination" }),
                    ModeRule("add", new[] { "uuid", "glyphs" },
                        new[] { "slot", "destination" }),
                    ModeRule("remove", new[] { "slot" },
                        new[] { "uuid", "glyphs", "destination" }),
                    ModeRule("move", new[] { "slot", "destination" },
                        new[] { "uuid", "glyphs" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_targeting",
                "Submit or randomize the pending target",
                "Resolve the game's one current target request. Success returns the exact submitted structure.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("submit", "randomize"),
                        ["uuid"] = StringSchema("Required only for submit; use an eligible named targeting candidate UUID."),
                    },
                    "mode"),
                    ModeRule("submit", new[] { "uuid" }),
                    ModeRule("randomize", forbidden: new[] { "uuid" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_consumable",
                "Use or organize consumables",
                "Use, cancel, discard, randomize, or reorder one consumable. Success returns the changed amount, flag, or slot.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "use", "cancel", "discard", "set_randomization", "move"),
                        ["uuid"] = StringSchema("Published ConsumableSO UUID."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                        ["enabled"] = BooleanSchema("Requested randomization state."),
                        ["list"] = EnumSchema("inventory", "hotbar"),
                        ["destination"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid"),
                    ModeRule("discard", new[] { "amount" }, new[] { "enabled", "list", "destination" }),
                    ModeRule("set_randomization", new[] { "enabled" }, new[] { "amount", "list", "destination" }),
                    ModeRule("move", new[] { "list", "destination" }, new[] { "amount", "enabled" }),
                    ModeRule("use", forbidden: new[] { "amount", "enabled", "list", "destination" }),
                    ModeRule("cancel", forbidden: new[] { "amount", "enabled", "list", "destination" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_craft",
                "Craft or control one recipe",
                "Craft one exact recipe, add its UI-sized automation increment, or cancel its manual or automated instance. Success returns only the settled quantity change.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "craft", "automate", "cancel_manual", "cancel_automation"),
                        ["uuid"] = StringSchema("Published CraftingRecipeSO UUID."),
                    },
                    "uuid"),
                    ModeRule("craft"),
                    ModeRule("automate"),
                    ModeRule("cancel_manual"),
                    ModeRule("cancel_automation")),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_discover",
                "Preview or confirm discovery",
                "Compose the components shown by a discovery screen, or drive a transient Discovery Tree offer. Component modes resolve the output; they never accept an output UUID.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("preview", "confirm", "offer_initiate", "offer_select", "offer_confirm", "offer_reroll"),
                        ["surface"] = EnumSchema("spellcraft", "glyphcraft", "devote", "runecraft", "alchemy", "artifacts", "concepts"),
                        ["components"] = ArraySchema(
                            ObjectSchema(new JObject
                            {
                                ["uuid"] = StringSchema("A component UUID selected on the discovery screen."),
                                ["count"] = IntegerSchema(1, int.MaxValue),
                            }, "uuid", "count"), 1, 64),
                        ["uuid"] = StringSchema("Required for offer modes; a published DiscoveryTreeSO UUID."),
                        ["offerUuid"] = StringSchema("Required for offer_select and offer_confirm."),
                    },
                    "mode"),
                    ModeRule("preview", new[] { "components" }, new[] { "uuid", "offerUuid" }),
                    ModeRule("confirm", new[] { "surface", "components" }, new[] { "uuid", "offerUuid" }),
                    ModeRule("offer_initiate", new[] { "uuid" }, new[] { "surface", "components", "offerUuid" }),
                    ModeRule("offer_reroll", new[] { "uuid" }, new[] { "surface", "components", "offerUuid" }),
                    ModeRule("offer_select", new[] { "uuid", "offerUuid" }, new[] { "surface", "components" }),
                    ModeRule("offer_confirm", new[] { "uuid", "offerUuid" }, new[] { "surface", "components" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_equipment",
                "Equip or unequip an artifact",
                "Equip or unequip an explicit artifact amount through the native slot, type-slot, stack, and usage-cost decision. Success returns the stack count before and after.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("equip", "unequip"),
                        ["uuid"] = StringSchema("Published EquipmentSO UUID."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid", "amount"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_alchemy",
                "Change the ordinary Alchemy loadout",
                "Add or remove an explicit number of uses of one discovered ordinary Alchemy recipe through the native usage-capacity decision. Concept assignments stay on game_concept.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("add", "remove"),
                        ["uuid"] = StringSchema("Published ordinary AlchemyRecipeSO UUID."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid", "amount"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_ritual",
                "Select, level, activate, or cancel a ritual reward",
                "Drive the Ritual list controls. Discovery stays on game_discover surface devote; cancel_duration ends a completed run's duration reward, not an active battle.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema(
                            "select", "deselect", "set_level", "activate", "cancel_duration", "end"),
                        ["uuid"] = StringSchema("Published RitualSO UUID."),
                        ["level"] = IntegerSchema(
                            WorldRitualDecision.NativeMinimumStartingLevel, int.MaxValue - 1),
                    },
                    "mode", "uuid"),
                    ModeRule("select", forbidden: new[] { "level" }),
                    ModeRule("deselect", forbidden: new[] { "level" }),
                    ModeRule("set_level", new[] { "level" }),
                    ModeRule("activate", forbidden: new[] { "level" }),
                    ModeRule("cancel_duration", forbidden: new[] { "level" }),
                    ModeRule("end", forbidden: new[] { "level" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_level",
                "Buy paid or bonus levels",
                "Use the native level-list controls for artifact types, glyphs, resource types, and Time Runes. Research and spells keep their dedicated tools.",
                ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("purchase", "bonus"),
                        ["uuid"] = StringSchema("Published levelable entity UUID."),
                        ["amount"] = IntegerSchema(1, 1000),
                    },
                    "mode", "uuid", "amount"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_loadout",
                "Manage player loadouts and snapshots",
                "Select or edit a player loadout, or save, load, and clear visible Equipment or Alchemy snapshot slots. A loadout is named by its position on the loadout bar and a snapshot by its section and its slot, both counted from 1 as the screen shows them.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("select", "set_section", "rename", "next_icon", "next_color",
                            "snapshot_save", "snapshot_load", "snapshot_clear"),
                        ["loadout"] = IntegerSchema(1, int.MaxValue),
                        ["section"] = EnumSchema("equipment", "alchemy"),
                        ["enabled"] = BooleanSchema("Whether the selected loadout saves that section."),
                        ["name"] = StringSchema("Player-visible loadout label, at most 24 characters."),
                        ["slot"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode"),
                    ModeRule("select", new[] { "loadout" },
                        new[] { "section", "enabled", "name", "slot" }),
                    ModeRule("set_section", new[] { "loadout", "section", "enabled" },
                        new[] { "name", "slot" }),
                    ModeRule("rename", new[] { "loadout", "name" },
                        new[] { "section", "enabled", "slot" }),
                    ModeRule("next_icon", new[] { "loadout" },
                        new[] { "section", "enabled", "name", "slot" }),
                    ModeRule("next_color", new[] { "loadout" },
                        new[] { "section", "enabled", "name", "slot" }),
                    ModeRule("snapshot_save", new[] { "section", "slot" },
                        new[] { "loadout", "enabled", "name" }),
                    ModeRule("snapshot_load", new[] { "section", "slot" },
                        new[] { "loadout", "enabled", "name" }),
                    ModeRule("snapshot_clear", new[] { "section", "slot" },
                        new[] { "loadout", "enabled", "name" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "time_challenge",
                "Select, queue, abandon, or reroll challenges",
                "Drive one exact native challenge decision on the game's Time tab. select presses a challenge's preferred toggle: it selects an unselected challenge and gives up a selected one, and where the selections are full and exactly one is held it gives that one up and takes this one in a single call. queue presses the row's queue toggle, which moves it between idle and queued; a queued challenge starts running at the next reset, and only a running one can be abandoned. reroll presses the offer screen's own new-challenges button, free once per world cycle and one reroll every time after. state returns the offers, the reroll budget, the selections, and the reset decision.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("select", "queue", "abandon", "reroll", "state"),
                        ["uuid"] = StringSchema("Required for select, queue, and abandon; a published ChallengeSO UUID."),
                    },
                    "mode"),
                    ModeRule("select", new[] { "uuid" }),
                    ModeRule("queue", new[] { "uuid" }),
                    ModeRule("abandon", new[] { "uuid" }),
                    ModeRule("reroll", forbidden: new[] { "uuid" }),
                    ModeRule("state", forbidden: new[] { "uuid" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "time_prestige",
                "Reset the persistent world",
                "Commit the irreversible native persistent reset after the world cycle and challenge choices are ready. Success waits for a fresh post-reset world and returns its named prestige and challenge decisions inline.",
                ActionSchemaWithoutIdentity(
                    new JObject
                    {
                        ["confirm"] = BooleanSchema("Must be true to confirm the irreversible persistent reset."),
                    },
                    "confirm"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_research",
                "Develop or manage research",
                "Develop, queue, pause, resume, cancel, or apply a free bonus level to one exact research. amount is the number of levels a develop asks for and defaults to 1. Success returns the changed level or state.",
                ModeSchema(ActionSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("develop", "pause", "resume", "cancel", "bonus"),
                        ["uuid"] = StringSchema("Published ResearchSO UUID."),
                        ["amount"] = IntegerSchema(1, int.MaxValue),
                    },
                    "mode", "uuid"),
                    ModeRule("develop"),
                    ModeRule("pause", forbidden: new[] { "amount" }),
                    ModeRule("resume", forbidden: new[] { "amount" }),
                    ModeRule("cancel", forbidden: new[] { "amount" }),
                    ModeRule("bonus", forbidden: new[] { "amount" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "suite_config_set",
                "Commit one suite setting",
                "Write one allowlisted setting through the single committed configuration-store publication path.",
                ObjectSchema(
                    new JObject
                    {
                        ["section"] = StringSchema("Exact BepInEx configuration section."),
                        ["key"] = StringSchema("Exact BepInEx configuration key."),
                        ["serializedValue"] = StringSchema("Ordinary BepInEx serialized value."),
                    },
                    "section", "key", "serializedValue"),
                readOnly: false,
                idempotent: false),
            Tool(
                "suite_automation",
                "Read or flip the automation on/off buttons",
                "The suite's seven green/gray automation buttons as booleans. list returns every "
                    + "feature and whether it is on; set flips exactly one and returns its on "
                    + "before/after. auto_buy buys affordable structures and upgrades. auto_cast "
                    + "fires equipped spells. auto_concept trains the lowest-mastery Scholar "
                    + "concepts. auto_harvest collects ready fruit and treasure trees. auto_items "
                    + "uses eligible Scrolls, Relics, and approved temporary items. auto_scribe "
                    + "writes Scrolls at the Scribe. mentor shares mastery experience with lagging "
                    + "spells, artifacts, and recipes. Everything else a feature can be configured "
                    + "with — thresholds, roles, allowlists — is suite_config_set, and "
                    + "suite_emergency_stop still overrides all seven at once.",
                ModeSchema(ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("list", "set"),
                        ["feature"] = EnumSchema(GameMcpAutomationFeatures.Names()),
                        ["on"] = BooleanSchema("Required for set: true turns the feature on."),
                    },
                    "mode"),
                    ModeRule("list", forbidden: new[] { "feature", "on" }),
                    ModeRule("set", new[] { "feature", "on" })),
                readOnly: false,
                idempotent: false),
            Tool(
                "suite_emergency_stop",
                "Engage or resume suite emergency stop",
                "Commit STOP or RESUME through the same safety configuration authority used in game.",
                ObjectSchema(
                    new JObject
                    {
                        ["mode"] = EnumSchema("engage", "resume"),
                    },
                    "mode"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_screenshot",
                "Capture the game framebuffer",
                "Return a PNG as inline MCP image content. Reading one costs ceil(width/28) x ceil(height/28) tokens, so pixels are its whole price and format and compression never enter it. Every capture arrives 896 pixels wide — 32 patch columns, the narrowest width at which every class of on-screen text stays readable. Set save=true to also write a generated name in the trace folder.",
                ObjectSchema(new JObject
                {
                    ["save"] = BooleanSchema("Also save to the trace folder."),
                }),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_continue",
                "Continue the selected save",
                "On the Start scene only, invoke the game's audited native Continue action for the already selected save.",
                ObjectSchema(),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_return_to_menu",
                "Return to the Start screen",
                "While playing, open the panel that holds the game's Back to Main Menu button if it is shut, invoke that button including its authored manual-save event, and acknowledge after the native screen transition starts but before scene teardown.",
                ObjectSchema(),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_modal",
                "Use the current modal",
                "Dismiss the one unambiguous open native modal through its visible close control.",
                ObjectSchema(new JObject
                {
                    ["mode"] = EnumSchema("dismiss"),
                }, "mode"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_screen_catalog",
                "Discover navigable screens",
                "List the live screens with the active one marked and its current subtab strips grouped beneath it. Screens come back under screens[]; a navigate answers with activeScreen.",
                ObjectSchema()),
            Tool(
                "game_navigate",
                "Navigate the live screen catalog",
                "UI-only, no gameplay/save mutation. Select one catalog screen, an optional subtab of that screen, and an optional published plot node; the answer is the arrived screen in words. game_screenshot is the only tool that captures the framebuffer.",
                ObjectSchema(
                    new JObject
                    {
                        ["screen"] = StringSchema("Exact player-facing top-level screen name."),
                        ["subtab"] = StringSchema("Optional exact player-facing subtab name."),
                        ["uuid"] = StringSchema("Optional published plot UUID to select after navigation."),
                    },
                    "screen"),
                readOnly: false,
                idempotent: false,
                classification: "UI-only, no gameplay/save mutation"),
            Tool(
                "game_tooltips",
                "Discover visible tooltips",
                "Page through the tooltip-bearing elements the player can hover right now — the current screen, its persistent chrome, and any open modal — by sibling-indexed native path. Each row's path is relative to the response's pathPrefix, present exactly when the returned rows share one. A closed modal stays instantiated and is not listed. nextOffset is present exactly when more rows remain, and is the offset to resume from.",
                ObjectSchema(new JObject
                {
                    ["offset"] = IntegerSchema(0, int.MaxValue),
                    ["limit"] = IntegerSchema(1, 200),
                })),
            Tool(
                "game_tooltip",
                "Read a visible tooltip",
                "Read compact plain screen text for one path from the current game_tooltips catalog; paths are volatile screen-state handles, so refresh the catalog after navigation or mutation.",
                ObjectSchema(
                    new JObject
                    {
                        ["path"] = StringSchema(
                            "A path exactly as game_tooltips returned it. Any longer tail of the " +
                            "same path is accepted, including the whole path."),
                    },
                    "path"),
                readOnly: false,
                idempotent: false),
            Tool(
                "game_probe",
                "Run an allowlisted native value probe",
                "Read runtime/lifecycle, navigation, or live action-queue facts absent from the published world.",
                ObjectSchema(
                    new JObject
                    {
                        ["probe"] = EnumSchema("runtime", "action_queue_room", "navigation"),
                    },
                    "probe")),
        },
    };

    private static JObject WorldGetSchema()
    {
        return ObjectSchema(
            new JObject
            {
                ["category"] = StringSchema("Exact name returned by world_categories."),
                ["uuids"] = ArraySchema(
                    StringSchema("Canonical D-format stable UUID."),
                    1,
                    GameMcpWorldQuery.MaximumBatchSize),
                ["uuid"] = StringSchema(
                    "Singular alias for one canonical UUID; do not combine with uuids."),
            },
            "category");
    }

    private static void ValidateToolArguments(string name, JObject arguments)
    {
        var tool = (ListTools()["tools"] as JArray ?? new JArray())
            .OfType<JObject>()
            .FirstOrDefault(candidate => string.Equals(
                (string?)candidate["name"],
                name,
                StringComparison.Ordinal));
        if (tool?["inputSchema"] is not JObject schema) return;

        var properties = schema["properties"] as JObject ?? new JObject();
        var errors = new JArray();
        if (schema["required"] is JArray required)
        {
            foreach (var field in required.Values<string>())
            {
                if (field is not null && !arguments.ContainsKey(field))
                    errors.Add(ValidationError(
                        "missing_required",
                        field,
                        "required field '" + field + "' is missing"));
            }
        }

        if (string.Equals(name, "world_get", StringComparison.Ordinal))
        {
            var hasBatch = arguments.ContainsKey("uuids");
            var hasSingle = arguments.ContainsKey("uuid");
            if (!hasBatch && !hasSingle)
                errors.Add(ValidationError(
                    "missing_required",
                    "uuids",
                    "world_get requires uuids (array) or uuid (singular alias)"));
            else if (hasBatch && hasSingle)
                errors.Add(ValidationError(
                    "mutually_exclusive",
                    "uuid",
                    "world_get accepts uuid or uuids, not both"));
        }

        foreach (var supplied in arguments.Properties())
        {
            if (properties.ContainsKey(supplied.Name)) continue;
            errors.Add(ValidationError(
                "unexpected_field",
                supplied.Name,
                "field '" + supplied.Name + "' is not accepted by " + name));
        }

        if (string.Equals(name, "game_discover", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var hasSurface = arguments.ContainsKey("surface");
            var hasComponents = arguments.ContainsKey("components");
            var hasTree = arguments.ContainsKey("uuid");
            var hasOffer = arguments.ContainsKey("offerUuid");
            if (mode is "preview" or "confirm")
            {
                if (mode == "confirm" && !hasSurface)
                    errors.Add(ValidationError("missing_required", "surface",
                        "required field 'surface' is missing for mode 'confirm'"));
                if (!hasComponents) errors.Add(ValidationError("missing_required", "components",
                    "required field 'components' is missing for mode '" + mode + "'"));
                if (hasTree) errors.Add(ValidationError("unexpected_for_mode", "uuid",
                    "field 'uuid' is accepted only for offer modes"));
                if (hasOffer) errors.Add(ValidationError("unexpected_for_mode", "offerUuid",
                    "field 'offerUuid' is accepted only for offer_select or offer_confirm"));
            }
            else
            {
                if (!hasTree) errors.Add(ValidationError("missing_required", "uuid",
                    "required field 'uuid' is missing for mode '" + mode + "'"));
                if (hasSurface) errors.Add(ValidationError("unexpected_for_mode", "surface",
                    "field 'surface' is accepted only for preview or confirm"));
                if (hasComponents) errors.Add(ValidationError("unexpected_for_mode", "components",
                    "field 'components' is accepted only for preview or confirm"));
                if (mode is "offer_select" or "offer_confirm" && !hasOffer)
                    errors.Add(ValidationError("missing_required", "offerUuid",
                        "required field 'offerUuid' is missing for mode '" + mode + "'"));
                if (mode is "offer_initiate" or "offer_reroll" && hasOffer)
                    errors.Add(ValidationError("unexpected_for_mode", "offerUuid",
                        "field 'offerUuid' is not accepted for mode '" + mode + "'"));
            }
        }

        if (string.Equals(name, "time_challenge", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var hasUuid = arguments.ContainsKey("uuid");
            if (mode is "select" or "queue" or "abandon" && !hasUuid)
                errors.Add(ValidationError("missing_required", "uuid",
                    "required field 'uuid' is missing for mode '" + mode + "'"));
            else if (mode is "reroll" or "state" && hasUuid)
                errors.Add(ValidationError("unexpected_for_mode", "uuid",
                    "field 'uuid' is not accepted for mode '" + mode + "'"));
        }

        if (string.Equals(name, "suite_automation", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var hasFeature = arguments.ContainsKey("feature");
            var hasOn = arguments.ContainsKey("on");
            if (mode == "set")
            {
                if (!hasFeature) errors.Add(ValidationError("missing_required", "feature",
                    "required field 'feature' is missing for mode 'set'"));
                if (!hasOn) errors.Add(ValidationError("missing_required", "on",
                    "required field 'on' is missing for mode 'set'"));
            }
            else
            {
                if (hasFeature) errors.Add(ValidationError("unexpected_for_mode", "feature",
                    "field 'feature' is accepted only for mode 'set'"));
                if (hasOn) errors.Add(ValidationError("unexpected_for_mode", "on",
                    "field 'on' is accepted only for mode 'set'"));
            }
        }

        if (string.Equals(name, "game_ritual", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var level = arguments.ContainsKey("level");
            if (mode == "set_level" && !level)
                errors.Add(ValidationError("missing_required", "level",
                    "required field 'level' is missing for mode 'set_level'"));
            else if (mode != "set_level" && level)
                errors.Add(ValidationError("unexpected_for_mode", "level",
                    "field 'level' is accepted only for mode 'set_level'"));
        }

        if (string.Equals(name, "game_loadout", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var section = arguments.ContainsKey("section");
            var enabled = arguments.ContainsKey("enabled");
            var label = arguments.ContainsKey("name");
            var slot = arguments.ContainsKey("slot");
            var loadout = arguments.ContainsKey("loadout");
            var snapshot = mode is "snapshot_save" or "snapshot_load" or "snapshot_clear";
            if (snapshot && !section)
                errors.Add(ValidationError("missing_required", "section",
                    "required field 'section' is missing for mode '" + mode + "'"));
            if (mode == "set_section" && !section)
                errors.Add(ValidationError("missing_required", "section",
                    "required field 'section' is missing for mode 'set_section'"));
            if (mode == "set_section" && !enabled)
                errors.Add(ValidationError("missing_required", "enabled",
                    "required field 'enabled' is missing for mode 'set_section'"));
            if (mode != "set_section" && !snapshot && section)
                errors.Add(ValidationError("unexpected_for_mode", "section",
                    "field 'section' is accepted only for mode 'set_section' and snapshot modes"));
            if (mode != "set_section" && enabled)
                errors.Add(ValidationError("unexpected_for_mode", "enabled",
                    "field 'enabled' is accepted only for mode 'set_section'"));
            if (mode == "rename" && !label)
                errors.Add(ValidationError("missing_required", "name",
                    "required field 'name' is missing for mode 'rename'"));
            if (mode != "rename" && label)
                errors.Add(ValidationError("unexpected_for_mode", "name",
                    "field 'name' is accepted only for mode 'rename'"));
            if (snapshot && !slot)
                errors.Add(ValidationError("missing_required", "slot",
                    "required field 'slot' is missing for mode '" + mode + "'"));
            if (!snapshot && slot)
                errors.Add(ValidationError("unexpected_for_mode", "slot",
                    "field 'slot' is accepted only for snapshot modes"));
            if (!snapshot && !loadout)
                errors.Add(ValidationError("missing_required", "loadout",
                    "required field 'loadout' is missing for mode '" + mode + "'"));
            if (snapshot && loadout)
                errors.Add(ValidationError("unexpected_for_mode", "loadout",
                    "field 'loadout' is not accepted for snapshot modes"));
        }

        if (string.Equals(name, "game_agromancy", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var action = arguments.ContainsKey("actionUuid");
            if (mode is "add_plot_action" or "remove_plot_action" or
                "add_element_action" or "remove_element_action" && !action)
                errors.Add(ValidationError("missing_required", "actionUuid",
                    "required field 'actionUuid' is missing for mode '" + mode + "'"));
            else if (mode is "add_element" or "remove_element" && action)
                errors.Add(ValidationError("unexpected_for_mode", "actionUuid",
                    "field 'actionUuid' is accepted only for action modes"));
        }

        if (string.Equals(name, "game_spell_level", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var hasRecipe = arguments.ContainsKey("uuid");
            if (mode == "single" && !hasRecipe)
                errors.Add(ValidationError("missing_required", "uuid",
                    "required field 'uuid' is missing for mode 'single'"));
            if (mode == "all" && hasRecipe)
                errors.Add(ValidationError("unexpected_for_mode", "uuid",
                    "field 'uuid' is not accepted for mode 'all'"));
        }

        if (string.Equals(name, "game_spell_loadout", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var subject = arguments.ContainsKey("uuid");
            var glyphs = arguments.ContainsKey("glyphs");
            var slot = arguments.ContainsKey("slot");
            var destination = arguments.ContainsKey("destination");
            if (mode is "preview" or "add")
            {
                if (!subject) errors.Add(ValidationError("missing_required", "uuid",
                    "required field 'uuid' is missing for mode '" + mode + "'"));
                if (!glyphs) errors.Add(ValidationError("missing_required", "glyphs",
                    "required field 'glyphs' is missing for mode '" + mode + "'"));
            }
            else
            {
                if (subject) errors.Add(ValidationError("unexpected_for_mode", "uuid",
                    "field 'uuid' is accepted only for modes 'preview' and 'add'"));
                if (glyphs) errors.Add(ValidationError("unexpected_for_mode", "glyphs",
                    "field 'glyphs' is accepted only for modes 'preview' and 'add'"));
            }
            var slotted = mode is "remove" or "move";
            if (slotted && !slot)
                errors.Add(ValidationError("missing_required", "slot",
                    "required field 'slot' is missing for mode '" + mode + "'"));
            else if (!slotted && slot)
                errors.Add(ValidationError("unexpected_for_mode", "slot",
                    "field 'slot' is accepted only for modes 'remove' and 'move'"));
            if (mode == "move" && !destination)
                errors.Add(ValidationError("missing_required", "destination",
                    "required field 'destination' is missing for mode 'move'"));
            else if (mode != "move" && destination)
                errors.Add(ValidationError("unexpected_for_mode", "destination",
                    "field 'destination' is accepted only for mode 'move'"));
        }

        if (string.Equals(name, "game_targeting", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var target = arguments.ContainsKey("uuid");
            if (mode == "submit" && !target)
                errors.Add(ValidationError("missing_required", "uuid",
                    "required field 'uuid' is missing for mode 'submit'"));
            else if (mode == "randomize" && target)
                errors.Add(ValidationError("unexpected_for_mode", "uuid",
                    "field 'uuid' is not accepted for mode '" + mode + "'"));
        }

        if (string.Equals(name, "game_consumable", StringComparison.Ordinal) &&
            arguments["mode"]?.Type == JTokenType.String)
        {
            var mode = (string?)arguments["mode"];
            var amount = arguments.ContainsKey("amount");
            var enabled = arguments.ContainsKey("enabled");
            var list = arguments.ContainsKey("list");
            var destination = arguments.ContainsKey("destination");
            if (mode == "discard" && !amount)
                errors.Add(ValidationError(
                    "missing_required",
                    "amount",
                    "required field 'amount' is missing for mode 'discard'"));
            if (mode == "set_randomization" && !enabled)
                errors.Add(ValidationError(
                    "missing_required",
                    "enabled",
                    "required field 'enabled' is missing for mode 'set_randomization'"));
            if (mode == "move" && !list)
                errors.Add(ValidationError(
                    "missing_required",
                    "list",
                    "required field 'list' is missing for mode 'move'"));
            if (mode == "move" && !destination)
                errors.Add(ValidationError(
                    "missing_required",
                    "destination",
                    "required field 'destination' is missing for mode 'move'"));
            if (mode != "discard" && amount)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "amount",
                    "field 'amount' is accepted only for mode 'discard'"));
            if (mode != "set_randomization" && enabled)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "enabled",
                    "field 'enabled' is accepted only for mode 'set_randomization'"));
            if (mode != "move" && list)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "list",
                    "field 'list' is accepted only for mode 'move'"));
            if (mode != "move" && destination)
                errors.Add(ValidationError(
                    "unexpected_for_mode",
                    "destination",
                    "field 'destination' is accepted only for mode 'move'"));
        }

        if (errors.Count > 0) throw GameMcpInvalidParamsException.Validation(errors);
    }

    private static JObject ValidationError(string code, string field, string message) => new()
    {
        ["code"] = code,
        ["field"] = field,
        ["message"] = message,
    };

    private static JObject ActionSchema(JObject properties, params string[] required)
    {
        return ObjectSchema(properties, required);
    }

    private static JObject ActionSchemaWithoutIdentity(
        JObject properties,
        params string[] required) => ObjectSchema(properties, required);

    private static JObject ModeSchema(JObject schema, params JObject[] rules)
    {
        schema["allOf"] = new JArray(rules);
        return schema;
    }

    private static JObject ModeRule(
        string mode,
        string[]? required = null,
        string[]? forbidden = null)
    {
        var then = new JObject();
        if (required is { Length: > 0 }) then["required"] = new JArray(required);
        if (forbidden is { Length: > 0 })
        {
            var any = new JArray();
            for (var index = 0; index < forbidden.Length; index++)
                any.Add(new JObject { ["required"] = new JArray(forbidden[index]) });
            then["not"] = new JObject { ["anyOf"] = any };
        }
        return new JObject
        {
            ["if"] = new JObject
            {
                ["properties"] = new JObject
                {
                    ["mode"] = new JObject { ["const"] = mode },
                },
                ["required"] = new JArray("mode"),
            },
            ["then"] = then,
        };
    }

    private static JObject ListResources() => new()
    {
        ["resources"] = new JArray
        {
            Resource("orb://world/overview", "world-overview", "Compact published-world strategy overview."),
            Resource("orb://world/categories", "world-categories", "Discoverable world table inventory and collection status."),
            Resource("orb://suite/health", "suite-health", "Compact feature, service, emergency, collection, and MCP health."),
            Resource("orb://suite/configuration", "suite-configuration", "Committed suite configuration and writable setting catalog."),
            Resource("orb://trace/health", "trace-health", "Trace-writer health and retained volume."),
        },
    };

    private static JObject ListResourceTemplates() => new()
    {
        ["resourceTemplates"] = new JArray
        {
            new JObject
            {
                ["uriTemplate"] = "orb://world/category/{category}",
                ["name"] = "world-category",
                ["title"] = "Published world category",
                ["description"] = "First page of an exact category returned by world_categories.",
                ["mimeType"] = "application/json",
            },
        },
    };

    private static JObject Tool(
        string name,
        string title,
        string description,
        JObject inputSchema,
        bool readOnly = true,
        bool idempotent = true,
        string classification = "")
    {
        var result = new JObject
        {
            ["name"] = name,
            ["title"] = title,
            ["description"] = description,
            ["inputSchema"] = inputSchema,
            ["annotations"] = new JObject
            {
                ["readOnlyHint"] = readOnly,
                ["destructiveHint"] = false,
                ["idempotentHint"] = idempotent,
                ["openWorldHint"] = false,
            },
        };
        if (classification.Length > 0) result["classification"] = classification;
        return result;
    }

    private static JObject Resource(string uri, string name, string description) => new()
    {
        ["uri"] = uri,
        ["name"] = name,
        ["title"] = name.Replace('-', ' '),
        ["description"] = description,
        ["mimeType"] = "application/json",
    };

    private static JObject ObjectSchema(JObject? properties = null, params string[] required)
    {
        var result = new JObject
        {
            ["type"] = "object",
            ["properties"] = properties ?? new JObject(),
            ["additionalProperties"] = false,
        };
        if (required.Length > 0) result["required"] = new JArray(required);
        return result;
    }

    private static JObject StringSchema(string description) => new()
    {
        ["type"] = "string",
        ["description"] = description,
    };

    private static JObject BooleanSchema(string description) => new()
    {
        ["type"] = "boolean",
        ["description"] = description,
    };

    private static JObject ArraySchema(JObject items, int minimum, int maximum) => new()
    {
        ["type"] = "array",
        ["items"] = items,
        ["minItems"] = minimum,
        ["maxItems"] = maximum,
    };

    /// <summary>
    /// A declared ceiling is published; an int.MaxValue placeholder is not. Publishing the
    /// placeholder put a number in the shape of a bound that no part of the game ever chose.
    /// </summary>
    private static JObject IntegerSchema(int minimum, int maximum)
    {
        var schema = new JObject
        {
            ["type"] = "integer",
            ["minimum"] = minimum,
        };
        if (DeclaresCeiling(maximum)) schema["maximum"] = maximum;
        return schema;
    }

    private static JObject UlongSchema(string description) => new()
    {
        ["type"] = "integer",
        ["minimum"] = 1,
        ["description"] = description,
    };

    private static JObject EnumSchema(params string[] values) => new()
    {
        ["type"] = "string",
        ["enum"] = new JArray(values),
    };

    private static JObject RequireObject(JObject source, string name) =>
        source[name] as JObject ??
        throw new GameMcpInvalidParamsException(name + " must be an object");

    private static string RequireString(JObject source, string name)
    {
        if (source[name]?.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        var value = ((string?)source[name] ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new GameMcpInvalidParamsException(name + " must not be empty");
        return value;
    }

    private static string OptionalString(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token)) return string.Empty;
        if (token.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        var value = ((string?)token ?? string.Empty).Trim();
        if (value.Length == 0)
            throw new GameMcpInvalidParamsException(name + " must not be empty when supplied");
        return value;
    }

    private static string[] RequireStringArray(JObject source, string name, int maximum)
    {
        if (source[name] is not JArray array)
            throw new GameMcpInvalidParamsException(name + " must be an array");
        if (array.Count == 0 || array.Count > maximum)
            throw new GameMcpInvalidParamsException(
                name + " must contain between 1 and " + maximum + " entries");
        var result = new string[array.Count];
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index]?.Type != JTokenType.String)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must be a string");
            result[index] = ((string?)array[index] ?? string.Empty).Trim();
            if (result[index].Length == 0)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must not be empty");
        }
        return result;
    }

    /// <summary>
    /// The batch form of <see cref="ReadEntityId"/>. A caller pages a list, reads its handles, and
    /// hands them straight back; an entry the resolver cannot place is named where it stands rather
    /// than reaching the world reader as a UUID nothing published.
    /// </summary>
    private static string[] ReadEntityIdArray(JObject source, string name, int maximum)
    {
        var text = RequireStringArray(source, name, maximum);
        var result = new string[text.Length];
        for (var index = 0; index < text.Length; index++)
        {
            result[index] = ReadEntityId(text[index], name + "[" + index + "]")
                .ToString("D");
        }
        return result;
    }

    private static GameMcpUuidCount[] RequireUuidCountArray(
        JObject source,
        string name,
        int maximum)
    {
        if (source[name] is not JArray values)
            throw new GameMcpInvalidParamsException(name + " must be an array");
        if (values.Count > maximum)
            throw new GameMcpInvalidParamsException(
                name + " accepts at most " + maximum + " rows");
        var result = new GameMcpUuidCount[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            if (values[index] is not JObject row)
                throw new GameMcpInvalidParamsException(
                    name + "[" + index + "] must be an object");
            foreach (var property in row.Properties())
                if (property.Name is not "uuid" and not "count")
                    throw new GameMcpInvalidParamsException(
                        name + "[" + index + "]." + property.Name + " is unexpected");
            result[index] = new GameMcpUuidCount(
                RequireUuid(row, "uuid"),
                RequiredInt(row, "count", 1, int.MaxValue));
        }
        return result;
    }

    private static string RequireRawString(JObject source, string name)
    {
        if (source[name]?.Type != JTokenType.String)
            throw new GameMcpInvalidParamsException(name + " must be a string");
        return (string?)source[name] ?? string.Empty;
    }

    private static int OptionalInt(JObject source, string name, int fallback)
    {
        if (!source.TryGetValue(name, out var token)) return fallback;
        if (token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        try { return token.Value<int>(); }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported integer range");
        }
    }

    private static int RequiredInt(
        JObject source,
        string name,
        int minimum,
        int maximum)
    {
        if (!source.TryGetValue(name, out var token) || token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        int value;
        try { value = token.Value<int>(); }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported integer range");
        }
        if (value < minimum || value > maximum)
        {
            // A floor this schema declares is a real bound. A ceiling of int.MaxValue is not one,
            // and printing it beside the floor published a number that is neither a native limit
            // nor a policy — 2147483647 where the game's own ceiling was 259. Where the schema
            // knows no ceiling it says so, and the game's refusal names the real one.
            throw new GameMcpInvalidParamsException(
                DeclaresCeiling(maximum)
                    ? name + " must be between " + minimum + " and " + maximum
                    : name + " must be " + minimum + " or greater; the game decides how high " +
                      "it may go and names that limit when it refuses");
        }
        return value;
    }

    private static bool DeclaresCeiling(int maximum) => maximum < int.MaxValue - 1;

    private static int OptionalIntInRange(
        JObject source,
        string name,
        int fallback,
        int minimum,
        int maximum)
    {
        if (!source.TryGetValue(name, out _)) return fallback;
        return RequiredInt(source, name, minimum, maximum);
    }

    private static bool OptionalBool(JObject source, string name, bool fallback)
    {
        if (!source.TryGetValue(name, out var token)) return fallback;
        if (token.Type != JTokenType.Boolean)
            throw new GameMcpInvalidParamsException(name + " must be a boolean");
        return token.Value<bool>();
    }

    private static bool RequireBool(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token))
            throw new GameMcpInvalidParamsException(name + " is required");
        if (token.Type != JTokenType.Boolean)
            throw new GameMcpInvalidParamsException(name + " must be a boolean");
        return token.Value<bool>();
    }

    private static ulong RequiredUlong(JObject source, string name)
    {
        var value = OptionalUlong(source, name);
        if (!value.HasValue)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        return value.Value;
    }

    private static ulong? OptionalUlong(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token)) return null;
        if (token.Type != JTokenType.Integer)
            throw new GameMcpInvalidParamsException(name + " must be an integer");
        try
        {
            var value = token.Value<ulong>();
            if (value == 0)
                throw new GameMcpInvalidParamsException(name + " must be greater than zero");
            return value;
        }
        catch (GameMcpInvalidParamsException) { throw; }
        catch (Exception)
        {
            throw new GameMcpInvalidParamsException(name + " is outside the supported unsigned range");
        }
    }

    private static Guid RequireUuid(JObject source, string name)
    {
        var value = OptionalUuid(source, name);
        if (value == Guid.Empty)
            throw new GameMcpInvalidParamsException(
                name + " must be a whole canonical UUID or an id handle that names one published " +
                "entity");
        return value;
    }

    private static Guid OptionalUuid(JObject source, string name)
    {
        if (!source.TryGetValue(name, out _)) return Guid.Empty;
        return ReadEntityId(RequireString(source, name), name);
    }

    /// <summary>
    /// One reader for every id argument: the full canonical UUID the suite calls identity, or the
    /// handle the wire hands out — any prefix that names exactly one published id.
    /// </summary>
    private static Guid ReadEntityId(string text, string name)
    {
        var outcome = GameMcpEntityHandle.Resolve(
            text, EntityIdentityCatalogPublication.Current, out var uuid, out var candidates);
        if (outcome == GameMcpEntityHandle.ResolutionOutcome.Ambiguous)
        {
            var catalog = EntityIdentityCatalogPublication.Current;
            var written = new System.Text.StringBuilder();
            for (var index = 0; index < candidates.Count; index++)
            {
                if (index > 0) written.Append(", ");
                written.Append(GameMcpEntityHandle.Name(candidates[index], catalog))
                    .Append(' ')
                    .Append(candidates[index].ToString("D"));
            }
            throw new GameMcpInvalidParamsException(
                "the id " + text + " given for " +
                name + " names more than one published entity — " + written +
                "; send more characters or the whole UUID",
                "ambiguous_handle");
        }

        // A handle stops resolving the moment a run ends, and answering "that is not an id" for one
        // this same run handed out taught a caller to throw away good ids after any teardown. The
        // whole UUID for the same entity already answered with the lifecycle fact; so does this.
        if (outcome == GameMcpEntityHandle.ResolutionOutcome.CatalogUnavailable)
        {
            throw new GameMcpInvalidParamsException(
                "No entity catalog is published, so no id handle resolves; " +
                "the whole UUID still reads, and handles resolve again once a save is loaded.",
                "entity_catalog_unavailable");
        }
        if (outcome == GameMcpEntityHandle.ResolutionOutcome.NotFound || uuid == Guid.Empty)
        {
            throw new GameMcpInvalidParamsException(
                name + " must be a whole canonical UUID or an id handle that names one published " +
                "entity",
                "invalid_uuid");
        }
        return uuid;
    }

    private static string RequireOneOf(
        JObject source,
        string name,
        params string[] allowed)
    {
        var value = RequireString(source, name);
        for (var index = 0; index < allowed.Length; index++)
            if (string.Equals(value, allowed[index], StringComparison.Ordinal))
                return value;
        throw new GameMcpInvalidParamsException(
            name + " must be one of: " + string.Join(", ", allowed));
    }

    private static string RequireSelector(JObject source, string name)
    {
        if (!source.TryGetValue(name, out var token))
            throw new GameMcpInvalidParamsException(name + " is required");
        if (token.Type == JTokenType.String)
        {
            var label = ((string?)token ?? string.Empty).Trim();
            if (label.Length == 0)
                throw new GameMcpInvalidParamsException(name + " must not be empty");
            return label;
        }
        throw new GameMcpInvalidParamsException(name + " must be an exact player-facing name");
    }

    private static JObject Success(JToken? id, JObject result) => new()
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
        ["result"] = result,
    };

    internal static JObject Error(
        JToken? id,
        int code,
        string message,
        JObject? data = null)
    {
        var error = new JObject
        {
            ["code"] = code,
            ["message"] = message,
        };
        if (data is not null) error["data"] = data;
        return new JObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone() ?? JValue.CreateNull(),
            ["error"] = error,
        };
    }
}

internal sealed class GameMcpToolExecution
{
    internal GameMcpToolExecution(
        GameMcpValue payload,
        byte[]? inlinePng,
        bool isError,
        EntityIdentityCatalogSnapshot? entityIdentities = null)
    {
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        InlinePng = inlinePng;
        IsError = isError;
        EntityIdentities = entityIdentities ?? EntityIdentityCatalogPublication.Current;
    }

    private GameMcpToolExecution(string text)
    {
        TextContent = string.IsNullOrWhiteSpace(text)
            ? throw new ArgumentException("Text tool content must not be empty.", nameof(text))
            : text;
    }

    internal GameMcpValue? Payload { get; }
    internal byte[]? InlinePng { get; }
    internal bool IsError { get; }
    internal string? TextContent { get; }
    internal EntityIdentityCatalogSnapshot EntityIdentities { get; } =
        EntityIdentityCatalogSnapshot.Unbound(0);

    internal static GameMcpToolExecution Read(GameMcpValue payload) =>
        new(payload, null, false);
    internal static GameMcpToolExecution Read(GameMcpObjectBuilder payload) =>
        new(payload.Freeze(), null, false);
    internal static GameMcpToolExecution Error(GameMcpValue payload) =>
        new(payload, null, true);
    internal static GameMcpToolExecution Error(GameMcpObjectBuilder payload) =>
        new(payload.Freeze(), null, true);
    internal static GameMcpToolExecution Text(string text) => new(text);

    internal GameMcpToolExecution WithEntityIdentities(
        EntityIdentityCatalogSnapshot entityIdentities) =>
        TextContent is not null
            ? this
            : new GameMcpToolExecution(
                Payload!, InlinePng, IsError,
                entityIdentities ?? throw new ArgumentNullException(nameof(entityIdentities)));

    internal JObject ToProtocolResult()
    {
        if (TextContent is not null)
        {
            return new JObject
            {
                ["content"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "text",
                        ["text"] = TextContent,
                    },
                },
            };
        }
        // One response, one representation. A page of text is what a caller reads, so it is what the
        // server says; emitting the same answer twice — once as a document and once as prose — made
        // clients decode and truncate the same result twice, and no tool here declares an output
        // schema that would oblige a machine-shaped copy.
        var content = new JArray();
        if (InlinePng is not null)
        {
            content.Add(new JObject
            {
                ["type"] = "image",
                ["data"] = Convert.ToBase64String(InlinePng),
                ["mimeType"] = "image/png",
            });
        }
        content.Add(new JObject
        {
            ["type"] = "text",
            ["text"] = GameMcpTextPage.Render(
                GameMcpDocumentJsonEncoder.Encode(Payload!, EntityIdentities)),
        });
        var result = new JObject { ["content"] = content };
        if (IsError) result["isError"] = true;
        return result;
    }
}

internal readonly struct GameMcpProtocolResponse
{
    private GameMcpProtocolResponse(int statusCode, JObject? body)
    {
        StatusCode = statusCode;
        Body = body;
    }

    internal int StatusCode { get; }
    internal JObject? Body { get; }
    internal static GameMcpProtocolResponse Accepted() => new(202, null);
    internal static GameMcpProtocolResponse Json(JObject body) => new(200, body);
}

/// <summary>
/// A tool call the server will not run because of what it was asked, carrying the producer code
/// that says which kind of no it is.
/// </summary>
internal sealed class GameMcpInvalidParamsException : Exception
{
    internal GameMcpInvalidParamsException(
        string message,
        string code = "argument_validation_failed")
        : base(message)
    {
        Code = code;
    }

    internal string Code { get; }

    internal static GameMcpInvalidParamsException Validation(JArray errors) =>
        new(Sentence(errors));

    // The sentence names every offending field, which is the whole of what a parallel array of
    // {code, field, message} rows said — the same facts once more in a machine shape, on a surface
    // whose one representation is the page a caller reads.
    private static string Sentence(JArray errors)
    {
        var text = new System.Text.StringBuilder("tool arguments failed schema validation");
        var written = 0;
        foreach (var error in errors.OfType<JObject>())
        {
            var message = (string?)error["message"];
            if (string.IsNullOrEmpty(message)) continue;
            text.Append(written == 0 ? ": " : "; ").Append(message);
            written++;
        }
        return text.ToString();
    }
}
#endif

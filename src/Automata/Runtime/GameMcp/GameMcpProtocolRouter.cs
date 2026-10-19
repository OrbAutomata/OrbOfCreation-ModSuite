#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static OrbAutomata.GameMcp.GameMcpToolArguments;

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
    private readonly GameMcpToolCatalog _catalog;
    private readonly Action<string>? _logError;

    internal GameMcpProtocolRouter(
        GameMcpFrameInbox operations,
        GameMcpToolCatalog catalog,
        Action<string>? logError = null)
    {
        _operations = operations ?? throw new ArgumentNullException(nameof(operations));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _logError = logError;
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
            // The exception's own text names types and members from inside the suite and the game.
            // A caller can act on none of it, and it is the one thing whoever fixes this needs — so
            // it goes to the suite log under a reference the caller is given and can quote back.
            var reference =
                "MCP-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
            _logError?.Invoke(
                "Game MCP internal error " + reference + " on " + method + ": " + exception);
            return GameMcpProtocolResponse.Json(
                Error(id, -32603, InternalErrorReason(method!, reference)));
        }
    }

    /// <summary>
    /// What a caller is told when a tool throws. The exception's own text names types and members
    /// from inside the suite and the game; a caller can act on none of it, and it is the one thing
    /// whoever fixes this needs — so it goes to the suite log under a reference they are handed.
    /// </summary>
    internal static string InternalErrorReason(string method, string reference) =>
        "The suite hit an internal error handling " + method + ", so nothing was applied. " +
        "Reference " + reference + " is in the suite log; quoting it is what a report of this " +
        "needs.";

    /// <summary>
    /// What a caller is told when Unity does not pick a call up. It named the wait in milliseconds
    /// and called the call "canceled before execution", which is the inbox's own vocabulary about
    /// the inbox's own states; what a caller can act on is that the game is busy and nothing
    /// landed.
    /// </summary>
    internal const string ClaimTimeoutReason =
        "The game did not pick this up within two seconds — it is likely paused or loading. " +
        "Nothing was applied.";

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
            if (_catalog.TryFind(name, out var tool)) ValidateToolArguments(tool, arguments);
            return SubmitAndWait(_catalog.BuildOperation(name, arguments)).ToProtocolResult();
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
        if (!_catalog.TryFindResource(uri, out var resource))
            throw new GameMcpInvalidParamsException(
                "unknown resource URI '" + uri + "'; call resources/list");
        var operation = new GameMcpOperationRequestBuilder
        {
            ToolName = "resource_read",
            Route = resource.Route,
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
                ["reason"] = ClaimTimeoutReason,
            }.Freeze());
            if (operation.Completion.TryCancelBeforeClaim(canceled)) return canceled;
            terminal = operation.Completion.WaitForClaimedTerminal();
        }
        return terminal;
    }

    private JObject ListTools() => new()
    {
        ["tools"] = new JArray(_catalog.Tools
            .Select(static tool => tool.Listing.ToJson(tool.Name))),
    };

    private JObject ListResources() => new()
    {
        ["resources"] = new JArray(_catalog.Resources
            .Where(static resource => !resource.IsTemplate)
            .Select(static resource => Resource("uri", resource))),
    };

    private JObject ListResourceTemplates() => new()
    {
        ["resourceTemplates"] = new JArray(_catalog.Resources
            .Where(static resource => resource.IsTemplate)
            .Select(static resource => Resource("uriTemplate", resource))),
    };

    private static JObject Resource(string addressKey, GameMcpResourceDescriptor resource) => new()
    {
        [addressKey] = resource.Uri,
        ["name"] = resource.Name,
        ["title"] = resource.Title,
        ["description"] = resource.Description,
        ["mimeType"] = "application/json",
    };

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
#endif

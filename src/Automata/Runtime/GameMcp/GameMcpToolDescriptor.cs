#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpToolDescriptor
{
    internal GameMcpToolDescriptor(
        string name,
        GameMcpToolListing listing,
        Action<JObject, GameMcpOperationRequestBuilder> bind,
        params GameMcpToolRoute[] routes)
        : this(name, listing, bind, null, routes)
    {
    }

    internal GameMcpToolDescriptor(
        string name,
        GameMcpToolListing listing,
        Action<JObject, GameMcpOperationRequestBuilder> bind,
        Action<JObject, List<string>>? validate,
        params GameMcpToolRoute[] routes)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Listing = listing ?? throw new ArgumentNullException(nameof(listing));
        Bind = bind ?? throw new ArgumentNullException(nameof(bind));
        Validate = validate;
        Routes = routes ?? throw new ArgumentNullException(nameof(routes));
    }

    internal string Name { get; }
    internal GameMcpToolListing Listing { get; }
    internal Action<JObject, GameMcpOperationRequestBuilder> Bind { get; }
    internal Action<JObject, List<string>>? Validate { get; }

    internal IReadOnlyList<GameMcpToolRoute> Routes { get; }

    internal GameMcpToolRoute RouteFor(GameMcpOperationRequestBuilder request)
    {
        for (var index = 0; index < Routes.Count; index++)
            if (Routes[index].Applies(request)) return Routes[index];
        throw new InvalidOperationException(Name + " has no route for mode '" + request.Mode + "'");
    }
}

internal sealed class GameMcpToolListing
{
    private readonly string _title;
    private readonly string _description;
    private readonly bool _readOnly;
    private readonly bool _idempotent;
    private readonly string _classification;

    internal GameMcpToolListing(
        string title,
        string description,
        JObject inputSchema,
        bool readOnly = true,
        bool idempotent = true,
        string classification = "")
    {
        _title = title ?? throw new ArgumentNullException(nameof(title));
        _description = description ?? throw new ArgumentNullException(nameof(description));
        InputSchema = inputSchema ?? throw new ArgumentNullException(nameof(inputSchema));
        _readOnly = readOnly;
        _idempotent = idempotent;
        _classification = classification ?? throw new ArgumentNullException(nameof(classification));
    }

    internal JObject InputSchema { get; }

    internal JObject ToJson(string name)
    {
        var result = new JObject
        {
            ["name"] = name,
            ["title"] = _title,
            ["description"] = _description,
            ["inputSchema"] = InputSchema.DeepClone(),
            ["annotations"] = new JObject
            {
                ["readOnlyHint"] = _readOnly,
                ["destructiveHint"] = false,
                ["idempotentHint"] = _idempotent,
                ["openWorldHint"] = false,
            },
        };
        if (_classification.Length > 0) result["classification"] = _classification;
        return result;
    }
}

internal sealed class GameMcpToolRoute
{
    private readonly Func<GameMcpOperationRequestBuilder, bool>? _when;

    private GameMcpToolRoute(
        Func<GameMcpOperationRequestBuilder, bool>? when,
        GameMcpOperationClass classification,
        GameMcpFrameData requiredData,
        GameMcpCommandKind? kind,
        Func<GameMcpOperationRequest, GameMcpFrameContext, GameMcpToolExecution>? read,
        Func<GameMcpFrameExecutor, GameMcpOperationRequest, GameMcpToolExecution>? hostRead)
    {
        _when = when;
        Classification = classification;
        RequiredData = requiredData;
        Kind = kind;
        Read = read;
        HostRead = hostRead;
    }

    internal GameMcpOperationClass Classification { get; }
    internal GameMcpFrameData RequiredData { get; }
    internal GameMcpCommandKind? Kind { get; }
    internal Func<GameMcpOperationRequest, GameMcpFrameContext, GameMcpToolExecution>? Read { get; }
    internal Func<GameMcpFrameExecutor, GameMcpOperationRequest, GameMcpToolExecution>? HostRead { get; }

    internal bool IsDefault => _when is null;
    internal bool Applies(GameMcpOperationRequestBuilder request) => _when?.Invoke(request) ?? true;

    internal static GameMcpToolRoute Command(
        GameMcpOperationClass classification,
        GameMcpFrameData requiredData,
        GameMcpCommandKind kind,
        Func<GameMcpOperationRequestBuilder, bool>? when = null) =>
        new(when, classification, requiredData, kind, null, null);

    internal static GameMcpToolRoute FrameRead(
        GameMcpOperationClass classification,
        GameMcpFrameData requiredData,
        Func<GameMcpOperationRequest, GameMcpFrameContext, GameMcpToolExecution> read,
        Func<GameMcpOperationRequestBuilder, bool>? when = null) =>
        new(when, classification, requiredData, null,
            read ?? throw new ArgumentNullException(nameof(read)), null);

    internal static GameMcpToolRoute ExecutorRead(
        GameMcpOperationClass classification,
        GameMcpFrameData requiredData,
        Func<GameMcpFrameExecutor, GameMcpOperationRequest, GameMcpToolExecution> read,
        Func<GameMcpOperationRequestBuilder, bool>? when = null) =>
        new(when, classification, requiredData, null, null,
            read ?? throw new ArgumentNullException(nameof(read)));
}

internal sealed class GameMcpResourceDescriptor
{
    internal GameMcpResourceDescriptor(
        string uri,
        string name,
        string title,
        string description,
        GameMcpFrameData requiredData,
        Func<GameMcpOperationRequest, GameMcpFrameContext, GameMcpToolExecution> read)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description ?? throw new ArgumentNullException(nameof(description));
        Route = GameMcpToolRoute.FrameRead(GameMcpOperationClass.ReadOnly, requiredData, read);
    }

    internal string Uri { get; }
    internal string Name { get; }
    internal string Title { get; }
    internal string Description { get; }
    internal GameMcpToolRoute Route { get; }
    internal bool IsTemplate => Uri.IndexOf('{') >= 0;

    internal bool Matches(string uri) => IsTemplate
        ? uri.StartsWith(Uri.Substring(0, Uri.IndexOf('{')), StringComparison.Ordinal)
        : string.Equals(uri, Uri, StringComparison.Ordinal);
}
#endif

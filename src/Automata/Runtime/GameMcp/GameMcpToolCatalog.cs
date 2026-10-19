#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using static OrbAutomata.GameMcp.GameMcpOperationClass;

namespace OrbAutomata.GameMcp;

internal sealed class GameMcpToolCatalog
{
    internal const GameMcpFrameData World = GameMcpFrameData.World;
    internal const GameMcpFrameData None = GameMcpFrameData.None;
    internal const GameMcpFrameData GameplayData =
        GameMcpFrameData.World | GameMcpFrameData.Configuration;
    internal const GameMcpFrameData HealthData = GameMcpFrameData.World |
        GameMcpFrameData.Configuration | GameMcpFrameData.FeatureHealth |
        GameMcpFrameData.ServiceHealth | GameMcpFrameData.Scene |
        GameMcpFrameData.NativeContractHealth;
    internal const GameMcpFrameData ConfigurationData =
        GameMcpFrameData.Configuration | GameMcpFrameData.WritableConfiguration;

    // The world too: what each category cost the pass that produced it travels on the
    // publication, and that is the half of trace health a session driving the game can act on.
    internal const GameMcpFrameData TraceData =
        GameMcpFrameData.TraceWriterHealth | GameMcpFrameData.World;
    internal const GameMcpFrameData UiData = GameMcpFrameData.World | GameMcpFrameData.Scene;
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

    internal static readonly Action<JObject, GameMcpOperationRequestBuilder> NoArguments =
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

    internal IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        var registered = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < Tools.Count; index++)
        {
            var tool = Tools[index];
            if (!registered.Add(tool.Name)) problems.Add(tool.Name + " is registered twice");
            if (tool.Routes.Count == 0) problems.Add(tool.Name + " has no route");
            for (var route = 0; route < tool.Routes.Count; route++)
                AddRouteProblems(problems, tool, route);
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
        GameMcpWorldTools.Tools(),
        GameMcpSuiteTools.Tools(),
        GameMcpGameplayTools.Tools(),
        GameMcpScreenTools.Tools(),
    }.SelectMany(static family => family).ToArray();

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

    internal static GameMcpToolExecution Overview(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Read(GameMcpWorldOverview.Overview(context).Freeze());

    internal static GameMcpToolExecution Categories(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Read(GameMcpWorldOverview.ListCategories(context).Freeze());

    internal static GameMcpToolExecution SuiteHealth(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Text(GameMcpHealthProjection.ProjectGameMcpHealthText(context));

    internal static GameMcpToolExecution SuiteConfiguration(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Read(GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
            context, request.Mode == "describe", request.Section));

    internal static GameMcpToolExecution TraceHealth(
        GameMcpOperationRequest request,
        GameMcpFrameContext context) =>
        GameMcpToolExecution.Text(GameMcpHealthProjection.ProjectGameMcpTraceHealthText(context));
}
#endif

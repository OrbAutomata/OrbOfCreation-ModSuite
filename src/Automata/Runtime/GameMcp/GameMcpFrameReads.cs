#if SERVICE_CYCLE_PROFILE
using System;

namespace OrbAutomata.GameMcp;

internal static class GameMcpFrameReads
{
    internal static bool TryExecuteFrameRead(
        GameMcpOperationRequest request,
        GameMcpFrameContext context,
        out GameMcpToolExecution execution)
    {
        switch (request.ToolName)
        {
            case "world_overview":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.Overview(context).Freeze());
                return true;
            case "world_categories":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ListCategories(context).Freeze());
                return true;
            case "world_list":
                execution = GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
                    context,
                    request.Category,
                    request.Offset,
                    request.Limit,
                    request.AffordableOnly,
                    request.LimitFromCaller,
                    request.DiscoveredFilter).Freeze());
                return true;
            case "world_get":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.GetRows(
                        context,
                        request.Category,
                        request.Uuids).Freeze());
                return true;
            case "world_search":
                execution = GameMcpToolExecution.Read(
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
                        request.ModifiesFilter).Freeze());
                return true;
            case "suite_health":
                execution = GameMcpToolExecution.Text(
                    GameMcpHealthProjection.ProjectGameMcpHealthText(context));
                return true;
            case "strategist":
                execution = GameMcpStrategistQuery.Read(context, request.Section);
                return true;
            case "suite_configuration":
                execution = GameMcpToolExecution.Read(
                    GameMcpConfigurationProjection.ProjectGameMcpConfiguration(
                        context, request.Mode == "describe", request.Section));
                return true;
            case "suite_breakers" when request.Mode == "list":
                execution = GameMcpToolExecution.Read(
                    GameMcpAutomationFeatures.ProjectGameMcpAutomationFeatures(context));
                return true;
            case "time_challenge" when request.Mode == "state":
                execution = GameMcpToolExecution.Read(
                    GameMcpWorldQuery.ChallengeStateRead(context).Freeze());
                return true;
            case "trace_health":
                execution = GameMcpToolExecution.Text(
                    GameMcpHealthProjection.ProjectGameMcpTraceHealthText(context));
                return true;
        }
        execution = null!;
        return false;
    }

    internal static GameMcpToolExecution ProjectGameMcpCommand(
        GameMcpCommand command,
        GameMcpCommandResult result)
    {
        if (command.Kind == GameMcpCommandKind.TooltipRead &&
            result.InlinePng is null &&
            string.Equals(result.Status, "committed", StringComparison.Ordinal) &&
            result.Details is GameMcpObject tooltip &&
            TryReadText(tooltip, out var text))
            return GameMcpToolExecution.Text(text);
        return new GameMcpToolExecution(
            result.Project(command),
            result.InlinePng,
            result.IsProtocolError);
    }

    private static bool TryReadText(GameMcpObject document, out string text)
    {
        for (var index = 0; index < document.Properties.Count; index++)
        {
            var property = document.Properties[index];
            if (property.Name == "text" && property.Value is GameMcpScalar scalar &&
                scalar.Value is string value && value.Length > 0)
            {
                text = value;
                return true;
            }
        }
        text = string.Empty;
        return false;
    }

    internal static GameMcpToolExecution ExecuteGameMcpResource(
        string uri,
        GameMcpFrameContext context)
    {
        if (uri == "orb://world/overview")
            return GameMcpToolExecution.Read(GameMcpWorldQuery.Overview(context).Freeze());
        if (uri == "orb://world/categories")
            return GameMcpToolExecution.Read(GameMcpWorldQuery.ListCategories(context).Freeze());
        if (uri == "orb://suite/health")
            return GameMcpToolExecution.Text(
                GameMcpHealthProjection.ProjectGameMcpHealthText(context));
        if (uri == "orb://suite/configuration")
            return GameMcpToolExecution.Read(
                GameMcpConfigurationProjection.ProjectGameMcpConfiguration(context, describe: false));
        if (uri == "orb://trace/health")
            return GameMcpToolExecution.Text(
                GameMcpHealthProjection.ProjectGameMcpTraceHealthText(context));
        var category = Uri.UnescapeDataString(
            uri.Substring("orb://world/category/".Length));
        return GameMcpToolExecution.Read(GameMcpWorldQuery.ListRows(
            context,
            category,
            0,
            GameMcpWorldQuery.DefaultLimit,
            limitFromCaller: false).Freeze());
    }
}
#endif

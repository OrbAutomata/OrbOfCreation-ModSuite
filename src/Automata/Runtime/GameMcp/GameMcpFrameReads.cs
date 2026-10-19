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
        if (request.Route.Read is not { } read)
        {
            execution = null!;
            return false;
        }
        execution = read(request, context);
        return true;
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
}
#endif

#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using System.Text;

namespace OrbAutomata.GameMcp;

/// <summary>
/// One always-on ledger line per completed MCP operation: what was asked for, how it went, what it
/// cost, and which frame it finished on.
/// </summary>
/// <remarks>
/// The line used to read <c>Game MCP operation 85 completed committed (committed):</c> — a sequence
/// number, a disposition said twice, and an empty trailer. It named no verb, so thirteen of them
/// across one session said nothing about what that session did; it carried no duration, so a command
/// that spent several frames waiting for post-state settlement read like an instant lookup; and it
/// had no timestamp of any kind, so nothing tied it to the trace recorded beside it. The frame is
/// the join: pump and capture records carry the same counter, so a line here lands on an exact trace
/// offset instead of being anchored by hand against wall-clock evidence.
/// </remarks>
internal static class GameMcpOperationLedger
{
    internal static string Describe(GameMcpCommand command, GameMcpCommandResult result, long frame)
    {
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (result is null) throw new ArgumentNullException(nameof(result));
        return Describe(command, result, frame, command.ElapsedMilliseconds);
    }

    internal static string Describe(
        GameMcpCommand command,
        GameMcpCommandResult result,
        long frame,
        double elapsedMilliseconds)
    {
        var text = new StringBuilder("Game MCP operation ")
            .Append(command.Sequence.ToString(CultureInfo.InvariantCulture));
        var verb = command.ToolName;
        if (verb.Length != 0) text.Append(" (").Append(verb).Append(')');
        text.Append(" completed ").Append(result.Status);
        if (!string.Equals(result.Status, result.Code, StringComparison.Ordinal))
            text.Append(" (").Append(result.Code).Append(')');
        text.Append(" in ")
            .Append(elapsedMilliseconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" ms at frame ")
            .Append(frame.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(result.Reason)) text.Append(": ").Append(result.Reason);
        return text.Append('.').ToString();
    }
}
#endif

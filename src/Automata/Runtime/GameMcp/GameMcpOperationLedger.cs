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
/// <para>
/// The line used to read <c>Game MCP operation 85 completed committed (committed):</c> — a sequence
/// number, a disposition said twice, and an empty trailer. It named no verb, so thirteen of them
/// across one session said nothing about what that session did; it carried no duration, so a command
/// that spent several frames waiting for post-state settlement read like an instant lookup; and it
/// had no timestamp of any kind, so nothing tied it to the trace recorded beside it. The frame is
/// the join: pump and capture records carry the same counter, so a line here lands on an exact trace
/// offset instead of being anchored by hand against wall-clock evidence.
/// </para>
/// <para>
/// Reads write the same line. They always drew an operation number and never wrote a completion, so
/// the ledger was a sequence with holes in it and the question "what did that read cost" had no
/// answer on any surface: one session measured a 200-id batch by watching a frame counter against a
/// wall clock, because that was the only instrument left. A read's line carries two things a
/// mutation's does not — a summary of what was asked for, and how much came back — because those
/// are the two facts that separate one read of a category from another.
/// </para>
/// <para>
/// The summary names the category, the page and the number of ids; never the ids themselves. A
/// two-hundred-id batch is one number here and the ids are the caller's own argument, so a log
/// nobody reads for identity does not become the place they are kept.
/// </para>
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
        if (command is null) throw new ArgumentNullException(nameof(command));
        if (result is null) throw new ArgumentNullException(nameof(result));
        return Line(
            command.Sequence,
            command.ToolName,
            Arguments(command),
            result.Status,
            result.Code,
            string.Empty,
            elapsedMilliseconds,
            frame,
            result.Reason);
    }

    /// <summary>
    /// The line for an operation the frame answered itself: every read, and every command refused or
    /// committed without leaving the frame it was claimed on.
    /// </summary>
    internal static string DescribeAnswered(
        GameMcpFrameOperation operation,
        GameMcpToolExecution execution,
        long frame,
        double elapsedMilliseconds)
    {
        if (operation is null) throw new ArgumentNullException(nameof(operation));
        if (execution is null) throw new ArgumentNullException(nameof(execution));
        var answer = GameMcpAnsweredOperation.Read(execution);
        return Line(
            operation.Sequence,
            operation.Request.ToolName,
            Arguments(operation.Request),
            answer.Disposition,
            answer.Code,
            answer.Size,
            elapsedMilliseconds,
            frame,
            answer.Reason);
    }

    private static string Line(
        long sequence,
        string verb,
        string arguments,
        string disposition,
        string code,
        string size,
        double elapsedMilliseconds,
        long frame,
        string reason)
    {
        var text = new StringBuilder("Game MCP operation ")
            .Append(sequence.ToString(CultureInfo.InvariantCulture));
        if (verb.Length != 0)
        {
            text.Append(" (").Append(verb);
            if (arguments.Length != 0) text.Append(' ').Append(arguments);
            text.Append(')');
        }
        text.Append(" completed ").Append(disposition);
        if (!string.Equals(disposition, code, StringComparison.Ordinal) && code.Length != 0)
            text.Append(" (").Append(code).Append(')');
        if (size.Length != 0) text.Append(' ').Append(size);
        text.Append(" in ")
            .Append(elapsedMilliseconds.ToString("F1", CultureInfo.InvariantCulture))
            .Append(" ms at frame ")
            .Append(frame.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(reason)) text.Append(": ").Append(reason);

        // Refusals are written as sentences and arrive with their own full stop, so the line ends
        // itself only when the trailer did not.
        var last = text[text.Length - 1];
        return last is '.' or '!' or '?' ? text.ToString() : text.Append('.').ToString();
    }

    /// <summary>
    /// Which press this was, for a mutation that left the frame it was claimed on.
    /// </summary>
    /// <remarks>
    /// A verb whose modes do different things says which one it did. The refused and the read lines
    /// already carried <c>mode=</c>, so the ledger named the mode of every challenge press except
    /// the ones that landed: five committed <c>time_challenge</c> lines in one session said only
    /// that the verb had committed, and the offer fetch — the heaviest press the verb has — read
    /// exactly like a queue toggle.
    /// </remarks>
    private static string Arguments(GameMcpCommand command)
    {
        var text = new StringBuilder();
        Add(text, "mode", command.Mode ?? string.Empty);
        if (command.Amount > 1)
            Add(text, "amount", command.Amount.ToString(CultureInfo.InvariantCulture));
        return text.ToString();
    }

    /// <summary>What the caller asked for, in the arguments that change which rows come back.</summary>
    private static string Arguments(GameMcpOperationRequest request)
    {
        var text = new StringBuilder();
        Add(text, "category", request.Category);
        Add(text, "mode", request.Mode);
        Add(text, "key", request.Key);
        Add(text, "query", request.Query);
        Add(text, "state", request.StateFilter);
        Add(text, "run", request.RunFilter);
        if (request.Uuids.Length != 0)
            Add(text, "uuids", request.Uuids.Length.ToString(CultureInfo.InvariantCulture));
        if (request.Offset != 0)
            Add(text, "offset", request.Offset.ToString(CultureInfo.InvariantCulture));
        if (request.LimitFromCaller)
            Add(text, "limit", request.Limit.ToString(CultureInfo.InvariantCulture));
        if (request.Amount > 1)
            Add(text, "amount", request.Amount.ToString(CultureInfo.InvariantCulture));
        if (request.AffordableOnly) Add(text, "affordable", "true");
        return text.ToString();
    }

    private static void Add(StringBuilder text, string name, string value)
    {
        if (value.Length == 0) return;
        if (text.Length != 0) text.Append(' ');
        text.Append(name).Append('=').Append(value);
    }
}

/// <summary>
/// How an answered operation went, read off the answer itself rather than restated beside it.
/// </summary>
/// <remarks>
/// A read carries its outcome in the document it returns, so the ledger reads the same words the
/// caller does: a page that refuses says so in its own status and code, and one that answers says
/// nothing at all. Deriving the line from anything else would let the log and the wire disagree.
/// </remarks>
internal readonly struct GameMcpAnsweredOperation
{
    private GameMcpAnsweredOperation(string disposition, string code, string size, string reason)
    {
        Disposition = disposition;
        Code = code;
        Size = size;
        Reason = reason;
    }

    internal string Disposition { get; }
    internal string Code { get; }
    internal string Size { get; }
    internal string Reason { get; }

    internal static GameMcpAnsweredOperation Read(GameMcpToolExecution execution)
    {
        if (execution.TextContent is { } text)
        {
            return new GameMcpAnsweredOperation(
                "read",
                string.Empty,
                Encoding.UTF8.GetByteCount(text).ToString(CultureInfo.InvariantCulture) + " bytes",
                string.Empty);
        }
        if (execution.Payload is not GameMcpObject document)
            return new GameMcpAnsweredOperation("read", string.Empty, string.Empty, string.Empty);

        var status = string.Empty;
        var code = string.Empty;
        var reason = string.Empty;
        var size = string.Empty;
        for (var index = 0; index < document.Properties.Count; index++)
        {
            var property = document.Properties[index];
            switch (property.Name)
            {
                case "status":
                    status = Text(property.Value);
                    break;
                case "reasonCode":
                case "code":
                    if (code.Length == 0) code = Text(property.Value);
                    break;
                case "reason":
                    reason = Text(property.Value);
                    break;

                // The collections a paged answer delivers rows in. Named rather than taken as
                // "whatever array comes first", because a refusal's own list of unavailable
                // categories is an array on the same document and is not what came back.
                case "rows":
                case "results":
                case "categories":
                case "features":
                    if (size.Length == 0 && property.Value is GameMcpArray delivered)
                    {
                        size = delivered.Items.Count.ToString(CultureInfo.InvariantCulture) +
                            " rows";
                    }
                    break;
            }
        }

        return status switch
        {
            "" or "available" => new GameMcpAnsweredOperation("read", string.Empty, size, reason),
            "unavailable" or "not_available" =>
                new GameMcpAnsweredOperation("refused", code, string.Empty, reason),
            _ => new GameMcpAnsweredOperation(status, code, size, reason),
        };
    }

    private static string Text(GameMcpValue value) =>
        value is GameMcpScalar scalar && scalar.Value is string text ? text : string.Empty;
}
#endif

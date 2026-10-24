#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

using static OrbAutomata.GameMcp.GameMcpTextBlocks;
using static OrbAutomata.GameMcp.GameMcpTextCells;
using static OrbAutomata.GameMcp.GameMcpTextContext;
using static OrbAutomata.GameMcp.GameMcpTextTable;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTextPage
{

    internal static string Render(JToken document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var lines = new List<string>();
        var said = new Said();
        switch (document)
        {
            case JObject page:
                WriteObject(page, string.Empty, lines, said);
                break;
            case JArray page:
            {
                var outer = said.Enter("rows");
                WriteArray("rows", page, null, null, string.Empty, lines, said);
                said.Leave(outer);
                break;
            }
            default:
                lines.Add(Scalar(document));
                break;
        }
        return lines.Count == 0 ? "(empty)" : string.Join("\n", lines);
    }

    internal static void WriteObject(
        JObject item,
        string indent,
        List<string> lines,
        Said said)
    {
        item = Unwrap(item);
        var verdict = Verdict(item, said);
        if (verdict is not null) lines.Add(indent + verdict);

        // Pagination describes the page, not any row in it, so the table header says it and these
        // never become lines of their own — but only where there is a table to say it on.
        var paged = verdict is null ? TableProperty(item) : null;
        var declared = paged is null ? null : Declared(item);
        var opened = lines.Count;
        string? outcome = null;
        foreach (var property in item.Properties())
        {
            if (verdict is not null && property.Name is "status" or "reasonCode" or "reason")
                continue;
            // A page that exists is a page that was available. Only a verdict with something to say
            // gets a line of its own.
            if (verdict is null && property.Name == "status" &&
                (string?)property.Value is "available" or "committed")
            {
                outcome = (string?)property.Value;
                continue;
            }
            if (paged is not null && property.Name is "total" or "nextOffset" or "columns") continue;
            var counted = property.Name == paged;
            WriteProperty(
                property.Name,
                property.Value,
                counted ? item["total"] : null,
                counted ? item["nextOffset"] : null,
                counted ? declared : null,
                indent,
                lines,
                said);
        }

        // The status word is dropped because the page beneath it already proves it. Where an action
        // has no observable payload — a loadout swap the game exposes no read for, a menu return —
        // there is no page beneath it, and dropping the one word it had left rendered a press that
        // landed as the literal `(empty)`. A press that worked says so and stops; that is the whole
        // honest answer, and inventing a second fact to sit under it would be the older lie again.
        if (lines.Count == opened && outcome is not null) lines.Add(indent + outcome);
    }

    /// <summary>
    /// <c>{"row": {...}}</c> and its kin are envelopes, not facts: one object wrapped in one key
    /// that names nothing the caller asked about. The page says what is inside.
    /// </summary>
    internal static JObject Unwrap(JObject item)
    {
        while (item.Count == 1)
        {
            var only = item.First as JProperty;
            if (only?.Value is not JObject inner) return item;
            item = inner;
        }
        return item;
    }

    /// <summary>
    /// The array this page's count, next offset and declared columns describe, or nothing where the
    /// object is not a page. An empty row array is a page too: a page that matched nothing still
    /// answers the question it was asked, so it reads as the same table with no rows rather than as
    /// a sentence in a second grammar.
    /// </summary>
    /// <remarks>
    /// A count belongs to the rows it counted. The rows are the page; a second array beside them
    /// counts itself.
    /// </remarks>
    internal static string? TableProperty(JObject item)
    {
        string? sole = null;
        foreach (var property in item.Properties())
        {
            if (property.Value is not JArray array) continue;
            if (array.Count > 0 && array[0] is not JObject) continue;
            if (property.Name is "rows" or "results") return property.Name;
            sole = sole is null ? property.Name : string.Empty;
        }
        return sole is { Length: > 0 } ? sole : null;
    }

    internal static void WriteProperty(
        string name,
        JToken value,
        JToken? total,
        JToken? nextOffset,
        IReadOnlyList<string>? declared,
        string indent,
        List<string> lines,
        Said said)
    {
        switch (value)
        {
            case null:
            case JValue { Type: JTokenType.Null }:
                return;
            // Everything under a key is said under that key's name. Two sibling decisions are two
            // places and each keeps its sentence; the rows of one array are one place and still say
            // a shared sentence once.
            case JArray array:
            {
                var outer = said.Enter(name);
                WriteArray(
                    name,
                    array,
                    TableSuffix(array, total, nextOffset),
                    declared,
                    indent,
                    lines,
                    said);
                said.Leave(outer);
                return;
            }
            case JObject nested:
            {
                nested = Unwrap(nested);
                var outer = said.Enter(name);
                if (IsPredicates(name) && TryPredicates(nested, name, indent, lines, said))
                {
                    said.Leave(outer);
                    return;
                }
                // A page under a key is that key's page. Naming the envelope as well would spend a
                // line and an indent level saying the key twice.
                if (TryNestedPage(nested, out var pageRows, out var pageTotal, out var pageNext))
                {
                    WriteArray(
                        name,
                        pageRows,
                        TableSuffix(pageRows, pageTotal, pageNext),
                        Declared(nested),
                        indent,
                        lines,
                        said);
                    said.Leave(outer);
                    return;
                }
                var inline = TryInline(nested, InlineBudget, said);
                if (inline is not null)
                {
                    lines.Add(indent + name + ": " + inline);
                    said.Leave(outer);
                    return;
                }
                lines.Add(indent + name + ":");
                WriteObject(nested, indent + Indent, lines, said);
                said.Leave(outer);
                return;
            }
            default:
            {
                var scalar = Scalar(value);
                // A value the game wrote as several paragraphs is rendered as several paragraphs.
                // Continuation lines are indented so a paragraph can never be mistaken for the next
                // key, which is the one thing the flat page grammar needs from them.
                if (scalar.IndexOf('\n') < 0)
                {
                    lines.Add(indent + name + ": " + scalar);
                    return;
                }
                lines.Add(indent + name + ":");
                var paragraphs = scalar.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (var index = 0; index < paragraphs.Length; index++)
                {
                    var paragraph = paragraphs[index].TrimEnd();
                    lines.Add(paragraph.Length == 0 ? string.Empty : indent + Indent + paragraph);
                }
                return;
            }
        }
    }

    /// <summary>
    /// A page sitting under a key: its rows, and the count and resume offset that describe them.
    /// </summary>
    /// <remarks>
    /// Narrow on purpose. Only an object holding a row array and nothing besides the three keys
    /// that page it is one of these; a block that happens to carry a list — a load budget and its
    /// usage rows — has facts of its own and stays a block.
    /// </remarks>
    internal static bool TryNestedPage(
        JObject item,
        out JArray rows,
        out JToken? total,
        out JToken? nextOffset)
    {
        rows = EmptyRows;
        total = item["total"];
        nextOffset = item["nextOffset"];
        if (total is null && nextOffset is null) return false;
        if (TableProperty(item) is not { } page || item[page] is not JArray found) return false;
        foreach (var property in item.Properties())
        {
            if (property.Name == page) continue;
            if (property.Name is not ("total" or "nextOffset" or "columns")) return false;
        }
        rows = found;
        return true;
    }

    internal static readonly JArray EmptyRows = new();

    internal static bool IsPredicates(string name) =>
        string.Equals(name, "predicates", StringComparison.Ordinal);

    /// <summary>
    /// One predicate slot's verdict, where the slot says nothing but the verdict.
    /// </summary>
    /// <remarks>
    /// A slot that carries anything else — <c>canUse</c> and the slot numbers the spell sits in —
    /// is not repetition and never joins a fold.
    /// </remarks>
    internal static bool IsPredicateVerdict(
        JToken value,
        out bool available,
        out string? code,
        out string? reason)
    {
        available = false;
        code = null;
        reason = null;
        if (value is not JObject item ||
            item["available"] is not JValue { Type: JTokenType.Boolean } flag)
        {
            return false;
        }
        foreach (var property in item.Properties())
        {
            if (property.Name is not ("available" or "reasonCode" or "reason")) return false;
        }
        available = (bool)flag;
        code = (string?)item["reasonCode"];
        reason = (string?)item["reason"];
        return true;
    }

    /// <summary>
    /// The predicate block with its repetition taken out: an all-affirmative block on one line, and
    /// predicates answering the same way named together.
    /// </summary>
    /// <remarks>
    /// No name and no reason string leaves the page — each name is still printed and each distinct
    /// sentence is still said. The fold needs no knowledge of what any slot implies, so it can
    /// never hide a verdict: two names share a line only when their verdict, class and sentence are
    /// the same three facts.
    /// </remarks>
    internal static bool TryPredicates(
        JObject item,
        string name,
        string indent,
        List<string> lines,
        Said said)
    {
        // An unwrapped block of one is the verdict itself, not a block of slots, and the page
        // already says that on one line.
        if (item.Count == 0) return false;
        foreach (var property in item.Properties())
        {
            if (property.Value is not JObject) return false;
        }
        var slots = new List<JProperty>(item.Count);
        foreach (var property in item.Properties()) slots.Add(property);
        var verdicts = new (bool Available, string? Code, string? Reason)?[slots.Count];
        var affirmative = true;
        for (var index = 0; index < slots.Count; index++)
        {
            if (!IsPredicateVerdict(
                    slots[index].Value, out var available, out var code, out var reason))
            {
                affirmative = false;
                continue;
            }
            verdicts[index] = (available, code, reason);
            affirmative &= available && reason is null;
        }
        if (affirmative)
        {
            var named = new StringBuilder(slots[0].Name);
            for (var index = 1; index < slots.Count; index++)
                named.Append(", ").Append(slots[index].Name);
            lines.Add(indent + name + ": " + named + " — yes");
            return true;
        }

        lines.Add(indent + name + ":");
        var taken = new bool[slots.Count];
        for (var index = 0; index < slots.Count; index++)
        {
            if (taken[index]) continue;
            if (verdicts[index] is not { } verdict)
            {
                // A slot carrying more than a verdict says it in the page's own grammar.
                WriteProperty(
                    slots[index].Name,
                    slots[index].Value,
                    null,
                    null,
                    null,
                    indent + Indent,
                    lines,
                    said);
                continue;
            }
            var together = new StringBuilder(slots[index].Name);
            for (var other = index + 1; other < slots.Count; other++)
            {
                if (taken[other] || verdicts[other] is not { } twin ||
                    !SameVerdict(verdict, twin))
                {
                    continue;
                }
                taken[other] = true;
                together.Append(", ").Append(slots[other].Name);
            }
            together.Append(": ").Append(verdict.Available
                ? "yes"
                : verdict.Code is { } code ? "no (" + code + ")" : "no");
            together.Append(Sentence(said, verdict.Code is not null, verdict.Reason));
            lines.Add(indent + Indent + together);
        }
        return true;
    }

    /// <summary>
    /// Whether two predicates say the same three things. A yes prints no class, so two of them
    /// share a line on their sentence alone.
    /// </summary>
    internal static bool SameVerdict(
        (bool Available, string? Code, string? Reason) left,
        (bool Available, string? Code, string? Reason) right) =>
        left.Available == right.Available &&
        string.Equals(left.Reason, right.Reason, StringComparison.Ordinal) &&
        (left.Available || string.Equals(left.Code, right.Code, StringComparison.Ordinal));

    /// <summary>
    /// The whole of a no, on one line: which kind it is, and the sentence that says the rest. The
    /// same shape carries a yes, because a caller reading down a page should not have to switch
    /// between two grammars to learn whether it may act.
    /// </summary>
    internal static string? Verdict(JObject item, Said? said)
    {
        var reason = (string?)item["reason"];
        var code = (string?)item["reasonCode"];
        var status = (string?)item["status"];
        if (status is null || reason is null) return null;
        if (item["rows"] is not null || item["results"] is not null) return null;
        var line = new StringBuilder(status);
        if (code is not null) line.Append(" (").Append(code).Append(')');
        return line.Append(Sentence(said, code is not null, reason)).ToString();
    }
}
#endif

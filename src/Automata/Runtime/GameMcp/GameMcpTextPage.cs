#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

namespace OrbAutomata.GameMcp;

/// <summary>
/// Renders one finished response document as the page a caller reads.
/// </summary>
/// <remarks>
/// <para>
/// This surface is read, not piped. Nobody runs its answers through a JSON parser and picks fields
/// out by path; an agent reads the page the way a player reads a screen, and every brace, quote and
/// repeated key it had to read past was cost with no reader. So the wire says
/// <c>Spellpower: 43/45</c> where it used to say an object with three fields, and
/// <c>suite_health</c> — already plain text, one fact per line — is the shape the rest of the
/// surface now matches.
/// </para>
/// <para>
/// The renderer is the last stage and owns layout alone. Producers keep building documents, so what
/// a response <em>means</em> stays where the game facts are; how it <em>looks</em> is decided once,
/// here, and every tool inherits the same idiom for free. That also keeps the page honest about
/// repetition: a column with one value across a whole page is said once in the header rather than
/// once per row, which is arithmetic no producer can do because it sees one row at a time.
/// </para>
/// </remarks>
internal static class GameMcpTextPage
{
    private const string Indent = "  ";

    /// <summary>Past this, a one-line summary stops being easier to read than a block.</summary>
    private const int InlineBudget = 110;

    internal static string Render(JToken document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        var lines = new List<string>();
        switch (document)
        {
            case JObject page:
                WriteObject(page, string.Empty, lines);
                break;
            case JArray page:
                WriteArray("rows", page, null, string.Empty, lines);
                break;
            default:
                lines.Add(Scalar(document));
                break;
        }
        return lines.Count == 0 ? "(empty)" : string.Join("\n", lines);
    }

    private static void WriteObject(JObject item, string indent, List<string> lines)
    {
        item = Unwrap(item);
        var verdict = Verdict(item);
        if (verdict is not null) lines.Add(indent + verdict);

        // Pagination describes the page, not any row in it, so the table header says it and these
        // never become lines of their own — but only where there is a table to say it on.
        var paged = verdict is null && HasTable(item);
        foreach (var property in item.Properties())
        {
            if (verdict is not null && property.Name is "status" or "reasonCode" or "reason")
                continue;
            // A page that exists is a page that was available. Only a verdict with something to say
            // gets a line of its own.
            if (verdict is null && property.Name == "status" &&
                (string?)property.Value is "available" or "committed")
            {
                continue;
            }
            if (paged && property.Name is "total" or "nextOffset") continue;
            WriteProperty(
                property.Name,
                property.Value,
                paged ? item["total"] : null,
                paged ? item["nextOffset"] : null,
                indent,
                lines);
        }
    }

    /// <summary>
    /// <c>{"row": {...}}</c> and its kin are envelopes, not facts: one object wrapped in one key
    /// that names nothing the caller asked about. The page says what is inside.
    /// </summary>
    private static JObject Unwrap(JObject item)
    {
        while (item.Count == 1)
        {
            var only = item.First as JProperty;
            if (only?.Value is not JObject inner) return item;
            item = inner;
        }
        return item;
    }

    private static bool HasTable(JObject item)
    {
        foreach (var property in item.Properties())
            if (property.Value is JArray { Count: > 0 } array && array[0] is JObject) return true;
        return false;
    }

    private static void WriteProperty(
        string name,
        JToken value,
        JToken? total,
        JToken? nextOffset,
        string indent,
        List<string> lines)
    {
        switch (value)
        {
            case null:
            case JValue { Type: JTokenType.Null }:
                return;
            case JArray array:
                WriteArray(name, array, TableSuffix(array, total, nextOffset), indent, lines);
                return;
            case JObject nested:
            {
                nested = Unwrap(nested);
                var inline = TryInline(nested);
                if (inline is not null)
                {
                    lines.Add(indent + name + ": " + inline);
                    return;
                }
                lines.Add(indent + name + ":");
                WriteObject(nested, indent + Indent, lines);
                return;
            }
            default:
                lines.Add(indent + name + ": " + Scalar(value));
                return;
        }
    }

    /// <summary>
    /// The whole of a no, on one line: which kind it is, and the sentence that says the rest. The
    /// same shape carries a yes, because a caller reading down a page should not have to switch
    /// between two grammars to learn whether it may act.
    /// </summary>
    private static string? Verdict(JObject item)
    {
        var reason = (string?)item["reason"];
        var code = (string?)item["reasonCode"];
        var status = (string?)item["status"];
        if (status is null || reason is null) return null;
        if (item["rows"] is not null || item["results"] is not null) return null;
        var line = new StringBuilder(status);
        if (code is not null) line.Append(" (").Append(code).Append(')');
        return line.Append(": ").Append(reason).ToString();
    }

    private static string? TableSuffix(JArray array, JToken? total, JToken? nextOffset)
    {
        if (total is null && nextOffset is null) return null;
        var suffix = new StringBuilder();
        suffix.Append(array.Count.ToString(CultureInfo.InvariantCulture));
        if (total is not null) suffix.Append('/').Append(Scalar(total));
        if (nextOffset is not null) suffix.Append(" next=").Append(Scalar(nextOffset));
        return suffix.ToString();
    }

    private static void WriteArray(
        string name,
        JArray array,
        string? countSuffix,
        string indent,
        List<string> lines)
    {
        if (array.Count == 0)
        {
            lines.Add(indent + name + ": none");
            return;
        }
        var scalars = AllScalars(array);
        if (scalars is not null)
        {
            lines.Add(indent + name + ": " + scalars);
            return;
        }
        if (TryIdentityList(array, out var identities))
        {
            lines.Add(indent + name + ": " + identities);
            return;
        }
        if (TryTable(array, out var columns, out var constants, out var cells))
        {
            var spaced = false;
            for (var index = 0; !spaced && index < cells.Count; index++)
                for (var column = 0; !spaced && column < cells[index].Length; column++)
                    spaced = cells[index][column].IndexOf(' ') >= 0;
            var separator = spaced ? " | " : " ";

            var header = new StringBuilder(indent).Append(name).Append(' ')
                .Append(countSuffix ?? array.Count.ToString(CultureInfo.InvariantCulture));
            if (constants.Count > 0)
            {
                header.Append("; all ");
                for (var index = 0; index < constants.Count; index++)
                {
                    if (index > 0) header.Append(", ");
                    header.Append(constants[index].Key).Append('=')
                        .Append(HeaderCell(constants[index].Value));
                }
            }
            header.Append("  [").Append(string.Join(separator, ColumnLabels(columns))).Append(']');
            lines.Add(header.ToString());
            for (var index = 0; index < cells.Count; index++)
                lines.Add(indent + string.Join(separator, cells[index]));
            return;
        }

        lines.Add(indent + name + " " +
            (countSuffix ?? array.Count.ToString(CultureInfo.InvariantCulture)) + ":");
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JObject item) WriteObject(item, indent + Indent, lines);
            else lines.Add(indent + Indent + Scalar(array[index]));
        }
    }

    private static string[] ColumnLabels(List<string> columns)
    {
        var result = new string[columns.Count];
        for (var index = 0; index < columns.Count; index++)
            result[index] = columns[index] == "uuid" ? "id" : columns[index];
        return result;
    }

    /// <summary>
    /// A page's rows share a shape, so they are a table: the keys are said once and each row is one
    /// line. Any column holding one value across the whole page moves into the header, because a
    /// value repeated on every line is a page-level fact wearing a row's clothes.
    /// </summary>
    /// <remarks>
    /// Only the columns that vary have to fit in a cell. A constant one is said once in the header,
    /// so its size stops being a per-row cost — and a page whose every row carried the same refusal
    /// block was rendered as twenty paragraphs precisely because that block was too big for a cell
    /// it was never going to occupy.
    /// </remarks>
    private static bool TryTable(
        JArray array,
        out List<string> columns,
        out List<KeyValuePair<string, JToken>> constants,
        out List<string[]> cells)
    {
        columns = new List<string>();
        constants = new List<KeyValuePair<string, JToken>>();
        cells = new List<string[]>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JObject row) return false;
            foreach (var property in row.Properties())
                if (seen.Add(property.Name)) columns.Add(property.Name);
        }
        if (columns.Count == 0) return false;

        if (array.Count > 1)
        {
            var varying = new List<string>(columns.Count);
            for (var index = 0; index < columns.Count; index++)
            {
                var column = columns[index];
                var first = ((JObject)array[0])[column];
                var constant = first is not null;
                for (var row = 1; constant && row < array.Count; row++)
                    constant = JToken.DeepEquals(first, ((JObject)array[row])[column]);
                // Hoisting is only worth it when the header can say the value. A constant the
                // header cannot render came out as a bare property count — `all foo=3` — and
                // because constants are not printed per row, the value then appeared nowhere on
                // the page at all. Left as a column it keeps its content, whatever shape it takes.
                if (constant && HeaderCanSay(first!))
                    constants.Add(new KeyValuePair<string, JToken>(column, first!));
                else varying.Add(column);
            }
            if (varying.Count == 0) constants.Clear();
            else columns = varying;
        }

        for (var index = 0; index < array.Count; index++)
        {
            var row = (JObject)array[index];
            for (var column = 0; column < columns.Count; column++)
            {
                var cell = row[columns[column]];
                if (cell is not null && !Flat(cell)) return false;
            }
        }

        for (var index = 0; index < array.Count; index++)
        {
            var row = (JObject)array[index];
            var line = new string[columns.Count];
            for (var column = 0; column < columns.Count; column++)
            {
                var cell = row[columns[column]];
                line[column] = cell is null || cell.Type == JTokenType.Null ? "-" : Cell(cell);
            }
            cells.Add(line);
        }
        return true;
    }

    /// <summary>
    /// Whether <see cref="HeaderCell"/> would say this value rather than count it. Both fallbacks
    /// it can reach — a nested object's property count and a non-scalar array's element count —
    /// print an integer that means nothing a caller asked about.
    /// </summary>
    private static bool HeaderCanSay(JToken value) => value switch
    {
        JObject item =>
            ((string?)item["status"] is not null && (string?)item["reason"] is not null) ||
            TryInline(item, int.MaxValue) is not null,
        JArray array =>
            array.Count == 0 || AllScalars(array) is not null || TryIdentityList(array, out _),
        _ => true,
    };

    /// <summary>A value a table cell can hold without the row needing a second line.</summary>
    private static bool Flat(JToken value)
    {
        switch (value)
        {
            case JArray array:
                return AllScalars(array) is not null || TryIdentityList(array, out _);
            case JObject item:
                return TryInline(item) is not null;
            default:
                return true;
        }
    }

    /// <summary>
    /// A constant said once for the whole page, so nothing about it is budgeted against a row it
    /// does not occupy: the sentence a refusal repeated on every line is what the header exists to
    /// carry.
    /// </summary>
    private static string HeaderCell(JToken value)
    {
        if (value is not JObject item) return Cell(value);
        var status = (string?)item["status"];
        var reason = (string?)item["reason"];
        if (status is null || reason is null) return TryInline(item, int.MaxValue) ?? Cell(value);

        // The same grammar a verdict has anywhere else: which kind of answer it is, the fields a
        // caller acts on, then the sentence — which ends in a full stop, so nothing may follow it.
        var line = new StringBuilder(status);
        if ((string?)item["reasonCode"] is { } code) line.Append(" (").Append(code).Append(')');
        foreach (var property in item.Properties())
        {
            if (property.Name is "status" or "reasonCode" or "reason") continue;
            if (property.Value.Type == JTokenType.Null) continue;
            line.Append(' ').Append(property.Name).Append('=').Append(Cell(property.Value));
        }
        return line.Append(": ").Append(reason).ToString();
    }

    private static string Cell(JToken value)
    {
        switch (value)
        {
            case JArray array:
            {
                if (array.Count == 0) return "none";
                var scalars = AllScalars(array);
                if (scalars is not null) return scalars;
                if (TryIdentityList(array, out var identities)) return identities;
                return array.Count.ToString(CultureInfo.InvariantCulture);
            }
            case JObject item:
                return TryInline(item) ?? item.Count.ToString(CultureInfo.InvariantCulture);
            default:
                return Scalar(value);
        }
    }

    /// <summary>
    /// The one-line form of a nested object, or nothing when it earns a block of its own. A current
    /// value beside its ceiling is the shape the game's own screens use, so it is said the way they
    /// say it, and one level of nesting is the limit: two levels of <c>key=value</c> inside one line
    /// stop being readable exactly where a caller most needs to read them.
    /// </summary>
    private static string? TryInline(JObject item) => TryInline(item, InlineBudget);

    private static string? TryInline(JObject item, int budget)
    {
        if (item.Count == 0) return "none";
        if (IsIdentity(item)) return Identity(item);
        if (item["current"] is { } current && item["maximum"] is { } maximum && item.Count == 2)
            return Scalar(current) + "/" + Scalar(maximum);
        if (item["before"] is { } before && item["after"] is { } after && item.Count == 2)
            return Scalar(before) + " -> " + Scalar(after);

        // A requirement node with nothing under it is the answer, not the frame around one: naming
        // the operator, the tier and the owner of an empty tree said everything except that.
        if (item["operator"] is not null && item["children"] is JArray { Count: 0 })
            return "no conditions";

        var verdict = Decision(item);
        var parts = new List<string>(item.Count);
        foreach (var property in item.Properties())
        {
            if (property.Value.Type == JTokenType.Null) continue;
            if (verdict is not null &&
                property.Name is "available" or "reasonCode" or "reason")
            {
                continue;
            }
            switch (property.Value)
            {
                case JObject nested when IsIdentity(nested):
                    parts.Add(property.Name + "=" + Identity(nested));
                    break;
                case JObject nested when nested.Count == 2 &&
                    nested["current"] is { } inner && nested["maximum"] is { } ceiling:
                    parts.Add(property.Name + " " + Scalar(inner) + "/" + Scalar(ceiling));
                    break;
                case JObject:
                    return null;
                case JArray array when AllScalars(array) is { } scalars:
                    parts.Add(property.Name + "=" +
                        (array.Count == 0 ? "none" : "[" + scalars + "]"));
                    break;
                case JArray array when TryIdentityList(array, out var identities):
                    parts.Add(property.Name + "=[" + identities + "]");
                    break;
                case JArray:
                    return null;
                default:
                    parts.Add(property.Name + "=" + Scalar(property.Value));
                    break;
            }
        }
        var body = string.Join(", ", parts);
        if (verdict is not null)
        {
            // Verdict, then the numbers a caller acts on, then the sentence — which ends in a full
            // stop, so nothing may follow it.
            var line = new StringBuilder(verdict);
            if (parts.Count > 0) line.Append(' ').Append(body);
            if ((string?)item["reason"] is { } sentence)
                line.Append(": ").Append(sentence);
            body = line.ToString();
        }
        else if (parts.Count == 0) body = "none";
        return body.Length > budget ? null : body;
    }

    /// <summary>
    /// A decision block's own sentence, so a caller reads whether it may act and why in that order
    /// rather than hunting a boolean and a code that live three keys apart.
    /// </summary>
    private static string? Decision(JObject item)
    {
        if (item["available"] is not JValue { Type: JTokenType.Boolean } available) return null;
        if ((bool)available) return "yes";
        return (string?)item["reasonCode"] is { } code ? "no (" + code + ")" : "no";
    }

    private static bool IsIdentity(JToken value) =>
        value is JObject item &&
        item["uuid"] is JValue { Type: JTokenType.String } &&
        (item.Count == 1 || (item.Count == 2 && item["name"] is JValue { Type: JTokenType.String }));

    private static string Identity(JObject item)
    {
        var uuid = (string?)item["uuid"] ?? string.Empty;
        var name = (string?)item["name"];
        return string.IsNullOrEmpty(name) ? uuid : name + " " + uuid;
    }

    private static bool TryIdentityList(JArray array, out string rendered)
    {
        rendered = string.Empty;
        if (array.Count == 0) return false;
        for (var index = 0; index < array.Count; index++)
            if (!IsIdentity(array[index])) return false;
        var parts = new string[array.Count];
        for (var index = 0; index < array.Count; index++)
            parts[index] = Identity((JObject)array[index]);
        rendered = string.Join(", ", parts);
        return true;
    }

    private static string? AllScalars(JArray array)
    {
        var parts = new List<string>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is JObject or JArray) return null;
            parts.Add(Scalar(array[index]));
        }
        return string.Join(", ", parts);
    }

    private static string Scalar(JToken value)
    {
        switch (value.Type)
        {
            case JTokenType.Boolean:
                return (bool)value ? "yes" : "no";
            case JTokenType.Null:
                return "-";
            case JTokenType.Float:
                return Convert.ToDouble(((JValue)value).Value, CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture);
            default:
                return value.ToString(Newtonsoft.Json.Formatting.None).Trim('"');
        }
    }
}
#endif

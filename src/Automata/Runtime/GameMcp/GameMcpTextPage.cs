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
/// repetition: a column with one value across a whole page is named once on a share line rather than
/// left for the reader to notice, which is arithmetic no producer can do because it sees one row at
/// a time.
/// </para>
/// <para>
/// A table is three header lines and then rows: the count, an optional share line, and the bracketed
/// column set. The column set is a fact about the category, so it is complete and in declaration
/// order on every page — a page that happened to hold one value in a column still names the column,
/// and the share line only says what that value is. Compression that removed a column made the
/// header a fact about the page's rows instead, and a paged scan whose third column changed meaning
/// between pages costs a reader more than every byte it saved.
/// </para>
/// </remarks>
internal static class GameMcpTextPage
{
    private const string Indent = "  ";

    /// <summary>Past this, a one-line summary stops being easier to read than a block.</summary>
    private const int InlineBudget = 110;

    /// <summary>
    /// One dialect for every table on the surface. A bare space is two columns or one name with a
    /// space in it and nothing in the row says which, so the page never offers the reader that
    /// choice — the two characters buy back a whole class of misread row.
    /// </summary>
    private const string Delimiter = " | ";

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
                WriteArray("rows", page, null, null, string.Empty, lines);
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
                lines);
        }

        // The status word is dropped because the page beneath it already proves it. Where an action
        // has no observable payload — a loadout swap the game exposes no read for, a menu return —
        // there is no page beneath it, and dropping the one word it had left rendered a press that
        // landed as the literal `(empty)`. A press that worked says so and stops; that is the whole
        // honest answer, and inventing a second fact to sit under it would be the older lie again.
        if (lines.Count == opened && outcome is not null) lines.Add(indent + outcome);
    }

    /// <summary>
    /// The column set the producer declared for this page, in the order it declared it.
    /// </summary>
    /// <remarks>
    /// The renderer sees one page and cannot know what the category promises, so it stops guessing:
    /// a producer that owns a declared column set says so, and the header is that set on every page
    /// — including the page that returned nothing, which is the page whose shape a reader has no
    /// other way to learn.
    /// </remarks>
    private static IReadOnlyList<string>? Declared(JObject item)
    {
        if (item["columns"] is not JArray declared || declared.Count == 0) return null;
        var names = new List<string>(declared.Count);
        for (var index = 0; index < declared.Count; index++)
        {
            if (declared[index] is not JValue { Type: JTokenType.String } name) return null;
            names.Add((string?)name ?? string.Empty);
        }
        return names;
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

    /// <summary>
    /// The array this page's count, next offset and declared columns describe, or nothing where the
    /// object is not a page. An empty row array is a page too: a page that matched nothing still
    /// answers the question it was asked, so it reads as the same table with no rows rather than as
    /// a sentence in a second grammar.
    /// </summary>
    /// <remarks>
    /// A count belongs to the rows it counted. Every array of a paged object used to be handed the
    /// page's own <c>total</c> and <c>nextOffset</c>, so a degraded search — whose rows sit beside a
    /// short list of the categories it could not read — headed that second list
    /// <c>unavailableCategories 1/174 next=30</c>: a total and a resume offset belonging to an
    /// entirely different set, on a list that is complete and has no offsets at all. The rows are
    /// the page; a second array beside them counts itself.
    /// </remarks>
    private static string? TableProperty(JObject item)
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

    private static void WriteProperty(
        string name,
        JToken value,
        JToken? total,
        JToken? nextOffset,
        IReadOnlyList<string>? declared,
        string indent,
        List<string> lines)
    {
        switch (value)
        {
            case null:
            case JValue { Type: JTokenType.Null }:
                return;
            case JArray array:
                WriteArray(
                    name, array, TableSuffix(array, total, nextOffset), declared, indent, lines);
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
        IReadOnlyList<string>? declared,
        string indent,
        List<string> lines)
    {
        if (array.Count == 0)
        {
            // A list that is not a page says its emptiness the way it says any other value: an
            // unequipped loadout's `spells: none` is the whole answer. A page says how many rows
            // the category holds and what its columns are, because "no rows here" and "no such
            // shape" are different facts and a reader who gets the sentence learns neither.
            if (countSuffix is null)
            {
                lines.Add(indent + name + ": none");
                return;
            }
            lines.Add(indent + name + " " + countSuffix);
            if (declared is { Count: > 0 }) lines.Add(indent + Bracket(declared));
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
        if (TryTable(array, declared, out var columns, out var shared, out var cells))
        {
            lines.Add(indent + name + " " +
                (countSuffix ?? array.Count.ToString(CultureInfo.InvariantCulture)));
            var share = ShareLine(array.Count, shared);
            if (share is not null) lines.Add(indent + share);
            lines.Add(indent + Bracket(columns));
            for (var index = 0; index < cells.Count; index++)
                lines.Add(indent + string.Join(Delimiter, cells[index]));
            return;
        }

        lines.Add(indent + name + " " +
            (countSuffix ?? array.Count.ToString(CultureInfo.InvariantCulture)) + ":");

        // Elements too big to be one line each need a boundary between them, or two answers read as
        // one. A detail read's batch is the case that made this loud: two blocks of a dozen lines
        // ran together and nothing said where the first one stopped. One-line elements never had
        // the problem and gain nothing from a gap, so they keep the tighter list.
        for (var index = 0; index < array.Count; index++)
        {
            var before = lines.Count;
            if (array[index] is JObject item) WriteObject(item, indent + Indent, lines);
            else lines.Add(indent + Indent + Scalar(array[index]));
            if (index + 1 < array.Count && lines.Count - before > 1) lines.Add(string.Empty);
        }
    }

    private static string Bracket(IReadOnlyList<string> columns)
    {
        var labels = new string[columns.Count];
        for (var index = 0; index < columns.Count; index++) labels[index] = Label(columns[index]);
        return "[" + string.Join(Delimiter, labels) + "]";
    }

    private static string Label(string column) => column == "uuid" ? "id" : column;

    /// <summary>
    /// What every row on this page says the same way, said once beside the page's own count.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line is page-scoped in its wording, because <c>all</c> beside <c>/229</c> claimed the
    /// whole category from six rows. It adds to the table and never subtracts from it: the columns
    /// it names are still in the header and still in every row, so what it saves is a reader's
    /// attention on a column they now know is uniform, never a fact the page would otherwise carry.
    /// </para>
    /// <para>
    /// Which means it has to be worth reading, so it is emitted only when it is shorter than the
    /// repetition it names — a summary longer than the six rows under it is not compression, and at
    /// the page sizes callers actually ask for that is what the old header had become. Values are
    /// comma-free by construction so a reader can split the line on <c>, </c> and the first
    /// <c>=</c>: free prose belongs in a cell, where nothing is trying to parse around it.
    /// </para>
    /// </remarks>
    private static string? ShareLine(int rowCount, List<KeyValuePair<string, string>> shared)
    {
        if (shared.Count == 0) return null;
        var line = new StringBuilder("these ")
            .Append(rowCount.ToString(CultureInfo.InvariantCulture))
            .Append(" share: ");
        var repeated = 0;
        for (var index = 0; index < shared.Count; index++)
        {
            if (index > 0) line.Append(", ");
            line.Append(shared[index].Key).Append('=').Append(shared[index].Value);
            repeated = checked(repeated + (shared[index].Value.Length * rowCount));
        }
        return line.Length < repeated ? line.ToString() : null;
    }

    /// <summary>
    /// Whether a page-constant value can be said on the share line at all: a comma would invent a
    /// field that is not there, and a key with nothing after it is indistinguishable from a
    /// truncated line, so both stay in their cells where position alone says where they end.
    /// </summary>
    private static bool Shareable(string value) =>
        value.Length > 0 && value.IndexOf(',') < 0 && value.IndexOf('\n') < 0;

    /// <summary>
    /// A page's rows share a shape, so they are a table: the columns are named once and each row is
    /// one line. A column holding one value across the whole page is also named on the share line —
    /// named, not moved, because which columns a page shows is a fact about the category and not
    /// about the rows the caller happened to land on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The old decomposition split the columns into constant and varying and rendered only the
    /// varying ones, which made the header a function of the page's contents: two adjacent reads of
    /// one unchanged category came back three columns wide and four columns wide, and the third
    /// number meant a different thing in each. Worse, the page that dropped the most columns was
    /// the page with the fewest rows, so a caller who narrowed a query got the widest table and a
    /// caller who paged a category got a header that moved under them. Constancy now decides one
    /// thing only: whether the share line mentions the column.
    /// </para>
    /// <para>
    /// Length is the one relaxation left, and it applies to constants alone. A value that is
    /// identical on every row makes every row equally wide, so it costs a reader nothing to scan
    /// past — where a varying value that big would make the table unreadable and costs the page its
    /// table instead.
    /// </para>
    /// </remarks>
    private static bool TryTable(
        JArray array,
        IReadOnlyList<string>? declared,
        out List<string> columns,
        out List<KeyValuePair<string, string>> shared,
        out List<string[]> cells)
    {
        columns = new List<string>();
        shared = new List<KeyValuePair<string, string>>();
        cells = new List<string[]>();
        var present = new HashSet<string>(StringComparer.Ordinal);
        var widest = 0;
        var widestCount = -1;
        for (var index = 0; index < array.Count; index++)
        {
            if (array[index] is not JObject row) return false;
            foreach (var property in row.Properties()) present.Add(property.Name);
            if (row.Count <= widestCount) continue;
            widestCount = row.Count;
            widest = index;
        }
        if (present.Count == 0) return false;
        columns = Order(present, declared, (JObject)array[widest]);

        var uniform = new string?[columns.Count];
        for (var index = 0; index < columns.Count; index++)
        {
            var first = ((JObject)array[0])[columns[index]];
            var constant = first is not null && first.Type != JTokenType.Null;
            for (var row = 1; constant && row < array.Count; row++)
                constant = JToken.DeepEquals(first, ((JObject)array[row])[columns[index]]);
            if (constant)
            {
                if (!SayableInOneCell(first!)) return false;
                uniform[index] = HeaderCell(first!);
                if (Shareable(uniform[index]!))
                    shared.Add(new KeyValuePair<string, string>(Label(columns[index]), uniform[index]!));
                continue;
            }
            for (var row = 0; row < array.Count; row++)
            {
                var cell = ((JObject)array[row])[columns[index]];
                if (cell is not null && !Flat(cell)) return false;
            }
        }

        for (var index = 0; index < array.Count; index++)
        {
            var row = (JObject)array[index];
            var line = new string[columns.Count];
            for (var column = 0; column < columns.Count; column++)
            {
                if (uniform[column] is { } settled)
                {
                    line[column] = settled;
                    continue;
                }
                var cell = row[columns[column]];
                line[column] = cell is null || cell.Type == JTokenType.Null ? "-" : Cell(cell);
            }
            cells.Add(line);
        }
        return true;
    }

    /// <summary>
    /// The order the columns are named in — the producer's declaration where there is one, and
    /// otherwise the order the page's widest row states them in.
    /// </summary>
    /// <remarks>
    /// Taking the union in first-seen order let the first row of a page decide the header, so a
    /// category whose rows do not all carry every column reversed its columns between page one and
    /// page two of one scan. Neither rule here can do that: a declaration does not change with the
    /// page, and the widest row states every column the narrower ones do. Anything left over — a
    /// verdict pair a refused row carries and its neighbours do not — is named after them in a
    /// fixed order rather than in the order the page happened to reach it.
    /// </remarks>
    private static List<string> Order(
        HashSet<string> present,
        IReadOnlyList<string>? declared,
        JObject widest)
    {
        var ordered = new List<string>(present.Count);
        if (declared is not null)
        {
            for (var index = 0; index < declared.Count; index++)
                if (present.Remove(declared[index])) ordered.Add(declared[index]);
        }
        foreach (var property in widest.Properties())
            if (present.Remove(property.Name)) ordered.Add(property.Name);
        var rest = new List<string>(present);
        rest.Sort(StringComparer.Ordinal);
        ordered.AddRange(rest);
        return ordered;
    }

    /// <summary>
    /// Whether <see cref="HeaderCell"/> would say this value rather than count it. Both fallbacks
    /// it can reach — a nested object's property count and a non-scalar array's element count —
    /// print an integer that means nothing a caller asked about.
    /// </summary>
    private static bool SayableInOneCell(JToken value) => value switch
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
    /// A page-constant value, rendered once and then printed identically in every row's cell — so
    /// the inline budget, which exists to stop one row towering over its neighbours, does not apply
    /// to a value that makes all of them the same height.
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
        // A pair whose halves are equal describes a move that did not happen, and an arrow is how
        // this page says one did. `on: no -> no` on a toggle that was already off spends a whole
        // transition saying nothing changed; where the value is the same on both sides the value is
        // the fact, so the page states it once and the arrow is kept for the moves that earn it.
        if (item["before"] is { } before && item["after"] is { } after && item.Count == 2)
        {
            return JToken.DeepEquals(before, after)
                ? Scalar(after)
                : Scalar(before) + " -> " + Scalar(after);
        }

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

    /// <summary>
    /// One value, and one word for having none. A member the game published as an empty string left
    /// a key with nothing after it — a line a reader cannot tell from a truncated one — so absence
    /// reads as the mark the page already uses for it, whichever way the absence arrived.
    /// </summary>
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
                var text = value.ToString(Newtonsoft.Json.Formatting.None).Trim('"');
                return text.Length == 0 ? "-" : text;
        }
    }
}
#endif

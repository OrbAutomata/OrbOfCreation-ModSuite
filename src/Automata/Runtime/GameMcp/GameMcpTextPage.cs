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
/// column set. Between them they name the category's whole declared column set, in declaration
/// order, on every page — a column whose value the share line states is named there and not again in
/// the header, and every other column is named in the header and said on every row. So the page
/// never contradicts itself by declaring a column constant and then printing it once per row, and it
/// never omits a column that varies: those are the two halves of one rule, and the share line's own
/// worth test is what decides which side a column falls on.
/// </para>
/// <para>
/// A canned refusal sentence is said once per decision. The class beside it is what a caller
/// branches on and it rides every occurrence; the sentence explains the class, and a reader who has
/// read it on the row above learns nothing from reading it again. The first occurrence under a key
/// always carries the sentence in full, so two decisions that happen to share a reason code each
/// still say why — one refusal explained and the next left as a bare class was the page keeping a
/// saving the reader paid for.
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

    /// <summary>
    /// The canned sentences this response has already printed, and where each was printed.
    /// </summary>
    /// <remarks>
    /// A rule sentence is worth saying once per decision, not once per response. Keyed on the
    /// sentence alone, a post-reset answer printed the rule under <c>reset</c> and then rendered
    /// <c>reroll</c> as a bare class with nothing saying why — two different decisions, and the
    /// second lost the only line that explained it. The key is the sentence under the place it is
    /// being said, so siblings each keep theirs while a table's rows, which all render under their
    /// array's one place, still say a shared sentence once.
    /// </remarks>
    private sealed class Said
    {
        private const char Separator = '\u001f';

        private readonly HashSet<string> _sentences;
        private string _place;

        internal Said()
            : this(new HashSet<string>(StringComparer.Ordinal), string.Empty)
        {
        }

        private Said(HashSet<string> sentences, string place)
        {
            _sentences = sentences;
            _place = place;
        }

        internal string Enter(string name)
        {
            var outer = _place;
            _place = outer + "/" + name;
            return outer;
        }

        internal void Leave(string outer) => _place = outer;

        internal bool Add(string reason) => _sentences.Add(_place + Separator + reason);

        /// <summary>
        /// A copy for lines that may never be printed, mergeable back once they are.
        /// </summary>
        internal Said Fork() => new(new HashSet<string>(_sentences, StringComparer.Ordinal), _place);

        internal void Merge(Said other) => _sentences.UnionWith(other._sentences);
    }

    /// <summary>
    /// The sentence a verdict ends in, or nothing where this response has already said it here.
    /// </summary>
    /// <remarks>
    /// Only a verdict carrying a class may drop its sentence, because the class is then what still
    /// says which kind of no this is; a bare no with the sentence removed would say nothing at all.
    /// <paramref name="said"/> is null wherever a line is being measured rather than written — the
    /// budget tests that decide whether a block fits a cell must weigh the sentence they would
    /// print, and a measurement that recorded it would silence the first real occurrence.
    /// </remarks>
    private static string Sentence(Said? said, bool classified, string? reason)
    {
        if (reason is null || reason.Length == 0) return string.Empty;
        if (classified && said is not null && !said.Add(reason)) return string.Empty;
        return ": " + reason;
    }

    private static void WriteObject(
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
    /// The whole of a no, on one line: which kind it is, and the sentence that says the rest. The
    /// same shape carries a yes, because a caller reading down a page should not have to switch
    /// between two grammars to learn whether it may act.
    /// </summary>
    private static string? Verdict(JObject item, Said? said)
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

    private static string? TableSuffix(JArray array, JToken? total, JToken? nextOffset)
    {
        if (total is null && nextOffset is null) return null;
        var suffix = new StringBuilder();
        suffix.Append(array.Count.ToString(CultureInfo.InvariantCulture));
        if (total is not null) suffix.Append('/').Append(Scalar(total));
        if (nextOffset is not null) suffix.Append(" next=").Append(Scalar(nextOffset));
        return suffix.ToString();
    }

    /// <summary>
    /// Whether this array is a detail read's blocks rather than a table's rows.
    /// </summary>
    /// <remarks>
    /// One entity page has one shape however many ids the call named. A batch of two rendered two
    /// indented blocks while the same id asked for alone came back as a one-row table — a
    /// <c>[row]</c> header over one comma-joined <c>key=value</c> line — because a single block whose
    /// fields are all flat satisfies every test for a table, and every column of a one-row table is
    /// constant. <c>results</c> is the detail read's own key and nothing else on the surface uses it,
    /// so saying that here is exact: these are documents, and a document is never a row.
    /// </remarks>
    private static bool IsDetailBlocks(string name) =>
        string.Equals(name, "results", StringComparison.Ordinal);

    private static void WriteArray(
        string name,
        JArray array,
        string? countSuffix,
        IReadOnlyList<string>? declared,
        string indent,
        List<string> lines,
        Said said)
    {
        if (array.Count == 0)
        {
            // A list that is not a page says its emptiness the way it says any other absence: an
            // unequipped loadout's `spells: -` is the whole answer. A page says how many rows the
            // category holds and what its columns are, because "no rows here" and "no such shape"
            // are different facts and a reader who gets the sentence learns neither.
            if (countSuffix is null)
            {
                lines.Add(indent + name + ": " + GameMcpListColumns.Absent);
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
        if (TrySoleCost(name, array, out var sole))
        {
            lines.Add(indent + sole);
            return;
        }
        if (!IsDetailBlocks(name) &&
            TryTable(
                array, declared, said,
                out var columns, out var shared, out var majorities, out var cells))
        {
            lines.Add(indent + name + " " +
                (countSuffix ?? array.Count.ToString(CultureInfo.InvariantCulture)));
            if (shared.Count > 0) lines.Add(indent + ShareLine(array.Count, shared));
            for (var index = 0; index < majorities.Count; index++)
                lines.Add(indent + majorities[index]);
            lines.Add(indent + Bracket(columns));
            for (var index = 0; index < cells.Count; index++)
                lines.Add(indent + string.Join(Delimiter, cells[index]));
            return;
        }

        lines.Add(indent + name + " " +
            (countSuffix ?? array.Count.ToString(CultureInfo.InvariantCulture)) + ":");

        var blocks = new List<List<string>>(array.Count);
        for (var index = 0; index < array.Count; index++)
        {
            var block = new List<string>();
            if (array[index] is JObject item) WriteObject(item, indent + Indent, block, said);
            else block.Add(indent + Indent + Scalar(array[index]));
            blocks.Add(block);
        }
        Hoist(blocks, indent + Indent, lines);

        // Elements too big to be one line each need a boundary between them, or two answers read as
        // one. A detail read's batch is the case that made this loud: two blocks of a dozen lines
        // ran together and nothing said where the first one stopped. One-line elements never had
        // the problem and gain nothing from a gap, so they keep the tighter list.
        for (var index = 0; index < blocks.Count; index++)
        {
            lines.AddRange(blocks[index]);
            if (index + 1 < blocks.Count && blocks[index].Count > 1) lines.Add(string.Empty);
        }
    }

    /// <summary>
    /// What every block in this answer says the same way, said once above them and then not said
    /// again inside any of them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same arithmetic a table's share line does, on the shape a detail read answers in. A
    /// live round's three-glyph <c>world_get</c> spent 796 of its 1,736 bytes — 46% of the answer —
    /// printing the same eighteen lines three times, because the hoist only ever existed on the
    /// table path and a batch of entities is not a table. No fact leaves the page: a line that is
    /// identical in every block is stated once, and every block keeps everything that is its own.
    /// </para>
    /// <para>
    /// Only a top-level line of a block is a candidate, and only where it stands alone: a header
    /// with an indented body under it belongs to the block that owns the body, and lifting the
    /// header away from it would leave the body attached to nothing. A block never gives up its
    /// last line either, because a block with nothing left in it is not a block.
    /// </para>
    /// </remarks>
    private static void Hoist(List<List<string>> blocks, string indent, List<string> lines)
    {
        if (blocks.Count < 2) return;
        var candidates = Standalone(blocks[0], indent);
        for (var index = 1; index < blocks.Count && candidates.Count > 0; index++)
        {
            var here = Standalone(blocks[index], indent);
            candidates.RemoveAll(line => !here.Contains(line));
        }
        if (candidates.Count == 0) return;

        // The last line of the shortest block is what decides how much may go: taking every line a
        // block has leaves an answer that counts entities and then shows none of them.
        var floor = int.MaxValue;
        for (var index = 0; index < blocks.Count; index++) floor = Math.Min(floor, blocks[index].Count);
        while (candidates.Count > 0 && candidates.Count >= floor)
            candidates.RemoveAt(candidates.Count - 1);
        if (candidates.Count == 0) return;

        // The shared lines sit under their own heading, one level in, so that a fact belonging to
        // every block cannot be read as the first block's own.
        var header = indent + "these " +
            blocks.Count.ToString(CultureInfo.InvariantCulture) + " share:";
        var cost = header.Length + 1;
        var saved = 0;
        for (var index = 0; index < candidates.Count; index++)
        {
            saved += (blocks.Count - 1) * (candidates[index].Length + 1);
            cost += Indent.Length;
        }
        if (saved <= cost) return;

        lines.Add(header);
        for (var index = 0; index < candidates.Count; index++)
            lines.Add(Indent + candidates[index]);
        for (var index = 0; index < blocks.Count; index++)
        {
            for (var line = 0; line < candidates.Count; line++)
                blocks[index].Remove(candidates[line]);
        }
        if (blocks[0].Count > 1) lines.Add(string.Empty);
    }

    /// <summary>
    /// The lines of one block that are a whole fact on their own: written at the block's own indent,
    /// with nothing indented under them.
    /// </summary>
    private static List<string> Standalone(List<string> block, string indent)
    {
        var alone = new List<string>(block.Count);
        for (var index = 0; index < block.Count; index++)
        {
            if (!IsAt(block[index], indent)) continue;
            if (index + 1 < block.Count && !IsAt(block[index + 1], indent)) continue;
            alone.Add(block[index]);
        }
        return alone;
    }

    /// <summary>Whether this line is written at exactly the given indent, not deeper.</summary>
    private static bool IsAt(string line, string indent) =>
        line.Length > indent.Length &&
        line.StartsWith(indent, StringComparison.Ordinal) &&
        line[indent.Length] != ' ';

    /// <summary>
    /// A price with exactly one resource in it, said on the line that names it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A table is three lines of frame — a count, a header naming four columns, and the row — and a
    /// live round paid that frame 106 times for 106 prices of one resource each: 11,540 bytes to
    /// deliver 4,613. Every word is the one the table used and in the table's order, so a reader
    /// who has read one shape has read the other; only the frame is gone. Two or more resources
    /// keep the table, because then the columns are doing what columns are for.
    /// </para>
    /// <para>
    /// <c>affordable</c> is the one column that does not ride the line unconditionally. On its own
    /// row it is <c>spendableAmount &gt;= cost</c> — 116 of 116 agreements in that round — and the
    /// block around the price states its own <c>affordable</c> besides, so the word is worth a
    /// reader's attention only where it says no, and that is where it is said.
    /// </para>
    /// </remarks>
    private static bool TrySoleCost(string name, JArray array, out string rendered)
    {
        rendered = string.Empty;
        if (!name.EndsWith("osts", StringComparison.Ordinal) || array.Count != 1) return false;
        if (array[0] is not JObject row) return false;
        if (row["cost"] is not { } cost || row["resource"] is not JObject resource) return false;
        if (!IsIdentity(resource)) return false;
        foreach (var property in row.Properties())
        {
            if (property.Name is "cost" or "spendableAmount" or "affordable" or "resource") continue;
            return false;
        }
        var line = new StringBuilder(name.Substring(0, name.Length - 1))
            .Append(": ")
            .Append(Cell(cost, said: null));
        if (row["spendableAmount"] is { } held)
            line.Append(" of ").Append(Cell(held, said: null));
        line.Append(' ').Append(Identity(resource));
        if (row["affordable"] is JValue { Type: JTokenType.Boolean } affordable && !(bool)affordable)
            line.Append(" affordable=").Append(GameMcpListColumns.No);
        rendered = line.ToString();
        return true;
    }

    private static string Bracket(IReadOnlyList<string> columns)
    {
        var labels = new string[columns.Count];
        for (var index = 0; index < columns.Count; index++) labels[index] = Label(columns[index]);
        return "[" + string.Join(Delimiter, labels) + "]";
    }

    private static string Label(string column) => column == "uuid" ? "id" : column;

    /// <summary>
    /// What every row on this page says the same way, said once beside the page's own count — and
    /// then not said again underneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line is page-scoped in its wording, because <c>all</c> beside <c>/229</c> claimed the
    /// whole category from six rows. It is where a page-constant column is named: the header names
    /// the columns the rows carry, this names the rest with the value they carry, and between them
    /// the declared set is complete on every page. A live round read
    /// <c>these 32 share: effect=raw, order=0</c> and then thirty-two rows of <c>| raw | 0</c>
    /// underneath it — the shape contradicting itself in the same breath.
    /// </para>
    /// <para>
    /// Which means it has to be worth reading, so it is said only when it is shorter than what it
    /// takes off the page — a summary longer than the six rows under it is not compression, and at
    /// the page sizes callers actually ask for that is what the old header had become. It also never
    /// takes the last column: a page whose every column is constant keeps its table, because rows
    /// with nothing in them are not rows. Values are comma-free by construction so a reader can
    /// split the line on <c>, </c> and the first <c>=</c>: free prose belongs in a cell, where
    /// nothing is trying to parse around it.
    /// </para>
    /// </remarks>
    private static string ShareLine(int rowCount, List<KeyValuePair<string, string>> shared)
    {
        var line = new StringBuilder("these ")
            .Append(rowCount.ToString(CultureInfo.InvariantCulture))
            .Append(" share: ");
        for (var index = 0; index < shared.Count; index++)
        {
            if (index > 0) line.Append(", ");
            line.Append(shared[index].Key).Append('=').Append(shared[index].Value);
        }
        return line.ToString();
    }

    /// <summary>
    /// What a column reads on the page when nearly every row says the same thing: the reading the
    /// majority share, and then each row that does not, by the handle it is addressed with.
    /// </summary>
    /// <remarks>
    /// The share line only ever fired on a column that was constant to the last row, so a
    /// ninety-eight-row page spent 1,076 bytes printing <c>available</c> ninety-five times and
    /// 566 more printing <c>idle</c> ninety-four times — and the three rows that were not available
    /// had to be found by eye down a wall of the same word. A live round read that wall as a
    /// truncated answer and misread a name out of it. Naming the majority once and then naming the
    /// exceptions puts the rows that differ where a reader is already looking, and every row's
    /// value is still recoverable exactly: it is the majority unless the line says otherwise.
    /// </remarks>
    private static string MajorityLine(
        int majorityCount,
        int rowCount,
        string column,
        string value,
        List<KeyValuePair<string, string>> exceptions)
    {
        var line = new StringBuilder()
            .Append(majorityCount.ToString(CultureInfo.InvariantCulture))
            .Append(" of ")
            .Append(rowCount.ToString(CultureInfo.InvariantCulture))
            .Append(" share: ")
            .Append(column)
            .Append('=')
            .Append(value)
            .Append("; ");
        for (var index = 0; index < exceptions.Count; index++)
        {
            if (index > 0) line.Append(", ");
            line.Append(exceptions[index].Key).Append(' ').Append(exceptions[index].Value);
        }
        return line.ToString();
    }

    /// <summary>
    /// What one row of a table costs on the page: the cells this plan keeps, and the delimiter
    /// between each neighbouring pair.
    /// </summary>
    private static int RowLength(string[] row, List<int> kept)
    {
        if (kept.Count == 0) return 0;
        var length = (kept.Count - 1) * Delimiter.Length;
        for (var index = 0; index < kept.Count; index++) length += row[kept[index]].Length;
        return length;
    }

    /// <summary>What the header costs for the columns this plan keeps.</summary>
    private static int HeaderLength(List<string> columns, List<int> kept)
    {
        if (kept.Count == 0) return 0;
        var length = 2 + ((kept.Count - 1) * Delimiter.Length);
        for (var index = 0; index < kept.Count; index++)
            length += Label(columns[kept[index]]).Length;
        return length;
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
    /// one line. A column holding one value across the whole page is named on the share line
    /// instead, with the value it holds, and then it is not printed again on any row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A page always names its whole declared column set — the share line and the header together,
    /// in declaration order, with the share line first. That is what keeps a paged scan readable
    /// while the rows stop repeating what the line above them already said: a column that leaves the
    /// header leaves it with its value attached, so a reader who sees a narrower header on page two
    /// is told on page one's own share line exactly which column went and what it read. Silently
    /// dropping a column, which is what an earlier decomposition did, said neither.
    /// </para>
    /// <para>
    /// Only the share line's worth test decides it, and it is measured against what the hoist
    /// actually removes rather than against the raw repetition — so a two-row page repeating a
    /// short word keeps its four columns, and six rows repeating <c>already_maxed</c> do not. The
    /// last column is never taken: rows with nothing left in them are not rows.
    /// </para>
    /// <para>
    /// Length is the one relaxation left, and it applies to constants alone. A value that is
    /// identical on every row is said once now, so its size costs the page one line — where a
    /// varying value that big would make the table unreadable and costs the page its table instead.
    /// </para>
    /// </remarks>
    private static bool TryTable(
        JArray array,
        IReadOnlyList<string>? declared,
        Said said,
        out List<string> columns,
        out List<KeyValuePair<string, string>> shared,
        out List<string> majorities,
        out List<string[]> cells)
    {
        columns = new List<string>();
        shared = new List<KeyValuePair<string, string>>();
        majorities = new List<string>();
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

        // Sentences said while a table is only being considered are not said on the page. The set
        // is merged into the response's own once this really is a table and these lines are really
        // going out; a table that turns out not to be one renders as blocks below, which say the
        // same sentences for the first time there.
        var tableSaid = said.Fork();
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
                var outer = tableSaid.Enter(columns[index]);
                uniform[index] = HeaderCell(first!, tableSaid);
                tableSaid.Leave(outer);
                continue;
            }
            for (var row = 0; row < array.Count; row++)
            {
                var cell = ((JObject)array[row])[columns[index]];
                if (cell is not null && !Flat(cell)) return false;
            }
        }

        // Every cell of the whole table, rendered once. The plans below differ only in which of
        // these columns reach the page, so what each one costs is arithmetic over strings that
        // already exist rather than a formula about them — which is what the old worth test was,
        // and it fired on eighteen of one round's twenty-seven tables where it did not pay.
        var full = new List<string[]>(array.Count);
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
                if (cell is null || cell.Type == JTokenType.Null)
                {
                    line[column] = GameMcpListColumns.Absent;
                    continue;
                }
                var outer = tableSaid.Enter(columns[column]);
                line[column] = Cell(cell, tableSaid);
                tableSaid.Leave(outer);
            }
            full.Add(line);
        }

        var taken = new bool[columns.Count];
        var cost = PlanCost(columns, full, taken, shared, majorities);
        TakeConstants(columns, full, uniform, taken, shared, majorities, ref cost);
        TakeMajorities(columns, full, uniform, taken, shared, majorities, ref cost);

        var kept = new List<int>(columns.Count);
        for (var index = 0; index < columns.Count; index++)
            if (!taken[index]) kept.Add(index);

        for (var index = 0; index < full.Count; index++)
        {
            var line = new string[kept.Count];
            for (var column = 0; column < kept.Count; column++) line[column] = full[index][kept[column]];
            cells.Add(line);
        }

        var header = new List<string>(kept.Count);
        for (var index = 0; index < kept.Count; index++) header.Add(columns[kept[index]]);
        columns = header;
        said.Merge(tableSaid);
        return true;
    }

    /// <summary>What this plan puts on the page, in characters.</summary>
    private static int PlanCost(
        List<string> columns,
        List<string[]> full,
        bool[] taken,
        List<KeyValuePair<string, string>> shared,
        List<string> majorities)
    {
        var kept = new List<int>(columns.Count);
        for (var index = 0; index < columns.Count; index++)
            if (!taken[index]) kept.Add(index);
        var cost = HeaderLength(columns, kept);
        for (var index = 0; index < full.Count; index++) cost += RowLength(full[index], kept);
        if (shared.Count > 0) cost += ShareLine(full.Count, shared).Length;
        for (var index = 0; index < majorities.Count; index++) cost += majorities[index].Length;
        return cost;
    }

    /// <summary>
    /// The page-constant columns worth naming on the share line, taken one at a time and kept only
    /// while the page is getting shorter. The last column is never taken: rows with nothing left in
    /// them are not rows.
    /// </summary>
    private static void TakeConstants(
        List<string> columns,
        List<string[]> full,
        string?[] uniform,
        bool[] taken,
        List<KeyValuePair<string, string>> shared,
        List<string> majorities,
        ref int cost)
    {
        // A page whose every column is constant keeps its table: rows with nothing left in them are
        // not rows, and one column of the same word repeated is not a page a share line improves.
        for (var index = 0; index < columns.Count; index++)
            if (uniform[index] is null) break;
            else if (index + 1 == columns.Count) return;

        // Passes until nothing more pays. A column is judged against the page as it stands, and the
        // share line's own opening is paid by whichever column is taken first — so the first column
        // weighed alone can look dearer than it is, and the second pass is where it is weighed
        // beside the line that now exists.
        var moved = true;
        while (moved)
        {
            moved = false;
            for (var index = 0; index < columns.Count; index++)
            {
                if (taken[index]) continue;
                if (uniform[index] is not { } settled || !Shareable(settled)) continue;
                if (Remaining(taken) <= 1) return;
                taken[index] = true;
                var slot = InsertionPoint(columns, shared, index);
                shared.Insert(slot, new KeyValuePair<string, string>(Label(columns[index]), settled));
                var candidate = PlanCost(columns, full, taken, shared, majorities);
                if (candidate < cost)
                {
                    cost = candidate;
                    moved = true;
                    continue;
                }
                taken[index] = false;
                shared.RemoveAt(slot);
            }
        }
    }

    /// <summary>
    /// Where this column belongs on the share line: in the page's own column order, so the share
    /// line and the header together name the declared set in the order it was declared however the
    /// hoists were arrived at.
    /// </summary>
    private static int InsertionPoint(
        List<string> columns,
        List<KeyValuePair<string, string>> shared,
        int column)
    {
        for (var index = 0; index < shared.Count; index++)
        {
            var placed = columns.FindIndex(name => string.Equals(
                Label(name), shared[index].Key, StringComparison.Ordinal));
            if (placed > column) return index;
        }
        return shared.Count;
    }

    /// <summary>
    /// The near-constant columns worth naming once with their exceptions listed, on the same terms:
    /// taken only while the page is getting shorter, and only where every row can be named.
    /// </summary>
    /// <remarks>
    /// A row is named by the handle the page already addresses it with, so a reader who wants the
    /// exception's other columns has the id to find its line. Without that handle there is nothing
    /// to hang an exception on, and the column stays where every row spells it out.
    /// </remarks>
    private static void TakeMajorities(
        List<string> columns,
        List<string[]> full,
        string?[] uniform,
        bool[] taken,
        List<KeyValuePair<string, string>> shared,
        List<string> majorities,
        ref int cost)
    {
        var handle = columns.IndexOf("uuid");
        if (handle < 0 || full.Count < MajorityFloor) return;
        for (var index = 0; index < columns.Count; index++)
        {
            if (index == handle || taken[index] || uniform[index] is not null) continue;
            if (Remaining(taken) <= 1) return;
            if (!TryMajority(full, index, out var value, out var majorityCount)) continue;
            var exceptions = new List<KeyValuePair<string, string>>();
            for (var row = 0; row < full.Count; row++)
            {
                if (string.Equals(full[row][index], value, StringComparison.Ordinal)) continue;
                exceptions.Add(
                    new KeyValuePair<string, string>(full[row][handle], full[row][index]));
            }
            taken[index] = true;
            majorities.Add(MajorityLine(
                majorityCount, full.Count, Label(columns[index]), value, exceptions));
            var candidate = PlanCost(columns, full, taken, shared, majorities);
            if (candidate < cost)
            {
                cost = candidate;
                continue;
            }
            taken[index] = false;
            majorities.RemoveAt(majorities.Count - 1);
        }
    }


    /// <summary>Below this many rows a majority is not a majority, it is a short list.</summary>
    private const int MajorityFloor = 6;

    /// <summary>
    /// The reading most of this column's rows share, where one of them does and every cell in it is
    /// short enough and plain enough to be named on a line of its own.
    /// </summary>
    private static bool TryMajority(
        List<string[]> full,
        int column,
        out string value,
        out int count)
    {
        value = string.Empty;
        count = 0;
        for (var row = 0; row < full.Count; row++)
            if (!Shareable(full[row][column])) return false;
        for (var row = 0; row < full.Count; row++)
        {
            var candidate = full[row][column];
            var seen = 0;
            for (var other = 0; other < full.Count; other++)
                if (string.Equals(full[other][column], candidate, StringComparison.Ordinal)) seen++;
            if (seen <= count) continue;
            count = seen;
            value = candidate;
        }
        return count > 0 && count < full.Count;
    }

    private static int Remaining(bool[] taken)
    {
        var left = 0;
        for (var index = 0; index < taken.Length; index++) if (!taken[index]) left++;
        return left;
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
            TryInline(item, int.MaxValue, said: null) is not null,
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
                return TryInline(item, InlineBudget, said: null) is not null;
            default:
                return true;
        }
    }

    /// <summary>
    /// A page-constant value, rendered once and then printed identically in every row's cell — so
    /// the inline budget, which exists to stop one row towering over its neighbours, does not apply
    /// to a value that makes all of them the same height.
    /// </summary>
    private static string HeaderCell(JToken value, Said? said)
    {
        if (value is not JObject item) return Cell(value, said);
        var status = (string?)item["status"];
        var reason = (string?)item["reason"];
        if (status is null || reason is null)
            return TryInline(item, int.MaxValue, said) ?? Cell(value, said);

        // The same grammar a verdict has anywhere else: which kind of answer it is, the fields a
        // caller acts on, then the sentence — which ends in a full stop, so nothing may follow it.
        var line = new StringBuilder(status);
        var code = (string?)item["reasonCode"];
        if (code is not null) line.Append(" (").Append(code).Append(')');
        foreach (var property in item.Properties())
        {
            if (property.Name is "status" or "reasonCode" or "reason") continue;
            if (property.Value.Type == JTokenType.Null) continue;
            line.Append(' ').Append(property.Name).Append('=').Append(Cell(property.Value, said));
        }
        return line.Append(Sentence(said, code is not null, reason)).ToString();
    }

    private static string Cell(JToken value, Said? said)
    {
        switch (value)
        {
            case JArray array:
            {
                if (array.Count == 0) return GameMcpListColumns.Absent;
                var scalars = AllScalars(array);
                if (scalars is not null) return scalars;
                if (TryIdentityList(array, out var identities)) return identities;
                return array.Count.ToString(CultureInfo.InvariantCulture);
            }
            case JObject item:
                return TryInline(item, InlineBudget, said) ??
                    item.Count.ToString(CultureInfo.InvariantCulture);
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
    private static string? TryInline(JObject item, int budget, Said? said)
    {
        if (item.Count == 0) return GameMcpListColumns.Absent;
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
                        (array.Count == 0
                            ? GameMcpListColumns.Absent
                            : "[" + scalars + "]"));
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
            line.Append(Sentence(
                said, (string?)item["reasonCode"] is not null, (string?)item["reason"]));
            body = line.ToString();
        }
        else if (parts.Count == 0) body = GameMcpListColumns.Absent;
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
    /// One value, and one mark for having none. A member the game published as an empty string left
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
                return GameMcpListColumns.Absent;
            case JTokenType.Float:
                return Convert.ToDouble(((JValue)value).Value, CultureInfo.InvariantCulture)
                    .ToString("R", CultureInfo.InvariantCulture);
            // A string is taken as it is rather than serialized and unquoted. Round-tripping it
            // through JSON put JSON's escapes on a page that is not JSON: a description the game
            // authors with a real paragraph break arrived as the two literal characters `\` and
            // `n`, and the same text through `game_tooltip` — which never took that detour —
            // rendered correctly, which is how the defect was pinned to this line.
            case JTokenType.String:
                var raw = (string?)((JValue)value).Value ?? string.Empty;
                return raw.Length == 0 ? GameMcpListColumns.Absent : raw;
            default:
                var text = value.ToString(Newtonsoft.Json.Formatting.None).Trim('"');
                return text.Length == 0 ? GameMcpListColumns.Absent : text;
        }
    }
}
#endif

#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

using static OrbAutomata.GameMcp.GameMcpTextCells;
using static OrbAutomata.GameMcp.GameMcpTextContext;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTextTable
{

    /// <summary>
    /// The column set the producer declared for this page, in the order it declared it.
    /// </summary>
    /// <remarks>
    /// The renderer sees one page and cannot know what the category promises, so it stops guessing:
    /// a producer that owns a declared column set says so, and the header is that set on every page
    /// — including the page that returned nothing, which is the page whose shape a reader has no
    /// other way to learn.
    /// </remarks>
    internal static IReadOnlyList<string>? Declared(JObject item)
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

    internal static string? TableSuffix(JArray array, JToken? total, JToken? nextOffset)
    {
        if (total is null && nextOffset is null) return null;
        var suffix = new StringBuilder();
        suffix.Append(array.Count.ToString(CultureInfo.InvariantCulture));
        if (total is not null) suffix.Append('/').Append(Scalar(total));
        if (nextOffset is not null) suffix.Append(" next=").Append(Scalar(nextOffset));
        return suffix.ToString();
    }

    internal static string Bracket(IReadOnlyList<string> columns)
    {
        var labels = new string[columns.Count];
        for (var index = 0; index < columns.Count; index++) labels[index] = Label(columns[index]);
        return "[" + string.Join(Delimiter, labels) + "]";
    }

    internal static string Label(string column) => column == "uuid" ? "id" : column;

    /// <summary>
    /// What every row on this page says the same way, said once beside the page's own count — and
    /// then not said again underneath it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The line is page-scoped in its wording, because <c>all</c> beside <c>/229</c> claimed the
    /// whole category from six rows. It is where a page-constant column is named: the header names
    /// the columns the rows carry, this names the rest with the value they carry, and between them
    /// the declared set is complete on every page.
    /// </para>
    /// <para>
    /// Which means it has to be worth reading, so it is said only when it is shorter than what it
    /// takes off the page — a summary longer than the six rows under it is not compression, and at
    /// the page sizes callers actually ask for that is what the old header had become. It also
    /// never takes the last column: a page whose every column is constant keeps its table, because
    /// rows with nothing in them are not rows. Values are comma-free by construction so a reader
    /// can split the line on <c>, </c> and the first <c>=</c>: free prose belongs in a cell, where
    /// nothing is trying to parse around it.
    /// </para>
    /// </remarks>
    internal static string ShareLine(int rowCount, List<KeyValuePair<string, string>> shared)
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
    /// ninety-eight-row page spent 1,076 bytes printing <c>available</c> ninety-five times and 566
    /// more printing <c>idle</c> ninety-four times — and the three rows that were not available had
    /// to be found by eye down a wall of the same word. Naming the majority once and then naming
    /// the exceptions puts the rows that differ where a reader is already looking, and every row's
    /// value is still recoverable exactly: it is the majority unless the line says otherwise.
    /// </remarks>
    internal static string MajorityLine(
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
    internal static int RowLength(string[] row, List<int> kept)
    {
        if (kept.Count == 0) return 0;
        var length = (kept.Count - 1) * Delimiter.Length;
        for (var index = 0; index < kept.Count; index++) length += row[kept[index]].Length;
        return length;
    }

    /// <summary>What the header costs for the columns this plan keeps.</summary>
    internal static int HeaderLength(List<string> columns, List<int> kept)
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
    internal static bool Shareable(string value) =>
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
    internal static bool TryTable(
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

        // Every cell of the whole table, rendered once.
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
    internal static int PlanCost(
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
    internal static void TakeConstants(
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
    internal static int InsertionPoint(
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
    internal static void TakeMajorities(
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
    internal const int MajorityFloor = 6;

    /// <summary>
    /// The reading most of this column's rows share, where one of them does and every cell in it is
    /// short enough and plain enough to be named on a line of its own.
    /// </summary>
    internal static bool TryMajority(
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

    internal static int Remaining(bool[] taken)
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
    internal static List<string> Order(
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
}
#endif

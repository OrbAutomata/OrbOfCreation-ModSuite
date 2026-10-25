#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

using static OrbAutomata.GameMcp.GameMcpTextCells;
using static OrbAutomata.GameMcp.GameMcpTextContext;
using static OrbAutomata.GameMcp.GameMcpTextPage;
using static OrbAutomata.GameMcp.GameMcpTextTable;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTextBlocks
{

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
    internal static bool IsDetailBlocks(string name) =>
        string.Equals(name, "results", StringComparison.Ordinal);

    internal static void WriteArray(
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

        // The same frame, and the same arithmetic, for every other list of one. A count, a header
        // naming the columns, and one row under it is three lines to deliver one — and a round paid
        // that frame nineteen more times, for `members 1`, `sources 1`, `cast 1`, `upkeep 1`,
        // `placements 1` and `children 1`. Inlined, every word the header carried is still there as
        // its own key, so a reader who has read the table has read this. A page keeps its table
        // however few rows it holds: its count and its declared columns are what a paged read is
        // read by, and one row today is not a promise about tomorrow's.
        // A list of one has no line budget. The budget exists so a long nested object becomes a
        // block a reader can scan, but the block a one-row table falls back to is a count, a header
        // and a row — every character of the inline form plus the frame around it — so refusing the
        // long line only ever bought more bytes saying the same thing.
        if (countSuffix is null &&
            declared is null or { Count: 0 } &&
            !IsDetailBlocks(name) &&
            array.Count == 1 &&
            array[0] is JObject only &&
            TryInline(only, int.MaxValue, said) is { } inlined)
        {
            lines.Add(indent + name + ": " + inlined);
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

            // Each block is its own answer, so it says its own causes; a sentence two blocks share
            // is lifted above them by the hoist rather than left off the second.
            var outer = said.Enter(index.ToString(CultureInfo.InvariantCulture));
            if (array[index] is JObject item) WriteObject(item, indent + Indent, block, said);
            else block.Add(indent + Indent + Scalar(array[index]));
            said.Leave(outer);
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
    /// The same arithmetic a table's share line does, on the shape a detail read answers in. No
    /// fact leaves the page: a line that is identical in every block is stated once, and every
    /// block keeps everything that is its own.
    /// </para>
    /// <para>
    /// Only a top-level line of a block is a candidate, and only where it stands alone: a header
    /// with an indented body under it belongs to the block that owns the body, and lifting the
    /// header away from it would leave the body attached to nothing. A block never gives up its
    /// last line either, because a block with nothing left in it is not a block.
    /// </para>
    /// </remarks>
    internal static void Hoist(List<List<string>> blocks, string indent, List<string> lines)
    {
        if (blocks.Count < 2) return;
        var units = new List<List<List<string>>>(blocks.Count);
        for (var index = 0; index < blocks.Count; index++) units.Add(Units(blocks[index], indent));

        var candidates = new List<List<string>>();
        for (var index = 0; index < units[0].Count; index++)
        {
            var unit = units[0][index];
            var everywhere = true;
            for (var block = 1; block < units.Count && everywhere; block++)
                everywhere = Holds(units[block], unit);
            if (everywhere) candidates.Add(unit);
        }
        if (candidates.Count == 0) return;

        // The last fact of the shortest block is what decides how much may go: taking every fact a
        // block has leaves an answer that counts entities and then shows none of them.
        var floor = int.MaxValue;
        for (var index = 0; index < units.Count; index++) floor = Math.Min(floor, units[index].Count);
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
            var unit = candidates[index];
            for (var line = 0; line < unit.Count; line++)
            {
                saved += (blocks.Count - 1) * (unit[line].Length + 1);
                cost += Indent.Length;
            }
        }
        if (saved <= cost) return;

        lines.Add(header);
        for (var index = 0; index < candidates.Count; index++)
        {
            var unit = candidates[index];
            for (var line = 0; line < unit.Count; line++) lines.Add(Indent + unit[line]);
        }
        for (var index = 0; index < blocks.Count; index++)
        {
            for (var unit = 0; unit < candidates.Count; unit++)
                Take(blocks[index], candidates[unit]);
        }
        if (blocks[0].Count > 1) lines.Add(string.Empty);
    }

    /// <summary>
    /// One block's facts: each a line written at the block's own indent, together with whatever is
    /// indented under it.
    /// </summary>
    internal static List<List<string>> Units(List<string> block, string indent)
    {
        var units = new List<List<string>>(block.Count);
        var index = 0;
        while (index < block.Count)
        {
            if (!IsAt(block[index], indent))
            {
                index++;
                continue;
            }
            var unit = new List<string> { block[index] };
            index++;
            while (index < block.Count && !IsAt(block[index], indent))
            {
                unit.Add(block[index]);
                index++;
            }
            units.Add(unit);
        }
        return units;
    }

    /// <summary>Whether this block says the same fact, line for line.</summary>
    internal static bool Holds(List<List<string>> units, List<string> unit)
    {
        for (var index = 0; index < units.Count; index++)
        {
            if (Same(units[index], unit)) return true;
        }
        return false;
    }

    internal static bool Same(List<string> left, List<string> right)
    {
        if (left.Count != right.Count) return false;
        for (var index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal)) return false;
        }
        return true;
    }

    /// <summary>Removes this fact's lines from the block, once.</summary>
    internal static void Take(List<string> block, List<string> unit)
    {
        for (var start = 0; start + unit.Count <= block.Count; start++)
        {
            var here = true;
            for (var index = 0; index < unit.Count && here; index++)
                here = string.Equals(block[start + index], unit[index], StringComparison.Ordinal);
            if (!here) continue;
            block.RemoveRange(start, unit.Count);
            return;
        }
    }

    /// <summary>Whether this line is written at exactly the given indent, not deeper.</summary>
    internal static bool IsAt(string line, string indent) =>
        line.Length > indent.Length &&
        line.StartsWith(indent, StringComparison.Ordinal) &&
        line[indent.Length] != ' ';
}
#endif

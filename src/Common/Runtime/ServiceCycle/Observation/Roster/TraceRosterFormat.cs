using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;

/// <summary>
/// Reads and writes the roster a session stores beside its segments.
/// </summary>
/// <remarks>
/// <para>
/// Text, and for the same reason the publication stores are text: this is a handful of lines written
/// once per session, so a fixed-width codec would buy nothing and cost a reader the ability to answer
/// "what did the numbers mean" with a text editor. One line per entry, in identity order.
/// </para>
/// <para>
/// The encoder and the decoder live together because they are one format. The recording runtime uses
/// the encoder and the analysis tool uses the decoder, and a format whose two halves are maintained in
/// two assemblies is a format that drifts.
/// </para>
/// </remarks>
internal static class TraceRosterFormat
{
    internal const string Magic = "OSCR";
    internal const int Version = 1;
    internal const string FileName = "roster.oscr";

    private const string Separator = " = ";
    private const int MaximumEntries = 1_024;

    internal static byte[] Encode(ServiceCycleTraceRoster roster)
    {
        if (roster is null) throw new ArgumentNullException(nameof(roster));
        var text = new StringBuilder(roster.Count * 48 + 32);
        text.Append(Magic).Append(' ')
            .Append(Version.ToString(CultureInfo.InvariantCulture)).Append(' ')
            .Append(roster.Count.ToString(CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var entry in roster.Entries)
        {
            text.Append(entry.Kind).Append(' ')
                .Append(entry.Identity.ToString(CultureInfo.InvariantCulture)).Append(' ')
                .Append(entry.MachineId)
                .Append(Separator)
                .Append(Sanitize(entry.DisplayName))
                .Append('\n');
        }
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>
    /// The entries in a roster file, or an empty roster when the text does not carry the header this
    /// format writes. A malformed roster costs a reader the names and nothing else, so it degrades to
    /// the same place an absent one does rather than failing a report that is otherwise readable.
    /// </summary>
    internal static ServiceCycleTraceRoster Decode(string text)
    {
        if (string.IsNullOrEmpty(text)) return ServiceCycleTraceRoster.Empty;
        var lines = text.Split('\n');
        var header = lines[0].Split(' ');
        if (header.Length != 3 ||
            !string.Equals(header[0], Magic, StringComparison.Ordinal) ||
            !int.TryParse(header[1], NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version != Version)
            return ServiceCycleTraceRoster.Empty;

        var entries = new List<ServiceCycleTraceRosterEntry>(Math.Min(lines.Length, MaximumEntries));
        for (var index = 1; index < lines.Length && entries.Count < MaximumEntries; index++)
        {
            if (TryParseEntry(lines[index], out var entry)) entries.Add(entry);
        }
        return new ServiceCycleTraceRoster(entries.ToArray());
    }

    private static bool TryParseEntry(string line, out ServiceCycleTraceRosterEntry entry)
    {
        entry = default;
        if (line.Length == 0) return false;
        var separator = line.IndexOf(Separator, StringComparison.Ordinal);
        if (separator < 0) return false;
        // Three fields, and the last one takes whatever is left: a machine identity is a name the
        // runtime chose, and a world-collection category chose one with spaces in it.
        var head = line.Substring(0, separator).Split(' ', 3);
        if (head.Length != 3) return false;
        if (!ulong.TryParse(head[1], NumberStyles.None, CultureInfo.InvariantCulture, out var identity))
            return false;
        if (head[0].Length == 0 || head[2].Length == 0) return false;
        entry = new ServiceCycleTraceRosterEntry(
            head[0],
            identity,
            head[2],
            line.Substring(separator + Separator.Length));
        return true;
    }

    /// <summary>
    /// A display name is a line in a line-oriented file, so it may not carry the two characters that
    /// would end the line or start a second field.
    /// </summary>
    private static string Sanitize(string value)
    {
        if (value.Length == 0) return value;
        var cleaned = value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        return cleaned;
    }
}

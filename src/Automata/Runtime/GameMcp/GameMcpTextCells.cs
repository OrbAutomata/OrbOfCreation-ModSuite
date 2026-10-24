#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;

using static OrbAutomata.GameMcp.GameMcpTextContext;
using static OrbAutomata.GameMcp.GameMcpTextPage;

namespace OrbAutomata.GameMcp;

internal static class GameMcpTextCells
{

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
    internal static bool TrySoleCost(string name, JArray array, out string rendered)
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

    /// <summary>
    /// Whether <see cref="HeaderCell"/> would say this value rather than count it. Both fallbacks
    /// it can reach — a nested object's property count and a non-scalar array's element count —
    /// print an integer that means nothing a caller asked about.
    /// </summary>
    internal static bool SayableInOneCell(JToken value) => value switch
    {
        JObject item =>
            ((string?)item["status"] is not null && (string?)item["reason"] is not null) ||
            TryInline(item, int.MaxValue, said: null) is not null,
        JArray array =>
            array.Count == 0 || AllScalars(array) is not null || TryIdentityList(array, out _),
        _ => true,
    };

    /// <summary>A value a table cell can hold without the row needing a second line.</summary>
    internal static bool Flat(JToken value)
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
    internal static string HeaderCell(JToken value, Said? said)
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

    internal static string Cell(JToken value, Said? said)
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
    internal static string? TryInline(JObject item, int budget, Said? said)
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
                // The move a mutation made, said the way the page says it everywhere else. A row
                // carrying one of these was the whole reason a list of one still paid for a header,
                // a column line and a row: the pair renders in three words and the block around it
                // cost three lines to deliver them.
                case JObject nested when nested.Count == 2 &&
                    nested["before"] is { } was && nested["after"] is { } now:
                    parts.Add(property.Name + "=" + (JToken.DeepEquals(was, now)
                        ? Scalar(now)
                        : Scalar(was) + " -> " + Scalar(now)));
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
    internal static string? Decision(JObject item)
    {
        if (item["available"] is not JValue { Type: JTokenType.Boolean } available) return null;
        if ((bool)available) return "yes";
        return (string?)item["reasonCode"] is { } code ? "no (" + code + ")" : "no";
    }

    internal static bool IsIdentity(JToken value) =>
        value is JObject item &&
        item["uuid"] is JValue { Type: JTokenType.String } &&
        (item.Count == 1 || (item.Count == 2 && item["name"] is JValue { Type: JTokenType.String }));

    internal static string Identity(JObject item)
    {
        var uuid = (string?)item["uuid"] ?? string.Empty;
        var name = (string?)item["name"];
        return string.IsNullOrEmpty(name) ? uuid : name + " " + uuid;
    }

    internal static bool TryIdentityList(JArray array, out string rendered)
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

    internal static string? AllScalars(JArray array)
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
    internal static string Scalar(JToken value)
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

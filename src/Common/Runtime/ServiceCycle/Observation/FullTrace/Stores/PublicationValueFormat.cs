using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Stores;

/// <summary>
/// Renders one immutable publication into the text a recording session stores beside its segments.
/// </summary>
/// <remarks>
/// <para>
/// The semantic stream says which generation of configuration and strategy a cycle decided against;
/// it does not say what those generations contained. A store keyed by generation closes that, and it
/// closes it once per generation however many services acted on it.
/// </para>
/// <para>
/// Text rather than a fixed-width record, and reflected rather than hand-written, because these
/// publications are settings trees that grow with the suite: a hand-written codec would silently stop
/// recording whatever was added to them last. Sorted `path = value` lines mean two generations of the
/// same store diff cleanly, and a reader needs nothing but a text editor. The cost is reflection over
/// a small record on a rare publication, which is the one place the full-trace mandate calls
/// allocation acceptable.
/// </para>
/// </remarks>
internal static class PublicationValueFormat
{
    internal const string Magic = "OSCV";
    internal const int Version = 1;

    private const int MaximumDepth = 6;
    private const int MaximumLines = 4_096;
    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static byte[] Encode(string store, ulong generation, object publication)
    {
        if (string.IsNullOrWhiteSpace(store)) throw new ArgumentException("A store name is required.", nameof(store));
        if (publication is null) throw new ArgumentNullException(nameof(publication));
        var lines = new List<string>(64);
        Walk(lines, string.Empty, publication, depth: 0);
        lines.Sort(StringComparer.Ordinal);

        var text = new StringBuilder(lines.Count * 32 + 64);
        text.Append(Magic).Append(' ').Append(Version.ToString(CultureInfo.InvariantCulture))
            .Append(' ').Append(store)
            .Append(' ').Append(generation.ToString("x16", CultureInfo.InvariantCulture))
            .Append('\n');
        foreach (var line in lines) text.Append(line).Append('\n');
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    private static void Walk(List<string> lines, string path, object? value, int depth)
    {
        if (lines.Count >= MaximumLines) return;
        if (value is null)
        {
            lines.Add(path + " = (none)");
            return;
        }

        var type = value.GetType();
        if (TryFormatLeaf(value, type, out var text))
        {
            lines.Add(path + " = " + text);
            return;
        }
        if (TryWalkTable(lines, path, value, type, depth)) return;
        if (depth == MaximumDepth)
        {
            lines.Add(path + " = (nested too deeply)");
            return;
        }

        // A record carries the state it was built from plus computed readings of that state, and only
        // the state belongs in a store — a computed member is the same fact said twice. A value struct
        // in a publication is the value, so all of it is read; its parts are usually get-only and
        // would otherwise vanish from the store entirely.
        var stateOnly = !type.IsValueType;
        var before = lines.Count;
        foreach (var property in type.GetProperties(Members))
        {
            if (stateOnly && !property.CanWrite) continue;
            if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
            object? member;
            try { member = property.GetValue(value); }
            catch (TargetInvocationException) { continue; }
            Walk(
                lines,
                path.Length == 0 ? property.Name : path + "." + property.Name,
                member,
                depth + 1);
        }
        if (lines.Count == before) lines.Add(path + " = " + (value.ToString() ?? string.Empty));
    }

    private static bool TryWalkTable(List<string> lines, string path, object value, Type type, int depth)
    {
        if (!type.IsGenericType ||
            type.GetGenericTypeDefinition() != typeof(PublicationTable<>))
            return false;

        var count = (int)type.GetProperty("Count", Members)!.GetValue(value)!;
        lines.Add(path + ".Count = " + count.ToString(CultureInfo.InvariantCulture));
        var item = type.GetProperty("Item", Members)!;
        var index = new object[1];
        for (var row = 0; row < count && lines.Count < MaximumLines; row++)
        {
            index[0] = row;
            Walk(
                lines,
                path + "[" + row.ToString("D4", CultureInfo.InvariantCulture) + "]",
                item.GetValue(value, index),
                depth + 1);
        }
        return true;
    }

    private static bool TryFormatLeaf(object value, Type type, out string text)
    {
        if (type.IsEnum)
        {
            text = value.ToString() ?? string.Empty;
            return true;
        }

        switch (value)
        {
            case bool typed: text = typed ? "true" : "false"; return true;
            case string typed: text = typed; return true;
            case Guid typed: text = EntityIdentityFormatter.Format(typed); return true;
            case float typed: text = typed.ToString("R", CultureInfo.InvariantCulture); return true;
            case double typed: text = typed.ToString("R", CultureInfo.InvariantCulture); return true;
            case IFormattable typed when type.IsPrimitive:
                text = typed.ToString(null, CultureInfo.InvariantCulture);
                return true;
            default:
                text = string.Empty;
                return false;
        }
    }
}

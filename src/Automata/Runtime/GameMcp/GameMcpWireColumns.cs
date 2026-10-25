#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Newtonsoft.Json.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

using static OrbAutomata.GameMcp.GameMcpEntityWireNormalizer;
using static OrbAutomata.GameMcp.GameMcpWireIdentity;

namespace OrbAutomata.GameMcp;

internal static class GameMcpWireColumns
{

    /// <summary>
    /// What this pass will call a producer's declared columns, in the order it will leave them in.
    /// </summary>
    /// <remarks>
    /// A page that matched no rows has no rows to read the header off, and a reader needs that
    /// header most on exactly that page — "no rows here" and "no such shape" are different answers
    /// and a bare count says neither. So the producer states its declaration and this says what the
    /// wire will have made of it, from the same rule and the same promotion order the rows go
    /// through. A category whose rendered page disagrees with what this predicts fails the gate
    /// rather than shipping a header naming columns its rows do not carry.
    /// </remarks>
    internal static string[] WireColumns(IReadOnlyList<string> declared)
    {
        if (declared is null) throw new ArgumentNullException(nameof(declared));
        var names = new List<string>(declared.Count + 1);
        for (var index = 0; index < declared.Count; index++) Declare(names, declared[index]);

        // Each of these lands its value under a name it did not have, and a name a row did not
        // already carry is appended — so a renamed column is last, in the order they are applied
        // to the row itself.
        for (var index = 0; index < Renamed.Length; index += 2)
        {
            if (!names.Remove(Renamed[index])) continue;
            Declare(names, Renamed[index + 1]);
        }

        // A reference is republished under its role rather than renamed in place, so it lands after
        // the columns that were already there — and the subject of a row is the one reference that
        // becomes two columns, the handle a caller acts on and the name a player reads.
        var declaredOrder = new List<string>(names);
        for (var index = 0; index < declaredOrder.Count; index++)
        {
            var name = declaredOrder[index];
            if (string.Equals(name, "entityId", StringComparison.Ordinal))
            {
                names.Remove(name);
                Declare(names, "uuid");
                Declare(names, "name");
                continue;
            }
            var role = WireName(name);
            if (string.Equals(role, name, StringComparison.Ordinal)) continue;
            names.Remove(name);
            Declare(names, role);
        }

        if (!names.Contains("uuid")) return names.ToArray();
        for (var index = 0; index < Identity.Length; index++)
        {
            if (!names.Remove(Identity[index])) continue;
            names.Insert(0, Identity[index]);
        }
        return names.ToArray();
    }

    /// <summary>
    /// The renames <see cref="NormalizeObject"/> applies, as from/to pairs in the order it applies
    /// them, so a declaration and a row cannot drift into two vocabularies.
    /// </summary>
    internal static readonly string[] Renamed =
    {
        "unlocked", "available",
        "quantity", "amount",
        "availableAmount", "amount",
        "trueQuantity", "amount",
        "trueRate", "netRatePerSecond",
        "equippedLevel", "equippedCount",
        "equippedStacks", "equippedCount",
        "activeAmount", "activeCount",
        "maximumAdditional", "maximumAmount",
    };

    /// <summary>
    /// One declared path as one column name. A member read out of a nested reading is lifted onto
    /// the row under its own name, so the column is the leaf; any other nested declaration stays
    /// the object it was copied into, so the column is its root.
    /// </summary>
    internal static void Declare(List<string> names, string path)
    {
        var stop = path.IndexOf('.');
        var name = stop < 0
            ? path
            : path.StartsWith("reading.", StringComparison.Ordinal) ||
                path.StartsWith("details.", StringComparison.Ordinal)
                ? path.Substring(path.LastIndexOf('.') + 1)
                : path.Substring(0, stop);
        if (!names.Contains(name)) names.Add(name);
    }

    /// <summary>
    /// The column a reference is published under: its role, not the raw member that held the id. A
    /// row says the resource it costs, not the <c>resourceId</c> it was read from.
    /// </summary>
    internal static string WireName(string name)
    {
        if (string.Equals(name, "uuid", StringComparison.Ordinal)) return name;
        if (name.EndsWith("Uuid", StringComparison.Ordinal))
            return name.Substring(0, name.Length - 4);
        return name.EndsWith("Id", StringComparison.Ordinal)
            ? name.Substring(0, name.Length - 2)
            : name;
    }
}
#endif

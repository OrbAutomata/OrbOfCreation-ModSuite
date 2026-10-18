using System;
using System.Collections;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common;

/// <summary>The bound reads of a native <c>ResourceCostList</c>'s rows.</summary>
internal interface INativeCostRows
{
    Func<object, IList?> CostEntries { get; }
    Func<object, object?> CostResource { get; }
    Func<object, BigDouble> CostValue { get; }
    Func<object, Guid> ResourceGuid { get; }
    Func<object, BigDouble, bool> HasResourceAmount { get; }
}

internal static class NativeCostShortfall
{
    /// <summary>
    /// The refusal for a price the game says is not held, naming the first resource it is short
    /// of, in the game's own row order.
    /// </summary>
    internal static NativeActionOutcome Refuse(
        INativeCostRows native,
        object cost,
        string label,
        ServiceActionResultCode unaffordable,
        ServiceActionResultCode contractUnavailable)
    {
        var rows = native.CostEntries(cost);
        if (rows is null)
            return NativeActionOutcome.Reject(contractUnavailable,
                "The " + label + " price has no readable resource rows.");
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            if (row is null) continue;
            var resource = native.CostResource(row);
            if (resource is null) continue;
            if (!native.HasResourceAmount(resource, native.CostValue(row)))
                return NativeActionOutcome.Reject(unaffordable,
                    EntityIdentityFormatter.PlayerName(native.ResourceGuid(resource)) +
                    " is short for this " + label + ".");
        }
        return NativeActionOutcome.Reject(contractUnavailable,
            "The game refused this " + label + " price without identifying a short resource.");
    }
}

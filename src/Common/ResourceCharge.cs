using System;

namespace OrbModding.Common;

/// <summary>One resource line of the price the game named at a press, in its nominal unit.</summary>
internal readonly struct ResourceCharge
{
    internal ResourceCharge(Guid resourceId, BigDouble amount)
    {
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

internal static class ResourceCharges
{
    /// <summary>Whether every line of a press's price asked for nothing.</summary>
    internal static bool Nothing(ReadOnlySpan<ResourceCharge> charges)
    {
        for (var index = 0; index < charges.Length; index++)
            if (charges[index].Amount > BigDouble.Zero) return false;
        return true;
    }
}

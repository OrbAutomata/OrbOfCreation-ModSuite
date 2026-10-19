using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>One exact native price beside the holding that would pay it now.</summary>
internal readonly struct WorldResourceCost
{
    internal WorldResourceCost(Guid resourceId, BigDouble price, BigDouble held)
    {
        ResourceId = resourceId;
        Price = price;
        Held = held;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Price { get; }
    internal BigDouble Held { get; }
    internal bool Affordable => Held.CompareTo(Price) >= 0;
}

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace OrbModding.Common.Runtime.World;

/// <summary>Reference identity for native objects while a main-thread capture is in progress.</summary>
internal sealed class WorldObjectReferenceComparer : IEqualityComparer<object>
{
    internal static readonly IEqualityComparer<object> Instance = new WorldObjectReferenceComparer();
    public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
    public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
}

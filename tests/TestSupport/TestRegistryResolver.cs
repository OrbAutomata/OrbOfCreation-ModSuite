using System;
using System.Collections;
using OrbModding.Common;

namespace OrbModding.TestSupport;

/// <summary>Builds a boundary over the fixture's registry without changing its native authority.</summary>
internal static class TestRegistryResolver
{
    internal static TypedRegistryResolver For(
        Func<long> epoch,
        IDictionary registry,
        Func<object, Guid>? identity = null) =>
        For(epoch, () => TypedRegistrySourceSnapshot.Ready(registry), identity);

    internal static TypedRegistryResolver For(
        Func<long> epoch,
        Func<TypedRegistrySourceSnapshot> registry,
        Func<object, Guid>? identity = null) =>
        new(epoch, registry, identity ?? ReadIdentity);

    private static Guid ReadIdentity(object value) =>
        value is IdScriptableObject item ? item.GetGuid() : Guid.Empty;
}

using System;

namespace OrbModding.Common.Runtime;

/// <summary>
/// Names the element a tick fault came from: display name and UUID through the suite's one identity
/// binding and formatter, plus the exact native type. A managed view is named by the view it shows.
/// </summary>
internal static class GameTickFaultSubject
{
    internal static string Describe(object subject) =>
        Describe(subject, RuntimeIdentityRegistryBinding.Shared);

    internal static string Describe(object subject, RuntimeIdentityRegistryBinding identity)
    {
        if (subject is null) throw new ArgumentNullException(nameof(subject));
        if (identity is null) throw new ArgumentNullException(nameof(identity));
        object holder = subject is ManagedView { viewReference: { } view } ? view : subject;
        var nativeType = subject.GetType().FullName ?? subject.GetType().Name;
        var uuid = TryReadUuid(holder, identity);
        if (uuid is { } id) return EntityIdentityFormatter.Format(id) + " [" + nativeType + "]";
        return subject is UnityEngine.Object unityObject && unityObject.name is { Length: > 0 } name
            ? "'" + name + "' [" + nativeType + "]"
            : "[" + nativeType + "]";
    }

    private static Guid? TryReadUuid(object holder, RuntimeIdentityRegistryBinding identity)
    {
        try { return identity.ReadStableUuid(holder); }
        catch (Exception) { return null; }
    }
}

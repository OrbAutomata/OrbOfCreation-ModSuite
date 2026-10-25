using System;
using System.Runtime.InteropServices;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal static class ServiceCycleBoundaryRules
{
    internal static bool IsExternalRuntimeBoundary(Type type)
    {
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        if (assembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass" or "BepInEx" or "0Harmony" ||
            assembly.StartsWith("UnityEngine", StringComparison.Ordinal))
        {
            return true;
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        return typeNamespace.StartsWith("UnityEngine", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("BepInEx", StringComparison.Ordinal) ||
            typeNamespace.StartsWith("HarmonyLib", StringComparison.Ordinal);
    }

    /// <summary>
    /// The curated allowlist of externally-defined value types the suite has audited and
    /// deliberately transports by value across the service-cycle boundary, overriding the blunt
    /// assembly-name rejection in <see cref="IsExternalRuntimeBoundary"/>.
    /// </summary>
    /// <remarks>
    /// Today this is only the game's <c>BigDouble</c> (BreakInfinity): an immutable
    /// <c>mantissa × 10^exponent</c> numeric value type that ships inside the game assemblies but is
    /// pure value math — no Unity object, native handle, or mutable runtime surface. Auto Buy carries
    /// captured economic magnitudes as <c>BigDouble</c> precisely so no per-frame conversion or copy
    /// into a hand-rolled mirror is needed; reimplementing it would be both wasteful and a fidelity
    /// risk against the game's own arithmetic. The allowlist is intentionally narrow — exact simple
    /// name, value type, and a known game assembly — so no other external type is silently admitted.
    /// </remarks>
    internal static bool IsAuditedExternalValueType(Type type)
    {
        if (!type.IsValueType || type.FullName != "BigDouble") return false;
        var assembly = type.Assembly.GetName().Name ?? string.Empty;
        return assembly is "Assembly-CSharp" or "Assembly-CSharp-firstpass";
    }

    /// <summary>
    /// Whether a type is an audited container admitted inside publication and worker graphs.
    /// Publications otherwise reject arrays and collections outright; a marked container earns its
    /// exception by copying its contents at construction and exposing no array, collection, or
    /// mutable view.
    /// </summary>
    /// <remarks>
    /// The badge alone is the admission. Which types may wear it is a review decision, pinned by
    /// the exact-set allowlist in <c>ServiceCycleAuditedTypeAllowlistTests</c> — a new bearer fails
    /// a test that names it. Open generic definitions are excluded so only closed constructions,
    /// whose arguments can actually be walked, qualify.
    /// </remarks>
    internal static bool IsAuditedPublicationValue(Type type) =>
        !type.ContainsGenericParameters &&
        type.IsDefined(typeof(ServiceCyclePublicationValueAttribute), inherit: false);

    internal static bool IsHandleOrWeakReference(Type type)
    {
        if (type == typeof(IntPtr) ||
            type == typeof(UIntPtr) ||
            type == typeof(GCHandle) ||
            type == typeof(WeakReference) ||
            typeof(SafeHandle).IsAssignableFrom(type) ||
            typeof(CriticalHandle).IsAssignableFrom(type))
        {
            return true;
        }

        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(WeakReference<>);
    }
}

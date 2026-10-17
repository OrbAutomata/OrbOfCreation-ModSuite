using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

internal static class ServiceCycleTypeGraphWalker
{
    internal static ServiceCycleTypeViolation? Validate(
        Type type,
        ServiceCycleTypeRole role,
        string path) =>
        FindViolation(type, role, true, new HashSet<Type>(), path);

    private static ServiceCycleTypeViolation? FindViolation(
        Type type,
        ServiceCycleTypeRole role,
        bool isRoot,
        ISet<Type> visited,
        string path)
    {
        // An action may not retain the capture buffer. It crosses to the main thread to be carried
        // out, and a buffer that crossed with it would be read after the cycle that filled it had
        // ended — and after the next capture had started overwriting it. A worker's state may hold
        // one, because state and buffer stay on the worker together.
        if (role is ServiceCycleTypeRole.Action && type == typeof(World.GameWorldCycleFrame))
            return Violation(path, type, "actions cannot retain the capture buffer");
        var immutable = ServiceCycleStorageShapeRules.IsImmutablePublication(role);
        // An array type reports its element's assembly, so it is judged by its element before the
        // boundary rules see it.
        if (type.IsArray)
        {
            if (immutable || isRoot)
                return Violation(path, type, "arrays are not immutable publication values or root frame/state values");
            return FindViolation(type.GetElementType()!, role, false, visited, path + "[]");
        }
        if (ServiceCycleBoundaryRules.IsAuditedExternalValueType(type)) return null;
        if (ServiceCycleBoundaryRules.IsAuditedPublicationValue(type))
        {
            // Common audits the container's own members, so they are not re-walked. Its type
            // arguments still face the full role rules: admitting a container must never become a
            // way to smuggle an unaudited value inside one.
            return FindTypeArgumentViolation(type, role, visited, path);
        }
        if (ServiceCycleBoundaryRules.IsExternalRuntimeBoundary(type))
            return Violation(path, type, "external game, loader, or patching runtime boundary");
        if (ServiceCycleBoundaryRules.IsHandleOrWeakReference(type))
            return Violation(path, type, "native handles, pointer-sized handles, and weak references are not service-cycle storage");
        if (type == typeof(object) || type.IsInterface || type.IsPointer || type.IsByRef || type.IsByRefLike)
            return Violation(path, type, "open-ended object, interface, pointer, or by-ref-like storage");
        if (typeof(Delegate).IsAssignableFrom(type))
            return Violation(path, type, "delegate storage");
        if (ServiceCycleStorageShapeRules.IsLeaf(type)) return null;

        if (typeof(IEnumerable).IsAssignableFrom(type))
            return Violation(path, type, "collection storage is not an audited publication shape");

        var nullable = Nullable.GetUnderlyingType(type);
        if (nullable is not null)
            return FindViolation(nullable, role, false, visited, path + ".Value");

        if (type.IsGenericType && !type.ContainsGenericParameters)
        {
            var argumentViolation = FindTypeArgumentViolation(type, role, visited, path);
            if (argumentViolation.HasValue) return argumentViolation;
        }

        if (!type.IsValueType && !type.IsSealed)
            return Violation(path, type, "service-cycle reference storage must be sealed against unsafe runtime subtypes");
        if (immutable && !ServiceCycleStorageShapeRules.IsReadonlyValue(type))
            return Violation(path, type, "published value structs must be readonly");

        if (!visited.Add(type)) return null;
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            var violation = AuditFields(current, role, immutable, visited, path);
            if (violation.HasValue) return violation;
            violation = AuditProperties(current, role, immutable, visited, path);
            if (violation.HasValue) return violation;
            violation = AuditMethods(current, role, visited, path);
            if (violation.HasValue) return violation;
        }

        return null;
    }

    /// <summary>
    /// Walks a closed generic's type arguments under the role's own rules. Shared by the ordinary
    /// generic pass and by audited-container admission so both treat contents identically.
    /// </summary>
    private static ServiceCycleTypeViolation? FindTypeArgumentViolation(
        Type type,
        ServiceCycleTypeRole role,
        ISet<Type> visited,
        string path)
    {
        if (!type.IsGenericType || type.ContainsGenericParameters) return null;
        var arguments = type.GetGenericArguments();
        for (var index = 0; index < arguments.Length; index++)
        {
            var violation = FindViolation(
                arguments[index], role, false, visited, path + $"<argument:{index}>");
            if (violation.HasValue) return violation;
        }

        return null;
    }

    private static ServiceCycleTypeViolation? AuditFields(
        Type current,
        ServiceCycleTypeRole role,
        bool immutable,
        ISet<Type> visited,
        string path)
    {
        foreach (var field in current.GetFields(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (field.IsStatic)
            {
                if (field.IsLiteral) continue;
                return Violation(path + "." + field.Name, field.FieldType,
                    "service-cycle data shapes cannot own non-constant static storage");
            }
            if (immutable && !field.IsInitOnly)
                return Violation(path + "." + field.Name, field.FieldType, "published fields must be readonly");
            if (!immutable && (field.IsPublic || field.IsFamily) &&
                ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(field.FieldType))
            {
                return Violation(path + "." + field.Name, field.FieldType,
                    "mutable backing storage cannot be publicly exposed");
            }

            var violation = FindViolation(
                field.FieldType, role, false, visited, path + "." + field.Name);
            if (violation.HasValue) return violation;
        }

        return null;
    }

    private static ServiceCycleTypeViolation? AuditProperties(
        Type current,
        ServiceCycleTypeRole role,
        bool immutable,
        ISet<Type> visited,
        string path)
    {
        foreach (var property in current.GetProperties(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            var getter = property.GetMethod;
            if (getter is not null && ServiceCycleStorageShapeRules.IsPublicOrProtected(getter))
            {
                if (ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(property.PropertyType))
                    return Violation(path + "." + property.Name, property.PropertyType,
                        "service-cycle properties cannot expose arrays, collections, memory views, object, or interface surfaces");
                var violation = FindViolation(
                    property.PropertyType, role, false, visited, path + "." + property.Name);
                if (violation.HasValue) return violation;
            }

            var setter = property.SetMethod;
            if (setter is not null && ServiceCycleStorageShapeRules.IsPublicOrProtected(setter) &&
                (immutable || role is ServiceCycleTypeRole.Frame or ServiceCycleTypeRole.State))
            {
                return Violation(path + "." + property.Name, property.PropertyType,
                    "service-cycle publication and frame/state surfaces cannot expose public or protected setters");
            }
        }

        return null;
    }

    private static ServiceCycleTypeViolation? AuditMethods(
        Type current,
        ServiceCycleTypeRole role,
        ISet<Type> visited,
        string path)
    {
        if (!ServiceCycleStorageShapeRules.ShouldAuditDeclaredMethods(current)) return null;
        foreach (var method in current.GetMethods(
                     BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!ServiceCycleStorageShapeRules.IsPublicOrProtected(method) ||
                ServiceCycleStorageShapeRules.IsHarmlessObjectContract(method))
            {
                continue;
            }
            if (method.IsGenericMethodDefinition)
                return Violation(path + "." + method.Name, current, "open generic method surfaces are not permitted");
            var violation = FindMethodSurfaceViolation(
                method.ReturnType, role, visited, path + "." + method.Name + " return");
            if (violation.HasValue) return violation;
            foreach (var parameter in method.GetParameters())
            {
                violation = FindMethodSurfaceViolation(
                    parameter.ParameterType, role, visited,
                    path + "." + method.Name + " parameter " + parameter.Name);
                if (violation.HasValue) return violation;
            }
        }

        foreach (var constructor in current.GetConstructors(
                     BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
        {
            if (!ServiceCycleStorageShapeRules.IsPublicOrProtected(constructor)) continue;
            foreach (var parameter in constructor.GetParameters())
            {
                var violation = FindMethodSurfaceViolation(
                    parameter.ParameterType, role, visited, path + ".ctor parameter " + parameter.Name);
                if (violation.HasValue) return violation;
            }
        }

        return null;
    }

    private static ServiceCycleTypeViolation? FindMethodSurfaceViolation(
        Type type,
        ServiceCycleTypeRole role,
        ISet<Type> visited,
        string path)
    {
        if (type == typeof(void)) return null;
        if (type.IsGenericParameter || type.ContainsGenericParameters)
            return Violation(path, type, "open generic method surfaces are not permitted");
        if (type.IsByRef || type.IsPointer || type.IsByRefLike)
            return Violation(path, type, "by-ref, pointer, and by-ref-like method surfaces are not permitted");
        if (ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(type))
            return Violation(path, type,
                "open-ended object, interface, array, collection, and memory-view method surfaces are not permitted");
        return FindViolation(type, role, false, visited, path);
    }

    private static ServiceCycleTypeViolation Violation(string path, Type type, string reason) =>
        new(path, type, reason);
}

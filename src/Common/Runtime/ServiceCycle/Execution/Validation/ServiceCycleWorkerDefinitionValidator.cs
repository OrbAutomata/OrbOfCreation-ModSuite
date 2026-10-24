using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.ServiceCycle.Execution.Validation;

internal static class ServiceCycleWorkerDefinitionValidator
{
    private const string RuntimeStorageNamespace = "OrbModding.Common.Runtime.ServiceCycle";

    internal static void EnsureSeparated<TState, TAction>(
        IServiceCycleMainThreadDefinition<TAction> mainDefinition,
        IServiceCycleWorkerStateDefinition<TState> workerDefinition)
    {
        // The shape-agnostic half of the service is what a worker must never reach. Both shapes
        // implement it, so one contract states the rule for both — and it is the half that owns the
        // native adapters, which is the reason the rule exists.
        var mainContract = typeof(IServiceCycleMainThreadDefinition<TAction>);
        var mainType = mainDefinition.GetType();
        var workerType = workerDefinition.GetType();
        if (ReferenceEquals(mainDefinition, workerDefinition) || mainContract.IsAssignableFrom(workerType))
            throw new InvalidOperationException("Main-thread and worker service definitions must be distinct objects and roles.");

        var visited = new HashSet<Type>();
        ValidateWorkerGraph(
            workerType,
            mainType,
            mainContract,
            visited,
            workerType.Name,
            workerDefinition);
    }

    private static void ValidateWorkerGraph(
        Type type,
        Type mainType,
        Type mainContract,
        HashSet<Type> visited,
        string path,
        object workerDefinition)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (ServiceCycleBoundaryRules.IsAuditedExternalValueType(type)) return;
        if (ServiceCycleBoundaryRules.IsExternalRuntimeBoundary(type))
            throw new InvalidOperationException($"Worker definition '{path}' reaches external runtime type '{type.FullName}'.");
        if (mainContract.IsAssignableFrom(type) || mainType.IsAssignableFrom(type))
            throw new InvalidOperationException($"Worker definition '{path}' retains the main-thread service definition.");
        if (type.IsPointer || type.IsByRef || ServiceCycleBoundaryRules.IsHandleOrWeakReference(type))
            throw new InvalidOperationException($"Worker definition '{path}' contains an unsafe runtime handle.");
        if (typeof(Delegate).IsAssignableFrom(type))
            throw new InvalidOperationException($"Worker definition '{path}' contains a delegate that could close over native state.");
        if (ServiceCycleStorageShapeRules.IsUnsafeMutableExposure(type))
            throw new InvalidOperationException(
                $"Worker definition '{path}' contains open-ended object, interface, array, collection, or memory-view storage.");
        if (type.IsGenericType)
        {
            foreach (var argument in type.GetGenericArguments())
                ValidateWorkerGraph(
                    argument,
                    mainType,
                    mainContract,
                    visited,
                    path + "<arg>",
                    workerDefinition);
        }

        var typeNamespace = type.Namespace ?? string.Empty;
        if (ServiceCycleStorageShapeRules.IsLeaf(type)) return;
        // An audited publication value is the one kind of runtime reference type a worker may hold:
        // immutable by construction, carrying no runtime ownership. Its type arguments were already
        // walked by the generic-argument pass above, so admitting the container admits nothing else.
        // This must stay ahead of the runtime-namespace rejection below: PublicationTable lives in it.
        if (ServiceCycleBoundaryRules.IsAuditedPublicationValue(type)) return;
        // Runtime-owned reference types are the mutable plumbing a worker must never reach. Value
        // types under the same namespace are not rejected but not waved through either: they fall to
        // the field walk below, which is what proves the struct carries no path back to the runtime.
        if (!type.IsValueType && IsRuntimeOwnedStorage(type))
            throw new InvalidOperationException($"Worker definition '{path}' contains mutable runtime storage.");
        if (typeNamespace.StartsWith("System", StringComparison.Ordinal))
            throw new InvalidOperationException($"Worker definition '{path}' contains opaque framework reference storage.");
        if (!type.IsValueType && !type.IsSealed)
            throw new InvalidOperationException($"Worker definition '{path}' contains an unsealed reference type.");
        if (!visited.Add(type)) return;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static |
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(flags))
            {
                if (field.IsStatic)
                {
                    if (field.IsLiteral) continue;
                    throw new InvalidOperationException(
                        $"Worker definition '{path}.{field.Name}' contains non-constant static storage.");
                }
                ValidateWorkerGraph(
                    field.FieldType,
                    mainType,
                    mainContract,
                    visited,
                    path + "." + field.Name,
                    workerDefinition);
            }
        }
    }

    /// <summary>
    /// The runtime's own service-cycle types.
    /// </summary>
    /// <remarks>
    /// Its only bearer was <c>ServiceCycleReplayWorker&lt;&gt;</c>, and it retired with replay.
    /// Revive that pattern if a runtime-owned worker base ever needs worker-resident buffers again.
    /// </remarks>
    private static bool IsRuntimeOwnedStorage(Type type) =>
        (type.Namespace ?? string.Empty).StartsWith(RuntimeStorageNamespace, StringComparison.Ordinal);
}

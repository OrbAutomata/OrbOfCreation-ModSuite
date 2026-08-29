using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace OrbModding.Common;

/// <summary>
/// The suite's one reflection boundary for the game's stable-identity registry.
/// </summary>
/// <remarks>
/// Binding is lazy because the game assembly is not guaranteed to be loaded at plugin construction.
/// Once the exact public declarations resolve, both mutation-grade typed resolution and the
/// lifecycle identity catalog use these same members. A shape mismatch is terminal for the current
/// process; an unloaded type or null registry is merely not ready yet.
/// </remarks>
internal sealed class RuntimeIdentityRegistryBinding
{
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

    private readonly Func<Type?> _resolveIdentityType;
    private readonly bool _requireStableIdentityContract;
    private FieldInfo? _runtimeLookup;
    private MethodInfo? _getGuid;
    private string? _contractFailure;

    internal RuntimeIdentityRegistryBinding(
        Func<Type?>? resolveIdentityType = null,
        bool requireStableIdentityContract = true)
    {
        _resolveIdentityType = resolveIdentityType ??
            (static () => Type.GetType("IdScriptableObject, Assembly-CSharp", false));
        _requireStableIdentityContract = requireStableIdentityContract;
    }

    internal static RuntimeIdentityRegistryBinding Shared { get; } = new();

    internal TypedRegistrySourceSnapshot Read()
    {
        if (_contractFailure is not null)
            return TypedRegistrySourceSnapshot.ContractUnavailable(_contractFailure);
        if (_runtimeLookup is null)
        {
            var idType = _resolveIdentityType();
            if (idType is null)
                return TypedRegistrySourceSnapshot.NotReady(
                    "native IdScriptableObject type is not loaded yet");

            var lookup = idType.GetField("RuntimeLookup", StaticFlags);
            var getGuid = _requireStableIdentityContract
                ? idType.GetMethod("GetGuid", InstanceFlags, null, Type.EmptyTypes, null)
                : null;
            if (!HasRuntimeLookupContract(
                    lookup, idType, _requireStableIdentityContract) ||
                (_requireStableIdentityContract &&
                 !HasExactGetGuidContract(getGuid, idType)))
            {
                _contractFailure = _requireStableIdentityContract
                    ? "native public IdScriptableObject.RuntimeLookup/GetGuid contract is unavailable"
                    : "native public identity RuntimeLookup dictionary contract is unavailable";
                return TypedRegistrySourceSnapshot.ContractUnavailable(_contractFailure);
            }

            _runtimeLookup = lookup;
            _getGuid = getGuid;
        }

        return _runtimeLookup.GetValue(null) is IDictionary registry
            ? TypedRegistrySourceSnapshot.Ready(registry)
            : TypedRegistrySourceSnapshot.NotReady(
                "native IdScriptableObject.RuntimeLookup is not ready");
    }

    internal Guid? ReadStableUuid(object value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        if (_getGuid is null)
        {
            var source = Read();
            if (!source.IsReady || _getGuid is null)
                throw new MissingMethodException("IdScriptableObject", "GetGuid");
        }

        return _getGuid.Invoke(value, Array.Empty<object>()) is Guid uuid ? uuid : null;
    }

    internal static bool HasExactRuntimeLookupContract(FieldInfo? field, Type idType)
        => HasRuntimeLookupContract(field, idType, requireExactValueType: true);

    private static bool HasRuntimeLookupContract(
        FieldInfo? field,
        Type idType,
        bool requireExactValueType)
    {
        if (field is null ||
            field.DeclaringType != idType ||
            !field.IsPublic ||
            !field.IsStatic ||
            !field.FieldType.IsGenericType ||
            field.FieldType.GetGenericTypeDefinition() != typeof(Dictionary<,>))
            return false;
        var arguments = field.FieldType.GetGenericArguments();
        return arguments.Length == 2 &&
            arguments[0] == typeof(Guid) &&
            (!requireExactValueType || arguments[1] == idType);
    }

    internal static bool HasExactGetGuidContract(MethodInfo? method, Type idType) =>
        method is not null &&
        method.DeclaringType == idType &&
        method.IsPublic &&
        !method.IsStatic &&
        method.ReturnType == typeof(Guid) &&
        method.GetParameters().Length == 0;
}

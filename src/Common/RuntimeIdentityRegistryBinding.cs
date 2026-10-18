using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbModding.Common;

/// <summary>
/// The suite's one reflection boundary for the game's stable-identity registry.
/// </summary>
/// <remarks>
/// Binding is lazy because the game assembly is not guaranteed to be loaded at plugin construction.
/// Once the exact public declarations resolve, they are compiled and published as one immutable
/// snapshot, and both mutation-grade typed resolution and the lifecycle identity catalog read through
/// those same delegates. Threads that bind at the same time each publish a whole snapshot, so none
/// reads a half-bound one. A shape mismatch is terminal for the current process; an unloaded type or
/// null registry is merely not ready yet.
/// </remarks>
internal sealed class RuntimeIdentityRegistryBinding
{
    private const BindingFlags StaticFlags =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private const BindingFlags InstanceFlags =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;
    private const string TypeNotLoaded = "native IdScriptableObject type is not loaded yet";

    private readonly Func<Type?> _resolveIdentityType;
    private readonly bool _requireStableIdentityContract;
    private volatile Bound? _bound;

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
        var bound = _bound ?? Bind();
        if (bound is null) return TypedRegistrySourceSnapshot.NotReady(TypeNotLoaded);
        if (bound.ContractFailure is { } failure) return TypedRegistrySourceSnapshot.ContractUnavailable(failure);

        return bound.RuntimeLookup!() is { } registry
            ? TypedRegistrySourceSnapshot.Ready(registry)
            : TypedRegistrySourceSnapshot.NotReady(
                "native IdScriptableObject.RuntimeLookup is not ready");
    }

    internal Guid ReadStableUuid(object value)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));
        var bound = _bound ?? Bind();
        if (bound?.GetGuid is { } getGuid) return getGuid(value);
        throw new InvalidOperationException(_requireStableIdentityContract
            ? bound?.ContractFailure ?? TypeNotLoaded
            : "this identity binding does not read IdScriptableObject.GetGuid");
    }

    /// <summary>
    /// The compiled identity read for <paramref name="type"/>, through <see cref="StableUuidMethod"/>'s
    /// accepted shapes. Every <c>IdScriptableObject</c> shares the one delegate this binding compiled.
    /// </summary>
    internal Func<object, Guid>? StableUuid(Type type)
    {
        var method = StableUuidMethod(type, out var bound);
        if (method is null) return null;
        return bound is not null && method == bound.GetGuidMethod ? bound.GetGuid : Compile(method);
    }

    /// <summary>
    /// The suite's one accepted-shape list for a native identity accessor on <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// An <c>IdScriptableObject</c> answers through the bound <c>GetGuid()</c>, and stays unreadable
    /// while that contract is unavailable. The few plain game objects that carry an identity, such as
    /// <c>ConsumableUsage</c> and <c>PlayerLoadout</c>, declare their own exact <c>GetGuid()</c>.
    /// Anything else, or anything while the game's identity type is not loaded, has no identity to read.
    /// </remarks>
    internal MethodInfo? StableUuidMethod(Type type) => StableUuidMethod(type, out _);

    private MethodInfo? StableUuidMethod(Type type, out Bound? bound)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        bound = null;
        var method = type.GetMethod(
            "GetGuid", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
        var identityType = _resolveIdentityType();
        if (method is null || method.ReturnType != typeof(Guid) || identityType is null) return null;
        if (method.DeclaringType != identityType) return method;
        bound = _bound ?? Bind();
        return bound?.GetGuidMethod;
    }

    private Bound? Bind()
    {
        var idType = _resolveIdentityType();
        if (idType is null) return null;

        var lookup = idType.GetField("RuntimeLookup", StaticFlags);
        var getGuid = _requireStableIdentityContract
            ? idType.GetMethod("GetGuid", InstanceFlags, null, Type.EmptyTypes, null)
            : null;
        Bound bound;
        if (!HasRuntimeLookupContract(lookup, idType, _requireStableIdentityContract) ||
            (_requireStableIdentityContract && !HasExactGetGuidContract(getGuid, idType)))
        {
            bound = new Bound(_requireStableIdentityContract
                ? "native public IdScriptableObject.RuntimeLookup/GetGuid contract is unavailable"
                : "native public identity RuntimeLookup dictionary contract is unavailable");
        }
        else
        {
            bound = new Bound(
                getGuid,
                getGuid is null ? null : Compile(getGuid),
                Expression.Lambda<Func<IDictionary?>>(
                    Expression.Convert(Expression.Field(null, lookup!), typeof(IDictionary))).Compile());
        }

        _bound = bound;
        return bound;
    }

    private static Func<object, Guid> Compile(MethodInfo getGuid)
    {
        var source = Expression.Parameter(typeof(object), "source");
        return Expression.Lambda<Func<object, Guid>>(
            Expression.Call(Expression.Convert(source, getGuid.DeclaringType!), getGuid), source).Compile();
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

    private sealed class Bound
    {
        internal Bound(string contractFailure) => ContractFailure = contractFailure;

        internal Bound(
            MethodInfo? getGuidMethod,
            Func<object, Guid>? getGuid,
            Func<IDictionary?> runtimeLookup)
        {
            GetGuidMethod = getGuidMethod;
            GetGuid = getGuid;
            RuntimeLookup = runtimeLookup;
        }

        internal string? ContractFailure { get; }
        internal MethodInfo? GetGuidMethod { get; }
        internal Func<object, Guid>? GetGuid { get; }
        internal Func<IDictionary?>? RuntimeLookup { get; }
    }
}

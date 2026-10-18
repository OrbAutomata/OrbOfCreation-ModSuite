using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbModding.Common;

/// <summary>A declared native contract the running game did not satisfy.</summary>
internal sealed class NativeBindingException : Exception
{
    internal NativeBindingException(string message) : base(message) { }
}

/// <summary>
/// The one strict binder for a feature's lifecycle binding set: every member is looked up by its
/// declared contract id, with exact owner, parameter, field and return types, and compiled once.
/// </summary>
/// <remarks>
/// <para>
/// A member that is missing, withheld or shaped differently throws <see cref="NativeBindingException"/>
/// naming the contract and the member, and <see cref="TryBind{TBindings}"/> turns that into the one
/// reason the whole set is unavailable. The read side (<c>NativeAccessorBinder</c>) answers null
/// instead, because a world category that cannot read reports itself missing; an action that cannot
/// bind must not run at all.
/// </para>
/// <para>
/// Lookups walk the owner's base chain, so a member the game declares on a generic base is found the
/// same way as one on the owner. An id outside the declared list is a suite defect and throws
/// <see cref="ArgumentException"/>, which no caller catches.
/// </para>
/// </remarks>
internal sealed class NativeBindingSet
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    private readonly HashSet<string> _declared;
    private readonly Func<string, Type?> _resolveType;
    private readonly Func<string, bool> _includeContract;

    private NativeBindingSet(
        IEnumerable<string> declared,
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract)
    {
        _declared = new HashSet<string>(declared, StringComparer.Ordinal);
        _resolveType = resolveType;
        _includeContract = includeContract;
    }

    internal static bool TryBind<TBindings>(
        string feature,
        IEnumerable<string> declared,
        Func<string, Type?>? resolveType,
        Func<string, bool>? includeContract,
        Func<NativeBindingSet, TBindings> build,
        out TBindings? bindings,
        out string reason)
        where TBindings : class
    {
        var set = new NativeBindingSet(
            declared,
            resolveType ?? ReflectionUtil.FindLoadedType,
            includeContract ?? (static _ => true));
        try
        {
            bindings = build(set);
            reason = string.Empty;
            return true;
        }
        catch (NativeBindingException exception)
        {
            bindings = null;
            reason = "The complete " + feature + " binding set is unavailable: " + exception.Message;
            return false;
        }
    }

    internal Type Type(string id, string name)
    {
        Require(id);
        return _resolveType(name) ?? throw new NativeBindingException(id + ": " + name + " was unavailable.");
    }

    /// <summary>A type the suite compiles against, still gated by its declared contract.</summary>
    internal Type Type(string id, Type known)
    {
        Require(id);
        return known;
    }

    /// <summary>The closed generic base of <paramref name="type"/> whose definition is <paramref name="definitionName"/>.</summary>
    internal Type GenericBase(string id, Type type, string definitionName)
    {
        Require(id);
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition().Name == definitionName)
                return current;
        }
        throw new NativeBindingException(id + ": " + type.Name + " has no " + definitionName + " base.");
    }

    internal TDelegate Call<TDelegate>(
        string id,
        Type owner,
        string name,
        Type returnType,
        params Type[] parameters)
        where TDelegate : Delegate =>
        Compile<TDelegate>(Method(id, owner, name, returnType, isStatic: false, parameters));

    internal TDelegate StaticCall<TDelegate>(
        string id,
        Type owner,
        string name,
        Type returnType,
        params Type[] parameters)
        where TDelegate : Delegate =>
        Compile<TDelegate>(Method(id, owner, name, returnType, isStatic: true, parameters));

    /// <summary>The owner's stable UUID, read through the suite's one identity accessor.</summary>
    internal Func<object, Guid> StableUuid(string id, Type owner)
    {
        Require(id);
        return Compile<Func<object, Guid>>(
            RuntimeIdentityRegistryBinding.Shared.StableUuidMethod(owner) ?? throw Mismatch(id, owner, "GetGuid"));
    }

    internal TDelegate Field<TDelegate>(string id, Type owner, string name, Type fieldType)
        where TDelegate : Delegate =>
        Read<TDelegate>(FieldMember(id, owner, name, fieldType, isStatic: false));

    internal TDelegate StaticField<TDelegate>(string id, Type owner, string name, Type fieldType)
        where TDelegate : Delegate =>
        Read<TDelegate>(FieldMember(id, owner, name, fieldType, isStatic: true));

    internal TDelegate Setter<TDelegate>(string id, Type owner, string name, Type fieldType)
        where TDelegate : Delegate =>
        Write<TDelegate>(FieldMember(id, owner, name, fieldType, isStatic: false));

    internal TDelegate New<TDelegate>(string id, Type type, params Type[] parameters)
        where TDelegate : Delegate
    {
        Require(id);
        var constructor = type.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null)
            ?? throw Mismatch(id, type, ".ctor");
        var invoke = Signature<TDelegate>();
        var arguments = Arguments(invoke);
        var body = Expression.New(constructor, constructor.GetParameters()
            .Select((parameter, index) => Adapt(arguments[index], parameter.ParameterType)));
        return Expression.Lambda<TDelegate>(Adapt(body, invoke.ReturnType), arguments).Compile();
    }

    private MethodInfo Method(
        string id,
        Type owner,
        string name,
        Type returnType,
        bool isStatic,
        Type[] parameters)
    {
        Require(id);
        var flags = Declared | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (var current = owner; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, flags, null, parameters, null);
            if (method is not null && method.ReturnType == returnType) return method;
        }
        throw Mismatch(id, owner, name);
    }

    private FieldInfo FieldMember(string id, Type owner, string name, Type fieldType, bool isStatic)
    {
        Require(id);
        var flags = Declared | (isStatic ? BindingFlags.Static : BindingFlags.Instance);
        for (var current = owner; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, flags);
            if (field is not null && field.FieldType == fieldType) return field;
        }
        throw Mismatch(id, owner, name);
    }

    private void Require(string id)
    {
        if (!_declared.Contains(id))
            throw new ArgumentException(id + " is not a declared contract of this binding set.", nameof(id));
        if (!_includeContract(id))
            throw new NativeBindingException("Required contract " + id + " was withheld.");
    }

    private static NativeBindingException Mismatch(string id, Type owner, string name) =>
        new(id + ": " + owner.Name + "." + name + " did not match the audited signature.");

    private static TDelegate Compile<TDelegate>(MethodInfo method) where TDelegate : Delegate
    {
        var invoke = Signature<TDelegate>();
        var arguments = Arguments(invoke);
        var offset = method.IsStatic ? 0 : 1;
        var parameters = method.GetParameters();
        if (arguments.Length != parameters.Length + offset)
            throw new ArgumentException(typeof(TDelegate).Name + " does not fit " + method.Name + ".");

        var target = method.IsStatic ? null : Adapt(arguments[0], method.DeclaringType!);
        var call = Expression.Call(target, method, parameters
            .Select((parameter, index) => Adapt(arguments[index + offset], parameter.ParameterType)));
        return Expression.Lambda<TDelegate>(Adapt(call, invoke.ReturnType), arguments).Compile();
    }

    private static TDelegate Read<TDelegate>(FieldInfo field) where TDelegate : Delegate
    {
        var invoke = Signature<TDelegate>();
        var arguments = Arguments(invoke);
        var target = field.IsStatic ? null : Adapt(arguments[0], field.DeclaringType!);
        return Expression.Lambda<TDelegate>(
            Adapt(Expression.Field(target, field), invoke.ReturnType), arguments).Compile();
    }

    private static TDelegate Write<TDelegate>(FieldInfo field) where TDelegate : Delegate
    {
        var arguments = Arguments(Signature<TDelegate>());
        var target = Adapt(arguments[0], field.DeclaringType!);
        return Expression.Lambda<TDelegate>(
            Expression.Assign(Expression.Field(target, field), Adapt(arguments[1], field.FieldType)),
            arguments).Compile();
    }

    private static MethodInfo Signature<TDelegate>() => typeof(TDelegate).GetMethod(nameof(Action.Invoke))!;

    private static ParameterExpression[] Arguments(MethodInfo invoke) =>
        invoke.GetParameters().Select(parameter => Expression.Parameter(parameter.ParameterType)).ToArray();

    private static Expression Adapt(Expression value, Type type) =>
        value.Type == type || type == typeof(void) ? value : Expression.Convert(value, type);
}

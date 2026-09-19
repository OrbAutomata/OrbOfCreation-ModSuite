using System;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Exact v1.0.5 binding set for the player-visible structure enable/disable button.</summary>
internal sealed class StructureLifecycleNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "structure-lifecycle.structure.type-action",
        "structure-lifecycle.available-action",
        "structure-lifecycle.disabled-action",
        "structure-lifecycle.toggle-action",
    };

    private StructureLifecycleNativeBindings(
        Type structureType,
        Func<object, bool> available,
        Func<object, bool> disabled,
        Action<object> toggle)
    {
        StructureType = structureType;
        Available = available;
        Disabled = disabled;
        Toggle = toggle;
    }

    internal Type StructureType { get; }
    internal Func<object, bool> Available { get; }
    internal Func<object, bool> Disabled { get; }
    internal Action<object> Toggle { get; }

    internal static bool TryCreate(
        out StructureLifecycleNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Require(0, includeContract);
            var structure = resolveType("StructureSO") ??
                throw new InvalidOperationException("StructureSO was unavailable");
            var available = Method(1, structure, "IsAvailable", typeof(bool), includeContract);
            var disabled = Field(2, structure, "disabled", typeof(bool), includeContract);
            var toggle = Method(3, structure, "ToggleDisabled", typeof(void), includeContract);
            bindings = new StructureLifecycleNativeBindings(
                structure, Func<bool>(available), FieldFunc<bool>(disabled), Action(toggle));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or AmbiguousMatchException or NotSupportedException)
        {
            reason = "Structure lifecycle contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private static MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type result,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return method;
    }

    private static FieldInfo Field(
        int index,
        Type owner,
        string name,
        Type type,
        Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != type)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match.");
        return field;
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Func<object, T> FieldFunc<T>(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            target).Compile();
    }

    private static Action<object> Action(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }
}

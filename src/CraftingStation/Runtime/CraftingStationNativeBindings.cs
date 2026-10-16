using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for the visible Brewing Station controls.</summary>
internal sealed class CraftingStationNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "crafting-station.structure.type-action",
        "crafting-station.station.type-action",
        "crafting-station.instance-list.type-action",
        "crafting-station.list-element.type-action",
        "crafting-station.element.type-action",
        "crafting-station.tooltipable-interface.type-action",
        "crafting-station.tooltipable-object.type-action",
        "crafting-station.tooltipable-guid-action",
        "crafting-station.structure-all-action",
        "crafting-station.structure-instances-action",
        "crafting-station.instance-list-get-all-action",
        "crafting-station.structure-ingredient-lists-action",
        "crafting-station.list-element-elements-action",
        "crafting-station.element-tooltipable-action",
        "crafting-station.element-available-action",
        "crafting-station.station-reference-action",
        "crafting-station.station-guid-action",
        "crafting-station.station-ingredient-action",
        "crafting-station.station-output-action",
        "crafting-station.station-output-list-action",
        "crafting-station.station-output-visible-action",
        "crafting-station.station-loaded-action",
        "crafting-station.station-active-action",
        "crafting-station.station-level-action",
        "crafting-station.station-min-level-action",
        "crafting-station.station-max-level-action",
        "crafting-station.station-set-ingredient-action",
        "crafting-station.station-set-output-action",
        "crafting-station.station-set-level-action",
        "crafting-station.station-set-active-action",
    };

    private CraftingStationNativeBindings(
        Type structureType,
        Type stationType,
        Type elementType,
        Func<IList?> structures,
        Func<object, IList?> instances,
        Func<object, IList?> ingredientLists,
        Func<object, IList?> elements,
        Func<object, Guid> elementId,
        Func<object, bool> elementAvailable,
        Func<object, object?> stationReference,
        Func<object, Guid> stationId,
        Func<object, int, object?> ingredient,
        Func<object, object?> output,
        Func<object, IList?> outputList,
        Func<object, object, bool> outputVisible,
        Func<object, bool> loaded,
        Func<object, bool> active,
        Func<object, int> level,
        Func<object, int> minimumLevel,
        Func<object, int> maximumLevel,
        Action<object, int, object> setIngredient,
        Action<object, object> setOutput,
        Action<object, int> setLevel,
        Action<object, bool> setActive)
    {
        StructureType = structureType;
        StationType = stationType;
        ElementType = elementType;
        Structures = structures;
        Instances = instances;
        IngredientLists = ingredientLists;
        Elements = elements;
        ElementId = elementId;
        ElementAvailable = elementAvailable;
        StationReference = stationReference;
        StationId = stationId;
        Ingredient = ingredient;
        Output = output;
        OutputList = outputList;
        OutputVisible = outputVisible;
        Loaded = loaded;
        Active = active;
        Level = level;
        MinimumLevel = minimumLevel;
        MaximumLevel = maximumLevel;
        SetIngredient = setIngredient;
        SetOutput = setOutput;
        SetLevel = setLevel;
        SetActive = setActive;
    }

    internal Type StructureType { get; }
    internal Type StationType { get; }
    internal Type ElementType { get; }
    internal Func<IList?> Structures { get; }
    internal Func<object, IList?> Instances { get; }
    internal Func<object, IList?> IngredientLists { get; }
    internal Func<object, IList?> Elements { get; }
    internal Func<object, Guid> ElementId { get; }
    internal Func<object, bool> ElementAvailable { get; }
    internal Func<object, object?> StationReference { get; }
    internal Func<object, Guid> StationId { get; }
    internal Func<object, int, object?> Ingredient { get; }
    internal Func<object, object?> Output { get; }
    internal Func<object, IList?> OutputList { get; }
    internal Func<object, object, bool> OutputVisible { get; }
    internal Func<object, bool> Loaded { get; }
    internal Func<object, bool> Active { get; }
    internal Func<object, int> Level { get; }
    internal Func<object, int> MinimumLevel { get; }
    internal Func<object, int> MaximumLevel { get; }
    internal Action<object, int, object> SetIngredient { get; }
    internal Action<object, object> SetOutput { get; }
    internal Action<object, int> SetLevel { get; }
    internal Action<object, bool> SetActive { get; }

    internal static bool TryCreate(
        out CraftingStationNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            Type T(int index, string name)
            {
                Require(index, includeContract);
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable");
            }

            var structure = T(0, "CraftingStructureSO");
            var station = T(1, "CraftingStructure");
            var instanceList = T(2, "CraftingStructureListVariable");
            var listElement = T(3, "CraftingStructureSO+TypeListElement");
            var element = T(4, "CraftingStructureSO+TypeElement");
            var tooltipableInterface = T(5, "ITooltipable");
            var tooltipableObject = T(6, "TooltipableObject");

            Require(7, includeContract);
            var getGuid = tooltipableObject.GetMethod("GetGuid", Instance, null, Type.EmptyTypes, null);
            if (getGuid is null || getGuid.ReturnType != typeof(Guid))
                throw new InvalidOperationException("TooltipableObject.GetGuid did not match the audited signature");
            var all = StaticCollection(8, structure, "All", includeContract);
            var instances = Reference(9, structure, "instances", instanceList, includeContract);
            var readInstances = ListMethod(10, instanceList, "GetAll", station, includeContract);
            var ingredientLists = Collection(11, structure, "ingredientLists", listElement, includeContract);
            var elements = ListMethod(12, listElement, "GetElements", element, includeContract);
            var getTooltipable = Method(13, element, "GetTooltipable", tooltipableInterface, Type.EmptyTypes, includeContract);
            var available = Method(14, element, "IsAvailable", typeof(bool), Type.EmptyTypes, includeContract);
            var reference = Method(15, station, "get_reference", structure, Type.EmptyTypes, includeContract);
            var id = Method(16, station, "GetGuid", typeof(Guid), Type.EmptyTypes, includeContract);
            var ingredient = Method(17, station, "GetIngredient", element, new[] { typeof(int) }, includeContract);
            var output = Method(18, station, "GetOutput", element, Type.EmptyTypes, includeContract);
            var outputList = ListMethod(19, station, "GetOutputList", element, includeContract);
            var outputVisible = Method(20, station, "IsOutputVisible", typeof(bool), new[] { element }, includeContract);
            var loaded = Method(21, station, "IsLoaded", typeof(bool), Type.EmptyTypes, includeContract);
            var active = Method(22, station, "IsActive", typeof(bool), Type.EmptyTypes, includeContract);
            var level = Method(23, station, "GetLevel", typeof(int), Type.EmptyTypes, includeContract);
            var minimum = Method(24, station, "GetMinSelectedLevel", typeof(int), Type.EmptyTypes, includeContract);
            var maximum = Method(25, station, "GetMaxSelectedLevel", typeof(int), Type.EmptyTypes, includeContract);
            var setIngredient = Method(26, station, "SetIngredient", typeof(void),
                new[] { typeof(int), element }, includeContract);
            var setOutput = Method(27, station, "SetOutput", typeof(void), new[] { element }, includeContract);
            var setLevel = Method(28, station, "SetSelectedLevel", typeof(void), new[] { typeof(int) }, includeContract);
            var setActive = Method(29, station, "SetActive", typeof(void), new[] { typeof(bool) }, includeContract);

            bindings = new CraftingStationNativeBindings(
                structure,
                station,
                element,
                StaticList(all),
                NestedList(instances, readInstances),
                ListField(ingredientLists),
                ListFunc(elements),
                ReferenceGuid(getTooltipable, tooltipableObject, getGuid),
                Func<bool>(available),
                ObjectFunc(reference),
                Func<Guid>(id),
                ObjectFuncInt(ingredient),
                ObjectFunc(output),
                ListFunc(outputList),
                FuncObject<bool>(outputVisible),
                Func<bool>(loaded),
                Func<bool>(active),
                Func<int>(level),
                Func<int>(minimum),
                Func<int>(maximum),
                ActionIntObject(setIngredient),
                ActionObject(setOutput),
                ActionInt(setLevel),
                ActionBool(setActive));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or
            InvalidOperationException or NotSupportedException)
        {
            reason = "Brewing Station contracts are unavailable: " +
                exception.GetBaseException().Message;
            return false;
        }
    }

    private static void Require(int index, Func<string, bool> include)
    {
        if (!include(ContractIds[index]))
            throw new InvalidOperationException(ContractIds[index] + " was unavailable");
    }

    private static FieldInfo StaticCollection(
        int index, Type owner, string name, Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Static);
        if (field is null || !field.IsStatic || !typeof(IList).IsAssignableFrom(field.FieldType))
            throw new InvalidOperationException(owner.Name + "." + name + " did not expose a list");
        return field;
    }

    private static FieldInfo Collection(
        int index, Type owner, string name, Type element, Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || !typeof(IList).IsAssignableFrom(field.FieldType) ||
            !HasElement(field.FieldType, element))
            throw new InvalidOperationException(owner.Name + "." + name + " did not expose the audited list");
        return field;
    }

    private static FieldInfo Reference(
        int index, Type owner, string name, Type valueType, Func<string, bool> include)
    {
        Require(index, include);
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != valueType)
            throw new InvalidOperationException(owner.Name + "." + name + " did not expose the audited reference");
        return field;
    }

    private static MethodInfo ListMethod(
        int index, Type owner, string name, Type element, Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, Type.EmptyTypes, null);
        if (method is null || method.IsStatic || !typeof(IList).IsAssignableFrom(method.ReturnType) ||
            !HasElement(method.ReturnType, element))
            throw new InvalidOperationException(owner.Name + "." + name + " did not return the audited list");
        return method;
    }

    private static bool HasElement(Type collection, Type element)
    {
        if (collection.IsGenericType && collection.GetGenericArguments().Length == 1 &&
            collection.GetGenericArguments()[0] == element) return true;
        foreach (var contract in collection.GetInterfaces())
        {
            if (contract.IsGenericType &&
                contract.GetGenericTypeDefinition() == typeof(IList<>) &&
                contract.GetGenericArguments()[0] == element) return true;
        }
        return false;
    }

    private static MethodInfo Method(
        int index,
        Type owner,
        string name,
        Type result,
        Type[] parameters,
        Func<string, bool> include)
    {
        Require(index, include);
        var method = owner.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static Func<IList?> StaticList(FieldInfo field) => () => field.GetValue(null) as IList;
    private static Func<object, IList?> ListField(FieldInfo field) => target => field.GetValue(target) as IList;

    private static Func<object, IList?> NestedList(FieldInfo field, MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Variable(field.FieldType, "value");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Block(
                new[] { value },
                Expression.Assign(
                    value,
                    Expression.Field(Expression.Convert(target, field.DeclaringType!), field)),
                Expression.Condition(
                    Expression.Equal(value, Expression.Constant(null, field.FieldType)),
                    Expression.Constant(null, typeof(IList)),
                    Expression.Convert(Expression.Call(value, method), typeof(IList)))),
            target).Compile();
    }

    private static Func<object, T> Func<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(T)),
            target).Compile();
    }

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)),
            target).Compile();
    }

    private static Func<object, int, object?> ObjectFuncInt(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Func<object, int, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value), typeof(object)),
            target, value).Compile();
    }

    private static Func<object, IList?> ListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(IList)),
            target).Compile();
    }

    private static Func<object, object, T> FuncObject<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, T>>(
            Expression.Convert(Expression.Call(
                Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), typeof(T)),
            target, value).Compile();
    }

    private static Func<object, Guid> ReferenceGuid(
        MethodInfo reference,
        Type identityType,
        MethodInfo getGuid)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Variable(reference.ReturnType, "value");
        return Expression.Lambda<Func<object, Guid>>(
            Expression.Block(
                new[] { value },
                Expression.Assign(value, Expression.Call(Expression.Convert(target, reference.DeclaringType!), reference)),
                Expression.Condition(
                    Expression.AndAlso(
                        Expression.NotEqual(value, Expression.Constant(null, reference.ReturnType)),
                        Expression.TypeIs(value, identityType)),
                    Expression.Call(Expression.Convert(value, identityType), getGuid),
                    Expression.Constant(Guid.Empty))),
            target).Compile();
    }

    private static Action<object, int, object> ActionIntObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var index = Expression.Parameter(typeof(int), "index");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, int, object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, index,
                Expression.Convert(value, method.GetParameters()[1].ParameterType)),
            target, index, value).Compile();
    }

    private static Action<object, object> ActionObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)),
            target, value).Compile();
    }

    private static Action<object, int> ActionInt(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Action<object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target, value).Compile();
    }

    private static Action<object, bool> ActionBool(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(bool), "value");
        return Expression.Lambda<Action<object, bool>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target, value).Compile();
    }
}

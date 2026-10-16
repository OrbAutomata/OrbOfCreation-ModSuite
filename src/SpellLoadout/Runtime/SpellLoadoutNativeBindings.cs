using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

/// <summary>
/// Complete lifecycle binding set for equipped-spell removal and reordering. Reflection is confined
/// to construction; execution uses exact compiled delegates only.
/// </summary>
internal sealed class SpellLoadoutNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-manager.instance",
        "spell-manager.active-spells",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.list-value-action",
        "spell-workbench.spell-guid-container-action",
        "discovery-tree-offer.guid-container-value",
        "spell-loadout.spell-is-empty-action",
        "spell-loadout.spell-at-max-charges-action",
        "spell-loadout.spell-is-casting-action",
        "spell-loadout.spell-readying-cast-action",
        "spell-loadout.spell-current-charges-action",
        "spell-loadout.spell-max-charges-action",
        "spell-loadout.spell-cooldown-remaining-action",
        "spell-loadout.spell-reference-action",
        "spell-loadout.recipe-identity-action",
        "spell-loadout.manager-remove-spell-action",
        "spell-loadout.list-swap-positions-action",
        "spell-loadout.list-update-observable-action",
        "spell-loadout.owning-view-availability-action",
    };

    private SpellLoadoutNativeBindings(
        Type spellType,
        Func<object?> manager,
        Func<object, object> active,
        Func<object, IList> activeValues,
        Func<object, object?> spellGuid,
        Func<object, Guid> guidValue,
        Func<object, bool> isEmpty,
        Func<object, bool> atMaxCharges,
        Func<object, bool> isCasting,
        Func<object, bool> readyingCast,
        Func<object, int> currentCharges,
        Func<object, int> maximumCharges,
        Func<object, BigDouble> cooldownRemaining,
        Func<object, object?> spellRecipe,
        Func<object, Guid> recipeIdentity,
        Action<object, object> remove,
        Action<object, int, int> swap,
        Action<object> updateObservable,
        Type viewType,
        Func<object, bool> viewAvailable)
    {
        SpellType = spellType;
        ReadManager = manager;
        ReadActive = active;
        ReadActiveValues = activeValues;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
        IsEmpty = isEmpty;
        IsAtMaxCharges = atMaxCharges;
        IsCasting = isCasting;
        IsReadyingCast = readyingCast;
        ReadCurrentCharges = currentCharges;
        ReadMaximumCharges = maximumCharges;
        ReadCooldownRemaining = cooldownRemaining;
        ReadSpellRecipe = spellRecipe;
        ReadRecipeIdentity = recipeIdentity;
        Remove = remove;
        Swap = swap;
        UpdateObservable = updateObservable;
        ViewType = viewType;
        IsViewAvailable = viewAvailable;
    }

    internal Type SpellType { get; }

    /// <summary>
    /// The screen the loadout bar lives on. <c>ViewSO.IsAvailable()</c> is
    /// <c>prerequisites.Container.Check()</c> — the same question the game asks before it draws the
    /// tab.
    /// </summary>
    internal Type ViewType { get; }
    internal Func<object, bool> IsViewAvailable { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }
    internal Func<object, bool> IsEmpty { get; }

    /// <summary>
    /// The three reads <c>SpellManager.RemoveSpell</c> itself gates on: it returns without
    /// removing anything unless the spell is at full charges and neither casting nor readying a
    /// cast. <c>Spell.CanRemove()</c> is a different predicate that the game never calls.
    /// </summary>
    internal Func<object, bool> IsAtMaxCharges { get; }
    internal Func<object, bool> IsCasting { get; }
    internal Func<object, bool> IsReadyingCast { get; }

    /// <summary>The numbers the refusal quotes, read live beside the gate that refused.</summary>
    internal Func<object, int> ReadCurrentCharges { get; }
    internal Func<object, int> ReadMaximumCharges { get; }
    internal Func<object, BigDouble> ReadCooldownRemaining { get; }

    /// <summary>The recipe behind an equipped instance, which is the identity a caller can look up.</summary>
    internal Func<object, object?> ReadSpellRecipe { get; }
    internal Func<object, Guid> ReadRecipeIdentity { get; }
    internal Action<object, object> Remove { get; }
    internal Action<object, int, int> Swap { get; }
    internal Action<object> UpdateObservable { get; }

    internal static bool TryCreate(
        Func<string, Type?> resolveType,
        Func<string, bool> includeContract,
        out SpellLoadoutNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            for (var index = 0; index < ContractIds.Length; index++)
                Require(ContractIds[index], includeContract);

            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable.");

            var managerType = T("SpellManager");
            var spellListType = T("SpellListVariable");
            var spellType = T("Spell");
            var guidType = T("GuidContainer");
            var listType = typeof(List<>).MakeGenericType(spellType);

            var managerInstance = Field(managerType, "instance", managerType, isStatic: true);
            var active = Field(managerType, "activeSpells", spellListType, isStatic: false);
            var values = HierarchyField(spellListType, "value", listType);
            var spellGuid = Field(spellType, "guidContainer", guidType, isStatic: false);
            var guidValue = Method(guidType, "get_guid", typeof(Guid));
            var isEmpty = Method(spellType, "IsEmpty", typeof(bool));
            var atMaxCharges = Method(spellType, "IsAtMaxCharges", typeof(bool));
            var isCasting = Method(spellType, "IsCasting", typeof(bool));
            var readyingCast = Method(spellType, "IsReadyingCast", typeof(bool));
            var currentCharges = Method(spellType, "GetCurrSpellCharges", typeof(int));
            var maximumCharges = Method(spellType, "GetMaxSpellCharges", typeof(int));
            var cooldownRemaining = Method(
                spellType, "GetCooldownTimeRemaining", T("BigDouble"));
            var recipeType = T("SpellRecipeSO");
            var spellRecipe = Method(spellType, "get_reference", recipeType);
            // RecipeSO carries the audited IdScriptableObject identity method, the same one the
            // workbench binds to name a recipe.
            var recipeIdentity = Method(T("IdScriptableObject"), "GetGuid", typeof(Guid));
            var remove = Method(managerType, "RemoveSpell", typeof(void), spellType);
            var swap = HierarchyMethod(
                spellListType,
                "SwapPositions",
                typeof(void),
                typeof(int),
                typeof(int));
            var update = HierarchyMethod(spellListType, "UpdateObservable", typeof(void));
            var viewType = T("ViewSO");
            var viewAvailable = Method(viewType, "IsAvailable", typeof(bool));

            bindings = new SpellLoadoutNativeBindings(
                spellType,
                StaticObject(managerInstance),
                ObjectField(active),
                ListField(values),
                NullableObjectField(spellGuid),
                InstanceFunc<Guid>(guidValue),
                InstanceFunc<bool>(isEmpty),
                InstanceFunc<bool>(atMaxCharges),
                InstanceFunc<bool>(isCasting),
                InstanceFunc<bool>(readyingCast),
                InstanceFunc<int>(currentCharges),
                InstanceFunc<int>(maximumCharges),
                InstanceFunc<BigDouble>(cooldownRemaining),
                InstanceNullableObjectFunc(spellRecipe),
                InstanceFunc<Guid>(recipeIdentity),
                InstanceObjectAction(remove),
                InstanceValueValueAction<int, int>(swap),
                InstanceAction(update),
                viewType,
                InstanceFunc<bool>(viewAvailable));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete spell loadout binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id))
            throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static FieldInfo Field(Type type, string name, Type valueType, bool isStatic)
    {
        var field = type.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.FieldType != valueType || field.IsStatic != isStatic)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static FieldInfo HierarchyField(Type type, string name, Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, Instance | BindingFlags.DeclaredOnly);
            if (field is not null && field.FieldType == valueType) return field;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo Method(
        Type type,
        string name,
        Type result,
        params Type[] parameters)
    {
        var method = type.GetMethod(name, Instance, null, parameters, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo HierarchyMethod(
        Type type,
        string name,
        Type result,
        params Type[] parameters)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                Instance | BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method is not null && !method.IsStatic && method.ReturnType == result)
                return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(
            Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<object, object> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(object));
        return Expression.Lambda<Func<object, object>>(body, target).Compile();
    }

    private static Func<object, object?> NullableObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, target).Compile();
    }

    private static Func<object, IList> ListField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            typeof(IList));
        return Expression.Lambda<Func<object, IList>>(body, target).Compile();
    }

    private static Func<object, object?> InstanceNullableObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Convert(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            typeof(object));
        return Expression.Lambda<Func<object, object?>>(body, target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var body = Expression.Call(Expression.Convert(target, method.DeclaringType!), method);
        return Expression.Lambda<Func<object, T>>(body, target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
            target).Compile();
    }

    private static Action<object, object> InstanceObjectAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)),
            target,
            value).Compile();
    }

    private static Action<object, TFirst, TSecond> InstanceValueValueAction<TFirst, TSecond>(
        MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var first = Expression.Parameter(typeof(TFirst), "first");
        var second = Expression.Parameter(typeof(TSecond), "second");
        return Expression.Lambda<Action<object, TFirst, TSecond>>(
            Expression.Call(
                Expression.Convert(target, method.DeclaringType!),
                method,
                first,
                second),
            target,
            first,
            second).Compile();
    }
}

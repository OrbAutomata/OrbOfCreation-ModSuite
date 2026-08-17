using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OrbAutomata;

internal sealed class SpellWorkbenchNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "spell-manager.instance",
        "spell-workbench.glyph-list-type-action",
        "spell-workbench.spell-list-type-action",
        "spell-workbench.manager-selected-core-action",
        "spell-workbench.manager-selected-augments-action",
        "spell-manager.active-spells",
        "spell-workbench.manager-get-from-recipe",
        "spell-workbench.manager-get-usage-cost-action",
        "spell-workbench.manager-discover",
        "spell-workbench.manager-create",
        "spell-workbench.recipe-all-action",
        "id-scriptable-object.get-guid-action",
        "spell-recipe.is-discovered",
        "spell-workbench.recipe-get-glyphs-action",
        "spell-workbench.recipe-create-empty-action",
        "spell-workbench.recipe-selected-level-action",
        "spell-workbench.recipe-usage-requirements-action",
        "spell-workbench.recipe-can-discover",
        "spell-workbench.recipe-is-creatable",
        "spell-workbench.recipe-get-discover-cost-action",
        "resource-cost-list.has-enough",
        "resource-cost-list.get-entries",
        "resource-tuple.get-value",
        "resource-tuple.resource-concept",
        "spell-workbench.resource-has-amount-action",
        "spell-workbench.glyph-is-available",
        "spell-workbench.glyph-is-augment",
        "glyph.level",
        "spell-composition.glyph-maximum-uses-action",
        "spell-composition.glyph-meets-non-level-requirements-action",
        "spell-workbench.list-value-action",
        "spell-workbench.list-empty-action",
        "spell-workbench.list-add-action",
        "spell-workbench.list-set-value-action",
        "spell-workbench.list-set-stack-action",
        "spell-workbench.list-stacked-record-action",
        "spell-workbench.loadout-has-empty-action",
        "spell-workbench.spell-reference-action",
        "spell-workbench.spell-guid-container-action",
        "spell-workbench.spell-set-level-action",
        "spell-workbench.spell-is-unique-action",
        "spell-composition.spell-set-augments-action",
        "spell-composition.spell-get-augment-glyphs-action",
        "spell-composition.stacked-record-type-action",
        "spell-composition.stacked-record-construct-action",
        "spell-composition.stacked-record-set-action",
        "spell-composition.stacked-record-quantity-action",
        "spell-composition.stacked-record-items-action",
        "spell-composition.spell-augment-stack-action",
        "spell-workbench.list-max-action",
        "discovery-tree-offer.guid-container-value",
        "spell-workbench.owning-view-availability-action",
    };

    private SpellWorkbenchNativeBindings(Type recipeType, Type glyphType, Type spellType,
        Func<object?> manager, Func<IList> recipes,
        Func<object, object> core, Func<object, object> augments, Func<object, object> active,
        Func<object, IList> glyphValues, Func<object, IList> activeValues, Func<object, Guid> identity,
        Func<object, IList> recipeGlyphs, Func<object, bool> discovered,
        Func<object, bool> canDiscover, Func<object, bool> creatable,
        Func<object, object> discoverCost,
        Func<object, bool> hasEnough,
        Func<object, IList> costEntries, Func<object, BigDouble> costValue,
        Func<object, object?> costResource, Func<object, BigDouble, bool> resourceHasAmount,
        Func<IList> createGlyphList, Func<object, bool> glyphAvailable,
        Func<object, int> glyphLevel,
        Func<object, bool> glyphAugment, Func<object, int> glyphMaximumUsages,
        Action<object> empty,
        Action<object, object> add, Action<object, IList> setListValue,
        Action<object, object> setListStack, Func<object, object?> readListStack,
        Func<object, int> listMax, Func<object, bool> hasEmpty,
        Func<object, object, object?> resolveRecipe,
        Func<object, object> usageCost,
        Action<object> discover,
        Action<object, object> create, Func<object, int, object> createEmpty,
        Func<object, int> selectedLevel, Func<object, bool> usageRequirements,
        Action<object, int> setLevel, Action<object, object> setAugments,
        Func<object, bool> unique, Func<object, IList> spellAugments,
        Func<object, object?> spellAugmentStack,
        Func<IList, object, bool> meetsNonLevelRequirements,
        Func<object> createStackedRecord, Action<object, object, int> setStackedRecord,
        Func<object, object, int> readStackedQuantity, Func<object, IList> readStackedItems,
        Func<object, object?> spellReference,
        Func<object, object?> spellGuid, Func<object, Guid> guidValue,
        Type viewType, Func<object, bool> viewAvailable)
    {
        RecipeType = recipeType;
        GlyphType = glyphType;
        SpellType = spellType;
        ReadManager = manager;
        ReadRecipes = recipes;
        ReadCore = core;
        ReadAugments = augments;
        ReadActive = active;
        ReadGlyphValues = glyphValues;
        ReadActiveValues = activeValues;
        ReadIdentity = identity;
        ReadRecipeGlyphs = recipeGlyphs;
        IsDiscovered = discovered;
        CanDiscover = canDiscover;
        IsCreatable = creatable;
        GetDiscoverCost = discoverCost;
        HasEnough = hasEnough;
        ReadCostEntries = costEntries;
        ReadCostValue = costValue;
        ReadCostResource = costResource;
        HasResourceAmount = resourceHasAmount;
        CreateGlyphList = createGlyphList;
        IsGlyphAvailable = glyphAvailable;
        ReadGlyphLevel = glyphLevel;
        IsGlyphAugment = glyphAugment;
        GetGlyphMaximumUsages = glyphMaximumUsages;
        Empty = empty;
        Add = add;
        SetListValue = setListValue;
        SetListStack = setListStack;
        ReadListStack = readListStack;
        GetListMax = listMax;
        HasEmpty = hasEmpty;
        ResolveRecipe = resolveRecipe;
        GetUsageCost = usageCost;
        Discover = discover;
        Create = create;
        CreateEmptySpell = createEmpty;
        GetSelectedSpellLevel = selectedLevel;
        HasMetUsageRequirements = usageRequirements;
        SetSpellLevel = setLevel;
        SetSpellAugments = setAugments;
        IsUniqueSpell = unique;
        ReadSpellAugments = spellAugments;
        ReadSpellAugmentStack = spellAugmentStack;
        MeetsNonLevelRequirements = meetsNonLevelRequirements;
        CreateStackedRecord = createStackedRecord;
        SetStackedRecord = setStackedRecord;
        ReadStackedQuantity = readStackedQuantity;
        ReadStackedItems = readStackedItems;
        ReadSpellReference = spellReference;
        ReadSpellGuid = spellGuid;
        ReadGuidValue = guidValue;
        ViewType = viewType;
        IsViewAvailable = viewAvailable;
    }

    internal Type RecipeType { get; }

    /// <summary>
    /// The screen the load button lives on. <c>ViewSO.IsAvailable()</c> is
    /// <c>prerequisites.Container.Check()</c> — the same question the game asks before it draws the
    /// tab — so a locked screen is a fact the boundary reads rather than a state it infers.
    /// </summary>
    internal Type ViewType { get; }
    internal Func<object, bool> IsViewAvailable { get; }
    internal Type GlyphType { get; }
    internal Type SpellType { get; }
    internal Func<object?> ReadManager { get; }
    internal Func<IList> ReadRecipes { get; }
    internal Func<object, object> ReadCore { get; }
    internal Func<object, object> ReadAugments { get; }
    internal Func<object, object> ReadActive { get; }
    internal Func<object, IList> ReadGlyphValues { get; }
    internal Func<object, IList> ReadActiveValues { get; }
    internal Func<object, Guid> ReadIdentity { get; }
    internal Func<object, IList> ReadRecipeGlyphs { get; }
    internal Func<object, bool> IsDiscovered { get; }
    internal Func<object, bool> CanDiscover { get; }
    internal Func<object, bool> IsCreatable { get; }
    internal Func<object, object> GetDiscoverCost { get; }
    internal Func<object, bool> HasEnough { get; }
    internal Func<object, IList> ReadCostEntries { get; }
    internal Func<object, BigDouble> ReadCostValue { get; }
    internal Func<object, object?> ReadCostResource { get; }
    internal Func<object, BigDouble, bool> HasResourceAmount { get; }
    internal Func<IList> CreateGlyphList { get; }
    internal Func<object, bool> IsGlyphAvailable { get; }
    internal Func<object, int> ReadGlyphLevel { get; }
    internal Func<object, bool> IsGlyphAugment { get; }
    internal Func<object, int> GetGlyphMaximumUsages { get; }
    internal Action<object> Empty { get; }
    internal Action<object, object> Add { get; }

    /// <summary>
    /// The setter <c>SpellManager.InsertSpellRecipeGlyphs</c> itself stages a core with. Unlike
    /// <see cref="Add"/> it is gated only on the list being authored immutable, so a staged core
    /// either lands or the list refuses every write there is.
    /// </summary>
    internal Action<object, IList> SetListValue { get; }

    /// <summary>
    /// The stack write the game's own augment UI performs, which sets the multiplicity record
    /// <c>SpellManager.CreateRecipe</c> bakes from and the value list beside it in one call.
    /// </summary>
    internal Action<object, object> SetListStack { get; }

    /// <summary>
    /// The staged multiplicity record, or nothing on a list the author did not mark stackable —
    /// in which case the game bakes no augments at all from it.
    /// </summary>
    internal Func<object, object?> ReadListStack { get; }

    /// <summary>
    /// How many different items the list holds at once — the augment selection's own ceiling,
    /// <c>Max Spell Augment Slots</c>, or <c>int.MaxValue</c> on a list the author left unbounded.
    /// </summary>
    internal Func<object, int> GetListMax { get; }
    internal Func<object, bool> HasEmpty { get; }
    internal Func<object, object, object?> ResolveRecipe { get; }
    internal Func<object, object> GetUsageCost { get; }
    internal Action<object> Discover { get; }
    internal Action<object, object> Create { get; }
    internal Func<object, int, object> CreateEmptySpell { get; }
    internal Func<object, int> GetSelectedSpellLevel { get; }
    internal Func<object, bool> HasMetUsageRequirements { get; }
    internal Action<object, int> SetSpellLevel { get; }
    internal Action<object, object> SetSpellAugments { get; }
    internal Func<object, bool> IsUniqueSpell { get; }
    internal Func<object, IList> ReadSpellAugments { get; }

    /// <summary>
    /// The multiplicity record a loaded spell was baked from. <see cref="ReadSpellAugments"/>
    /// projects it to distinct items and loses the counts, so only this can answer whether a spell
    /// carries two of a glyph or one.
    /// </summary>
    internal Func<object, object?> ReadSpellAugmentStack { get; }
    internal Func<IList, object, bool> MeetsNonLevelRequirements { get; }
    internal Func<object> CreateStackedRecord { get; }
    internal Action<object, object, int> SetStackedRecord { get; }
    internal Func<object, object, int> ReadStackedQuantity { get; }
    internal Func<object, IList> ReadStackedItems { get; }
    internal Func<object, object?> ReadSpellReference { get; }
    internal Func<object, object?> ReadSpellGuid { get; }
    internal Func<object, Guid> ReadGuidValue { get; }

    internal static bool TryCreate(Func<string, Type?> resolveType,
        Func<string, bool> includeContract, out SpellWorkbenchNativeBindings? bindings,
        out string reason)
    {
        bindings = null;
        try
        {
            for (var index = 0; index < ContractIds.Length; index++)
                Require(ContractIds[index], includeContract);

            Type T(string name)
            {
                return resolveType(name) ?? throw new InvalidOperationException(name + " was unavailable.");
            }

            var managerType = T("SpellManager");
            var glyphListType = T("GlyphListVariable");
            var spellListType = T("SpellListVariable");
            var recipeType = T("SpellRecipeSO");
            var identityType = T("IdScriptableObject");
            var glyphType = T("GlyphSO");
            var costType = T("ResourceCostList");
            var costTupleType = T("ResourceTuple");
            var resourceType = T("ResourceSO");
            var bigDoubleType = T("BigDouble");
            var spellType = T("Spell");
            var guidType = T("GuidContainer");
            var openStackedType = T("Stacked.StackedIdRecord`1");
            var glyphList = typeof(List<>).MakeGenericType(glyphType);
            var costTupleList = typeof(List<>).MakeGenericType(costTupleType);
            var recipeList = typeof(List<>).MakeGenericType(recipeType);
            var spellList = typeof(List<>).MakeGenericType(spellType);
            var stackedType = openStackedType.MakeGenericType(glyphType);

            var managerInstance = Field(managerType, "instance", managerType, true);
            var selectedCore = Field(managerType, "selectedCoreGlyphs", glyphListType, false);
            var selectedAugments = Field(managerType, "selectedAugmentGlyphs", glyphListType, false);
            var active = Field(managerType, "activeSpells", spellListType, false);
            var allRecipes = Field(recipeType, "All", recipeList, true);
            // RecipeSO and GlyphSO share the audited IdScriptableObject identity method. Bind the
            // declaring contract once so the resulting delegate is valid for both concrete kinds.
            var identity = Method(identityType, "GetGuid", typeof(Guid));
            var viewType = T("ViewSO");
            var viewAvailable = Method(viewType, "IsAvailable", typeof(bool));
            var recipeGlyphs = Method(recipeType, "GetGlyphRecipe", glyphList);
            var discovered = Method(recipeType, "IsDiscovered", typeof(bool));
            var canDiscover = Method(recipeType, "CanDiscover", typeof(bool));
            var creatable = Method(recipeType, "IsCreatable", typeof(bool));
            var discoverCost = Method(recipeType, "GetDiscoverCost", costType);
            var createEmpty = Method(recipeType, "CreateEmpty", spellType, typeof(int));
            var selectedLevel = Method(recipeType, "GetSelectedSpellLevel", typeof(int));
            var usageRequirements = Method(recipeType, "HasMetUsageRequirements", typeof(bool));
            var enough = Method(costType, "HasEnough", typeof(bool));
            var costEntries = Method(costType, "GetEntries", costTupleList);
            var costValue = Method(costTupleType, "GetValue", bigDoubleType);
            var costResource = Field(costTupleType, "resource", resourceType, false);
            var hasResourceAmount = Method(resourceType, "HasAmount", typeof(bool), bigDoubleType);
            var glyphAvailable = Method(glyphType, "IsAvailable", typeof(bool));
            var glyphAugment = Method(glyphType, "IsSpellAugment", typeof(bool));
            var glyphLevel = Field(glyphType, "level", typeof(int), false);
            var glyphMaximumUsages = Method(glyphType, "GetMaxUsages", typeof(int));
            var listValue = HierarchyField(glyphListType, "value", glyphList);
            var activeValue = HierarchyField(spellListType, "value", spellList);
            var empty = HierarchyMethod(glyphListType, "Empty", typeof(void));
            var add = HierarchyMethod(glyphListType, "Add", typeof(void), glyphType);
            var setListValue = HierarchyMethod(glyphListType, "SetValue", typeof(void), glyphList);
            var setListStack = HierarchyMethod(glyphListType, "SetStack", typeof(void), stackedType);
            var readListStack = HierarchyMethod(glyphListType, "GetStackedRecord", stackedType);
            var listMax = HierarchyMethod(glyphListType, "GetMax", typeof(int));
            var hasEmpty = HierarchyMethod(spellListType, "HasEmptySpot", typeof(bool));
            var resolve = Method(managerType, "GetSpellFromRecipe", recipeType, glyphList);
            var usageCost = StaticMethod(managerType, "GetUsageCostOfSpell", costType, spellType);
            var discover = Method(managerType, "DiscoverSpell", typeof(void));
            var create = Method(managerType, "CreateRecipe", typeof(void), recipeType);
            var setLevel = Method(spellType, "SetLevel", typeof(void), typeof(int));
            var setAugments = Method(spellType, "SetAugmentGlyphs", typeof(void), stackedType);
            var unique = Method(spellType, "IsUniqueSpell", typeof(bool));
            var spellAugments = Method(spellType, "GetAugmentGlyphs", glyphList);
            var spellAugmentStack = Field(spellType, "augmentGlyphRefs", stackedType, false);
            var meetsNonLevel = StaticMethod(
                glyphType, "MeetsNonLvRequirements", typeof(bool), glyphList, spellType);
            var stackedConstructor = stackedType.GetConstructor(Type.EmptyTypes) ??
                throw new InvalidOperationException(stackedType.Name + ".ctor was unavailable.");
            var setStacked = HierarchyMethod(stackedType, "Set", typeof(void), glyphType, typeof(int));
            var stackedQuantity = HierarchyMethod(stackedType, "GetQuantity", typeof(int), glyphType);
            var stackedItems = HierarchyMethod(stackedType, "GetItems", glyphList);
            var spellReference = Method(spellType, "get_reference", recipeType);
            var spellGuid = Field(spellType, "guidContainer", guidType, false);
            var guidValue = Method(guidType, "get_guid", typeof(Guid));

            bindings = new SpellWorkbenchNativeBindings(
                recipeType, glyphType, spellType, StaticObject(managerInstance), StaticList(allRecipes),
                ObjectField(selectedCore), ObjectField(selectedAugments), ObjectField(active),
                ListField(listValue), ListField(activeValue), InstanceFunc<Guid>(identity),
                InstanceList(recipeGlyphs), InstanceFunc<bool>(discovered),
                InstanceFunc<bool>(canDiscover), InstanceFunc<bool>(creatable),
                InstanceObject(discoverCost), InstanceFunc<bool>(enough),
                InstanceList(costEntries), InstanceFunc<BigDouble>(costValue),
                ObjectNullableField(costResource), InstanceValueFunc<BigDouble, bool>(hasResourceAmount),
                NewList(glyphList),
                InstanceFunc<bool>(glyphAvailable), IntField(glyphLevel),
                InstanceFunc<bool>(glyphAugment),
                InstanceFunc<int>(glyphMaximumUsages),
                InstanceAction(empty), InstanceObjectAction(add),
                InstanceListAction(setListValue), InstanceObjectAction(setListStack),
                InstanceNullableObject(readListStack), InstanceFunc<int>(listMax),
                InstanceFunc<bool>(hasEmpty),
                InstanceObjectObject(resolve),
                StaticObjectObject(usageCost), InstanceAction(discover), InstanceObjectAction(create),
                InstanceIntObject(createEmpty), InstanceFunc<int>(selectedLevel),
                InstanceFunc<bool>(usageRequirements), InstanceIntAction(setLevel),
                InstanceObjectAction(setAugments), InstanceFunc<bool>(unique),
                InstanceList(spellAugments), ObjectNullableField(spellAugmentStack),
                StaticListObjectBoolean(meetsNonLevel),
                NewObject(stackedConstructor), InstanceObjectIntAction(setStacked),
                InstanceObjectFunc<int>(stackedQuantity), InstanceList(stackedItems),
                InstanceNullableObject(spellReference), ObjectNullableField(spellGuid),
                InstanceFunc<Guid>(guidValue),
                viewType,
                InstanceFunc<bool>(viewAvailable));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or AmbiguousMatchException)
        {
            reason = "The complete spell workbench binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static void Require(string id, Func<string, bool> include)
    {
        if (!include(id)) throw new InvalidOperationException("Required contract " + id + " was withheld.");
    }

    private static FieldInfo Field(Type type, string name, Type valueType, bool isStatic)
    {
        var field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic |
            (isStatic ? BindingFlags.Static : BindingFlags.Instance));
        if (field is null || field.FieldType != valueType || field.IsStatic != isStatic)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return field;
    }

    private static FieldInfo HierarchyField(Type type, string name, Type valueType)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (field is not null && field.FieldType == valueType) return field;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo Method(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic, null, arguments, null);
        if (method is null || method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static MethodInfo HierarchyMethod(Type type, string name, Type result, params Type[] arguments)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
                BindingFlags.NonPublic | BindingFlags.DeclaredOnly, null, arguments, null);
            if (method is not null && !method.IsStatic && method.ReturnType == result) return method;
        }
        throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
    }

    private static MethodInfo StaticMethod(Type type, string name, Type result, params Type[] arguments)
    {
        var method = type.GetMethod(name, BindingFlags.Static | BindingFlags.Public |
            BindingFlags.NonPublic, null, arguments, null);
        if (method is null || !method.IsStatic || method.ReturnType != result)
            throw new InvalidOperationException(type.Name + "." + name + " was unavailable.");
        return method;
    }

    private static Func<object?> StaticObject(FieldInfo field) =>
        Expression.Lambda<Func<object?>>(Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();

    private static Func<IList> StaticList(FieldInfo field) =>
        Expression.Lambda<Func<IList>>(Expression.Convert(Expression.Field(null, field), typeof(IList))).Compile();

    private static Func<IList> NewList(Type type) =>
        Expression.Lambda<Func<IList>>(
            Expression.Convert(Expression.New(type), typeof(IList))).Compile();

    private static Func<object> NewObject(ConstructorInfo constructor) =>
        Expression.Lambda<Func<object>>(
            Expression.Convert(Expression.New(constructor), typeof(object))).Compile();

    private static Func<object, object> ObjectField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }

    private static Func<object, int> IntField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, int>>(
            Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
            target).Compile();
    }

    private static Func<object, object?> ObjectNullableField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Field(Expression.Convert(target, field.DeclaringType!), field), typeof(object)), target).Compile();
    }

    private static Func<object, IList> ListField(FieldInfo field)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(
                Expression.Field(Expression.Convert(target, field.DeclaringType!), field),
                typeof(IList)), target).Compile();
    }

    private static Func<object, T> InstanceFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, T>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(T)), target).Compile();
    }

    private static Func<object, TValue, TResult> InstanceValueFunc<TValue, TResult>(
        MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(TValue), "value");
        return Expression.Lambda<Func<object, TValue, TResult>>(
            Expression.Convert(Expression.Call(
                Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)),
                typeof(TResult)),
            target, value).Compile();
    }

    private static Func<object, object> InstanceObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }

    private static Func<object, int, object> InstanceIntObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Func<object, int, object>>(
            Expression.Convert(Expression.Call(
                Expression.Convert(target, method.DeclaringType!), method, value), typeof(object)),
            target, value).Compile();
    }

    private static Action<object, int> InstanceIntAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Action<object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value),
            target, value).Compile();
    }

    private static Func<object, object?> InstanceNullableObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method), typeof(object)), target).Compile();
    }

    private static Func<object, IList> InstanceList(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList>>(
            Expression.Convert(
                Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)), target).Compile();
    }

    private static Action<object> InstanceAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object, object> InstanceObjectAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), target, value).Compile();
    }

    private static Action<object, IList> InstanceListAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var values = Expression.Parameter(typeof(IList), "values");
        return Expression.Lambda<Action<object, IList>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(values, method.GetParameters()[0].ParameterType)),
            target, values).Compile();
    }

    private static Func<object, object, T> InstanceObjectFunc<T>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, T>>(
            Expression.Convert(Expression.Call(
                Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)),
                typeof(T)),
            target, value).Compile();
    }

    private static Func<object, object, object?> InstanceObjectObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), typeof(object)), target, value).Compile();
    }

    private static Action<object, object, int> InstanceObjectIntAction(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var item = Expression.Parameter(typeof(object), "item");
        var quantity = Expression.Parameter(typeof(int), "quantity");
        return Expression.Lambda<Action<object, object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(item, method.GetParameters()[0].ParameterType), quantity),
            target, item, quantity).Compile();
    }

    private static Func<object, object> StaticObjectObject(MethodInfo method)
    {
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object>>(
            Expression.Convert(Expression.Call(
                method, Expression.Convert(value, method.GetParameters()[0].ParameterType)),
                typeof(object)), value).Compile();
    }

    private static Func<IList, object, bool> StaticListObjectBoolean(MethodInfo method)
    {
        var values = Expression.Parameter(typeof(IList), "values");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<IList, object, bool>>(
            Expression.Call(method,
                Expression.Convert(values, method.GetParameters()[0].ParameterType),
                Expression.Convert(value, method.GetParameters()[1].ParameterType)),
            values, value).Compile();
    }
}

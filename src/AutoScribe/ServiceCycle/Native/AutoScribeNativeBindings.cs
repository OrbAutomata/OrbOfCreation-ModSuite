using System;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// One complete lifecycle binding set for the Scribe re-drive. Every execution member, field type,
/// signature, return type, staticness, and constructor is proven before the GameAction can run.
/// </summary>
internal sealed class AutoScribeNativeBindings
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private AutoScribeNativeBindings(
        Type recipeType,
        Type recipeTypeType,
        Type recipeListType,
        Type instanceType,
        Type instanceListType,
        Type consumableType,
        Type enchantmentType,
        Type resourceCostType,
        Type bigDoubleType,
        Type instantBlockType,
        Type instantScriptType,
        Type gainType,
        Type requestType,
        Type optionsType,
        Type selectionType,
        Type targetType,
        Type targetableType,
        Type enchantScriptType,
        Type scalingType,
        ConstructorInfo constructInstance,
        FieldInfo recipeListValue,
        FieldInfo instanceListValue,
        FieldInfo craftingTypes,
        FieldInfo completeEffects,
        FieldInfo useQuantityAsLevel,
        FieldInfo isLevelType,
        FieldInfo maxStartingLevel,
        FieldInfo effectScripts,
        FieldInfo gainConsumable,
        FieldInfo onUseEffects,
        FieldInfo targetOptions,
        FieldInfo enchantScriptEnchantment,
        MethodInfo identity,
        MethodInfo consumableIdentity,
        MethodInfo enchantmentIdentity,
        MethodInfo recipeVisible,
        MethodInfo recipeCanBuyAt,
        MethodInfo recipeTotalCost,
        MethodInfo recipeMainType,
        MethodInfo recipePurchase,
        MethodInfo costHasEnough,
        MethodInfo queueHasRoom,
        MethodInfo queueAdd,
        MethodInfo instanceRecipe,
        MethodInfo instanceQuantity,
        MethodInfo instanceExpired,
        MethodInfo instanceInitiate,
        MethodInfo instanceInstantCheck,
        MethodInfo instanceInstant,
        MethodInfo scalingBasic,
        MethodInfo getTargeting,
        MethodInfo getRandomList)
    {
        RecipeType = recipeType;
        RecipeTypeType = recipeTypeType;
        RecipeListType = recipeListType;
        InstanceType = instanceType;
        InstanceListType = instanceListType;
        ConsumableType = consumableType;
        EnchantmentType = enchantmentType;
        ResourceCostType = resourceCostType;
        BigDoubleType = bigDoubleType;
        InstantBlockType = instantBlockType;
        InstantScriptType = instantScriptType;
        GainType = gainType;
        RequestType = requestType;
        OptionsType = optionsType;
        SelectionType = selectionType;
        TargetType = targetType;
        TargetableType = targetableType;
        EnchantScriptType = enchantScriptType;
        ScalingType = scalingType;
        ConstructInstance = constructInstance;
        RecipeListValue = recipeListValue;
        InstanceListValue = instanceListValue;
        CraftingTypes = craftingTypes;
        CompleteEffects = completeEffects;
        UseQuantityAsLevel = useQuantityAsLevel;
        IsLevelType = isLevelType;
        MaxStartingLevel = maxStartingLevel;
        EffectScripts = effectScripts;
        GainConsumable = gainConsumable;
        OnUseEffects = onUseEffects;
        TargetOptions = targetOptions;
        EnchantScriptEnchantment = enchantScriptEnchantment;
        Identity = identity;
        ConsumableIdentity = consumableIdentity;
        EnchantmentIdentity = enchantmentIdentity;
        RecipeVisible = recipeVisible;
        RecipeCanBuyAt = recipeCanBuyAt;
        RecipeTotalCost = recipeTotalCost;
        RecipeMainType = recipeMainType;
        RecipePurchase = recipePurchase;
        CostHasEnough = costHasEnough;
        QueueHasRoom = queueHasRoom;
        QueueAdd = queueAdd;
        InstanceRecipe = instanceRecipe;
        InstanceQuantity = instanceQuantity;
        InstanceExpired = instanceExpired;
        InstanceInitiate = instanceInitiate;
        InstanceInstantCheck = instanceInstantCheck;
        InstanceInstant = instanceInstant;
        ScalingBasic = scalingBasic;
        GetTargeting = getTargeting;
        GetRandomList = getRandomList;
    }

    internal Type RecipeType { get; }
    internal Type RecipeTypeType { get; }
    internal Type RecipeListType { get; }
    internal Type InstanceType { get; }
    internal Type InstanceListType { get; }
    internal Type ConsumableType { get; }
    internal Type EnchantmentType { get; }
    internal Type ResourceCostType { get; }
    internal Type BigDoubleType { get; }
    internal Type InstantBlockType { get; }
    internal Type InstantScriptType { get; }
    internal Type GainType { get; }
    internal Type RequestType { get; }
    internal Type OptionsType { get; }
    internal Type SelectionType { get; }
    internal Type TargetType { get; }
    internal Type TargetableType { get; }
    internal Type EnchantScriptType { get; }
    internal Type ScalingType { get; }
    internal ConstructorInfo ConstructInstance { get; }
    internal FieldInfo RecipeListValue { get; }
    internal FieldInfo InstanceListValue { get; }
    internal FieldInfo CraftingTypes { get; }
    internal FieldInfo CompleteEffects { get; }
    internal FieldInfo UseQuantityAsLevel { get; }
    internal FieldInfo IsLevelType { get; }
    internal FieldInfo MaxStartingLevel { get; }
    internal FieldInfo EffectScripts { get; }
    internal FieldInfo GainConsumable { get; }
    internal FieldInfo OnUseEffects { get; }
    internal FieldInfo TargetOptions { get; }
    internal FieldInfo EnchantScriptEnchantment { get; }
    internal MethodInfo Identity { get; }
    internal MethodInfo ConsumableIdentity { get; }
    internal MethodInfo EnchantmentIdentity { get; }
    internal MethodInfo RecipeVisible { get; }
    internal MethodInfo RecipeCanBuyAt { get; }
    internal MethodInfo RecipeTotalCost { get; }
    internal MethodInfo RecipeMainType { get; }
    internal MethodInfo RecipePurchase { get; }
    internal MethodInfo CostHasEnough { get; }
    internal MethodInfo QueueHasRoom { get; }
    internal MethodInfo QueueAdd { get; }
    internal MethodInfo InstanceRecipe { get; }
    internal MethodInfo InstanceQuantity { get; }
    internal MethodInfo InstanceExpired { get; }
    internal MethodInfo InstanceInitiate { get; }
    internal MethodInfo InstanceInstantCheck { get; }
    internal MethodInfo InstanceInstant { get; }
    internal MethodInfo ScalingBasic { get; }
    internal MethodInfo GetTargeting { get; }
    internal MethodInfo GetRandomList { get; }

    internal static bool TryCreate(out AutoScribeNativeBindings? bindings, out string reason)
    {
        bindings = null;
        try
        {
            var recipe = Type("CraftingRecipeSO");
            var recipeType = Type("CraftingRecipeTypeSO");
            var recipeList = Type("CraftingRecipeListVariable");
            var instance = Type("CraftingInstance");
            var instanceList = Type("CraftingInstanceListVariable");
            var consumable = Type("ConsumableSO");
            var enchantment = Type("EnchantmentSO");
            var cost = Type("ResourceCostList");
            var big = Type("BigDouble");
            var block = Type("InstantEffectBlock");
            var script = Type("IInstantEffectScript");
            var gain = Type("ConsumableSO+ConsumableGainEffect");
            var request = Type("RequestTargetEffectScript");
            var options = Type("Targeting.TargetSelectOptions");
            var selection = Type("Targeting.BaseTargetSelection");
            var target = Type("Targeting.TargetStructure");
            var targetable = Type("Targeting.ITargetable");
            var enchantScript = Type("EnchantmentSO+EnchantItemScript");
            var scaling = Type("ScalingInfo");

            bindings = new AutoScribeNativeBindings(
                recipe,
                recipeType,
                recipeList,
                instance,
                instanceList,
                consumable,
                enchantment,
                cost,
                big,
                block,
                script,
                gain,
                request,
                options,
                selection,
                target,
                targetable,
                enchantScript,
                scaling,
                Constructor(instance, recipe, big),
                GenericListValue(recipeList, recipe),
                GenericListValue(instanceList, instance),
                CollectionField(recipe, "craftingTypes", recipeType),
                CollectionField(recipe, "completeEffects", block),
                Field(recipe, "useQuantityAsLevel", typeof(bool)),
                Field(recipeType, "isLevelType", typeof(bool)),
                Field(recipeType, "maxStartingLevel", typeof(int)),
                CollectionField(block, "effectScripts", script),
                Field(gain, "consumable", consumable),
                CollectionField(consumable, "onUseEffects", block),
                Field(request, "targetOptions", options),
                Field(enchantScript, "enchantment", enchantment),
                MethodFromHierarchy(recipe, "GetGuid", typeof(Guid)),
                MethodFromHierarchy(consumable, "GetGuid", typeof(Guid)),
                MethodFromHierarchy(enchantment, "GetGuid", typeof(Guid)),
                Method(recipe, "IsVisible", typeof(bool)),
                Method(recipe, "CanBuyAt", typeof(bool), big),
                Method(recipe, "GetTotalCost", cost, big, big),
                Method(recipe, "GetMainType", recipeType),
                Method(recipe, "PurchaseQuantity", typeof(void), big, big),
                Method(cost, "HasEnough", typeof(bool)),
                MethodFromHierarchy(instanceList, "HasEmptySpot", typeof(bool)),
                MethodFromHierarchy(instanceList, "Add", typeof(void), instance),
                MethodFromHierarchy(instance, "GetGuidReference", typeof(Guid)),
                Method(instance, "GetQuantity", big),
                Method(instance, "IsExpired", typeof(bool)),
                Method(instance, "Initiate", typeof(void)),
                Method(instance, "CheckInstantCraft", typeof(bool)),
                Method(instance, "InstantCraft", typeof(void)),
                StaticMethod(scaling, "Basic", scaling, big),
                Method(options, "GetTargeting", selection),
                Method(
                    target,
                    "GetRandomList",
                    typeof(List<>).MakeGenericType(targetable),
                    scaling));
            reason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or AmbiguousMatchException)
        {
            reason = "The complete Auto Scribe native binding set is unavailable: " + ex.Message;
            return false;
        }
    }

    private static Type Type(string name) =>
        ReflectionUtil.FindLoadedType(name) ??
        throw new InvalidOperationException(name + " was unavailable.");

    private static ConstructorInfo Constructor(Type type, params Type[] parameters) =>
        type.GetConstructor(Instance, null, parameters, null) ??
        throw new InvalidOperationException(
            $"{type.Name}({string.Join(",", Array.ConvertAll(parameters, p => p.Name))}) " +
            "constructor was unavailable.");

    private static FieldInfo GenericListValue(Type listType, Type element)
    {
        for (var current = listType; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                "value",
                Instance | BindingFlags.DeclaredOnly);
            if (field?.FieldType == typeof(List<>).MakeGenericType(element)) return field;
        }
        throw new InvalidOperationException(
            $"{listType.Name}.value : List<{element.Name}> was unavailable.");
    }

    private static FieldInfo CollectionField(Type type, string name, Type element)
    {
        var field = type.GetField(name, Instance);
        if (field is null || CollectionElement(field.FieldType) != element || field.IsStatic)
            throw new InvalidOperationException(
                $"{type.Name}.{name} collection of {element.Name} was unavailable.");
        return field;
    }

    private static FieldInfo Field(Type type, string name, Type fieldType)
    {
        var field = type.GetField(name, Instance);
        if (field is null || field.FieldType != fieldType || field.IsStatic)
            throw new InvalidOperationException(
                $"{type.Name}.{name} : {fieldType.Name} was unavailable.");
        return field;
    }

    private static MethodInfo StaticMethod(
        Type type,
        string name,
        Type returnType,
        params Type[] parameters) =>
        ExactMethod(type, name, returnType, Static, parameters);

    private static MethodInfo Method(
        Type type,
        string name,
        Type returnType,
        params Type[] parameters) =>
        ExactMethod(type, name, returnType, Instance, parameters);

    private static MethodInfo ExactMethod(
        Type type,
        string name,
        Type returnType,
        BindingFlags flags,
        params Type[] parameters)
    {
        var method = type.GetMethod(name, flags, null, parameters, null);
        if (method is null || method.ReturnType != returnType ||
            method.IsStatic != flags.HasFlag(BindingFlags.Static))
            throw new InvalidOperationException(
                $"{type.Name}.{name}({string.Join(",", Array.ConvertAll(parameters, p => p.Name))}) " +
                $": {returnType.Name} was unavailable.");
        return method;
    }

    private static MethodInfo MethodFromHierarchy(
        Type type,
        string name,
        Type returnType,
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
            if (method?.ReturnType == returnType && !method.IsStatic) return method;
        }
        throw new InvalidOperationException(
            $"{type.Name}.{name} : {returnType.Name} was unavailable.");
    }

    private static Type? CollectionElement(Type type)
    {
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return type.GetGenericArguments()[0];
        foreach (var candidate in type.GetInterfaces())
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return candidate.GetGenericArguments()[0];
        return null;
    }
}

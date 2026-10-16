using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

/// <summary>Validates the picker's complete native read set before enumerating one item.</summary>
internal sealed class AutoItemsTemporaryItemCatalogBindings
{
    private const BindingFlags AnyInstance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;
    private const BindingFlags PublicStatic = BindingFlags.Static | BindingFlags.Public;

    private AutoItemsTemporaryItemCatalogBindings(
        Type consumableType,
        Type familyType,
        IEnumerable entries,
        FieldInfo visible,
        FieldInfo quantity,
        FieldInfo families,
        MethodInfo itemGuid,
        MethodInfo familyGuid,
        MethodInfo getIcon)
    {
        ConsumableType = consumableType;
        FamilyType = familyType;
        Entries = entries;
        Visible = visible;
        Quantity = quantity;
        Families = families;
        ItemGuid = itemGuid;
        FamilyGuid = familyGuid;
        GetIcon = getIcon;
    }

    internal Type ConsumableType { get; }
    internal Type FamilyType { get; }
    internal IEnumerable Entries { get; }
    internal FieldInfo Visible { get; }
    internal FieldInfo Quantity { get; }
    internal FieldInfo Families { get; }
    internal MethodInfo ItemGuid { get; }
    internal MethodInfo FamilyGuid { get; }
    internal MethodInfo GetIcon { get; }

    internal static bool TryCreate(
        out AutoItemsTemporaryItemCatalogBindings? bindings,
        out string reason)
    {
        bindings = null;
        reason = "The exact native temporary-item picker contracts are unavailable.";

        var consumable = ReflectionUtil.FindLoadedType("ConsumableSO");
        var family = ReflectionUtil.FindLoadedType("ConsumableTypeSO");
        if (consumable is null || family is null)
        {
            reason = "ConsumableSO or ConsumableTypeSO is not loaded.";
            return false;
        }

        var all = consumable.GetField("All", PublicStatic);
        var visible = consumable.GetField("visible", AnyInstance);
        var quantity = consumable.GetField("quantity", AnyInstance);
        var families = consumable.GetField("consumableTypes", AnyInstance);
        var itemGuid = ExactMethod(consumable, "GetGuid", typeof(Guid));
        var familyGuid = ExactMethod(family, "GetGuid", typeof(Guid));
        var getIcon = ExactMethod(consumable, "GetIcon", typeof(Sprite));

        if (all?.GetValue(null) is not IEnumerable entries ||
            CollectionElementType(all.FieldType) != consumable)
        {
            reason = "ConsumableSO.All is not the exact native consumable registry.";
            return false;
        }
        if (visible?.FieldType != typeof(bool))
        {
            reason = "ConsumableSO.visible is not Boolean.";
            return false;
        }
        if (quantity?.FieldType != typeof(int))
        {
            reason = "ConsumableSO.quantity is not Int32.";
            return false;
        }
        if (families is null || CollectionElementType(families.FieldType) != family)
        {
            reason = "ConsumableSO.consumableTypes is not the exact native family list.";
            return false;
        }
        if (itemGuid is null || familyGuid is null)
        {
            reason = "The item or family GetGuid() contract is unavailable.";
            return false;
        }
        if (getIcon is null)
        {
            reason = "ConsumableSO.GetIcon() : Sprite is unavailable.";
            return false;
        }

        bindings = new AutoItemsTemporaryItemCatalogBindings(
            consumable,
            family,
            entries,
            visible,
            quantity,
            families,
            itemGuid,
            familyGuid,
            getIcon);
        reason = string.Empty;
        return true;
    }

    private static MethodInfo? ExactMethod(Type type, string name, Type returnType)
    {
        var method = type.GetMethod(name, PublicInstance, null, Type.EmptyTypes, null);
        return method is not null && !method.IsStatic && method.ReturnType == returnType
            ? method
            : null;
    }

    private static Type? CollectionElementType(Type type)
    {
        if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            return type.GetGenericArguments()[0];
        foreach (var candidate in type.GetInterfaces())
        {
            if (candidate.IsGenericType &&
                candidate.GetGenericTypeDefinition() == typeof(System.Collections.Generic.IEnumerable<>))
            {
                return candidate.GetGenericArguments()[0];
            }
        }
        return null;
    }
}

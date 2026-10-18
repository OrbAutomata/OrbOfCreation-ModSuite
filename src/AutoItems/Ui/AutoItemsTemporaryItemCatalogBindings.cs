using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;
using UnityEngine;

namespace OrbAutomata;

/// <summary>Validates the picker's complete native read set before enumerating one item.</summary>
internal sealed class AutoItemsTemporaryItemCatalogBindings
{
    internal static readonly string[] ContractIds =
    {
        "consumable.type",
        "consumable-type.type",
        "consumable.all",
        "consumable.visible",
        "consumable.quantity",
        "consumable.consumable-types",
        "id-scriptable-object.get-guid",
        "tooltipable-object.get-icon",
    };

    private AutoItemsTemporaryItemCatalogBindings(
        Type consumableType,
        Type familyType,
        IEnumerable entries,
        Func<object, bool> visible,
        Func<object, int> quantity,
        Func<object, IEnumerable?> families,
        Func<object, Guid> itemGuid,
        Func<object, Guid> familyGuid,
        Func<object, Sprite?> getIcon)
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
    internal Func<object, bool> Visible { get; }
    internal Func<object, int> Quantity { get; }
    internal Func<object, IEnumerable?> Families { get; }
    internal Func<object, Guid> ItemGuid { get; }
    internal Func<object, Guid> FamilyGuid { get; }
    internal Func<object, Sprite?> GetIcon { get; }

    internal static bool TryCreate(
        out AutoItemsTemporaryItemCatalogBindings? bindings,
        out string reason) =>
        NativeBindingSet.TryBind("temporary-item picker", ContractIds, null, null, set =>
        {
            var consumable = set.Type("consumable.type", "ConsumableSO");
            var family = set.Type("consumable-type.type", "ConsumableTypeSO");
            var all = set.StaticField<Func<IEnumerable?>>(
                "consumable.all", consumable, "All", typeof(List<>).MakeGenericType(consumable));
            var entries = all() ?? throw new NativeBindingException(
                "consumable.all: ConsumableSO.All is not the exact native consumable registry.");

            return new AutoItemsTemporaryItemCatalogBindings(
                consumable,
                family,
                entries,
                set.Field<Func<object, bool>>("consumable.visible", consumable, "visible", typeof(bool)),
                set.Field<Func<object, int>>("consumable.quantity", consumable, "quantity", typeof(int)),
                set.Field<Func<object, IEnumerable?>>(
                    "consumable.consumable-types",
                    consumable,
                    "consumableTypes",
                    typeof(List<>).MakeGenericType(family)),
                set.StableUuid("id-scriptable-object.get-guid", consumable),
                set.StableUuid("id-scriptable-object.get-guid", family),
                set.Call<Func<object, Sprite?>>(
                    "tooltipable-object.get-icon", consumable, "GetIcon", typeof(Sprite)));
        }, out bindings, out reason);
}

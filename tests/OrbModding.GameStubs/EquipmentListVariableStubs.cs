using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class EquipmentListVariable : StackableListVariable<EquipmentSO>
{
    public int GetTypesEquipped(EquipmentTypeSO equipmentType) =>
        value.Count(item => ReferenceEquals(item.equipmentType, equipmentType));

    /// <summary>
    /// The base write, plus the equip bookkeeping this stub does for the game's own equip side of
    /// the same call. The shipped type declares neither member; both come from the stackable base.
    /// </summary>
    public override void SetStack(Stacked.StackedIdRecord<EquipmentSO> record)
    {
        base.SetStack(record);
        foreach (var item in EquipmentSO.All)
            item.Equip(itemStack.GetQuantity(item));
    }
}

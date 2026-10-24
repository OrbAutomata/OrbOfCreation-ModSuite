using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class EquipmentManager
{
    public static EquipmentManager instance = new EquipmentManager();
    public EquipmentListVariable allEquipment = new EquipmentListVariable { Maximum = int.MaxValue };
    public EquipmentListVariable equippedEquipment = new EquipmentListVariable();
    public bool SuppressMutation { get; set; }
    public Exception? ThrowBeforeMutation { get; set; }
    public bool ThrowAfterMutation { get; set; }
    public bool ThrowAfterMutationWithoutReadablePostState { get; set; }
    public int EquipCalls { get; private set; }
    public int UnEquipCalls { get; private set; }

    public void EquipItem(EquipmentSO equipment)
    {
        EquipCalls++;
        if (ThrowBeforeMutation is { } equipFailure) throw equipFailure;
        var cost = equipment.GetUsageCost();
        if (!cost.HasEnough() ||
            (!equippedEquipment.value.Contains(equipment) && equippedEquipment.IsAtMax())) return;
        var amount = Math.Min(
            GlobalVariables.GetMultiBuy().AsInt(),
            Math.Min(
                equipment.GetMaxLevel() - equippedEquipment.GetStacks(equipment),
                cost.MaximumCostTimes().ToInt()));
        if (!SuppressMutation && amount > 0)
        {
            equippedEquipment.Stack(equipment, amount);
            equipment.Equip(equippedEquipment.GetStacks(equipment));
        }
        if (ThrowAfterMutationWithoutReadablePostState)
        {
            equipment.ThrowOnStateRead = true;
            throw new InvalidOperationException("injected equip failure with unreadable post-state");
        }
        if (ThrowAfterMutation) throw new InvalidOperationException("injected equip failure after mutation");
    }

    public void UnEquipItem(EquipmentSO equipment)
    {
        UnEquipCalls++;
        if (ThrowBeforeMutation is { } unequipFailure) throw unequipFailure;
        if (!equipment.IsEquipped()) return;
        var amount = Math.Min(GlobalVariables.GetMultiBuy().AsInt(), equippedEquipment.GetStacks(equipment));
        if (!SuppressMutation && amount > 0)
        {
            equippedEquipment.Unstack(equipment, amount);
            equipment.Equip(equippedEquipment.GetStacks(equipment));
        }
        if (ThrowAfterMutationWithoutReadablePostState)
        {
            equipment.ThrowOnStateRead = true;
            throw new InvalidOperationException("injected unequip failure with unreadable post-state");
        }
        if (ThrowAfterMutation) throw new InvalidOperationException("injected unequip failure after mutation");
    }
}

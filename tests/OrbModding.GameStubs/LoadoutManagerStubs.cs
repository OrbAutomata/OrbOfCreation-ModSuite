using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class LoadoutManager
{
    public static LoadoutManager instance = new LoadoutManager();
    public PlayerLoadoutListVariable playerLoadouts = new PlayerLoadoutListVariable();
    public AlchemySnapshotListVariable alchemyLoadouts = new AlchemySnapshotListVariable();
    public EquipmentSnapshotListVariable equipmentLoadouts = new EquipmentSnapshotListVariable();
    public AlchemyInstanceListVariable activeAlchemy = new AlchemyInstanceListVariable();
    public EquipmentListVariable activeEquipment = new EquipmentListVariable();
    public SpellListVariable activeSpells = new SpellListVariable();
    public bool SuppressSetLoadout { get; set; }
    public bool ThrowAfterSetLoadout { get; set; }

    /// <summary>The switch lands, then the game throws and the target stops reading.</summary>
    public bool BreakAfterSetLoadout { get; set; }
    public int SetLoadoutCalls { get; private set; }
    public int SaveActiveCalls { get; private set; }

    public int RefusalFeedbacks { get; private set; }

    /// <summary>The game's refusal plays the error sound and raises a popup, counted here.</summary>
    private bool CanSwapLoadouts()
    {
        if (activeSpells.value.All(spell => !spell.IsCasting() && !spell.IsReadyingCast()))
            return true;
        RefusalFeedbacks++;
        return false;
    }

    public void SetLoadout(PlayerLoadout target)
    {
        SetLoadoutCalls++;
        if (!CanSwapLoadouts()) return;
        if (!SuppressSetLoadout)
        {
            SaveActiveLoadout();
            foreach (var loadout in playerLoadouts.value) loadout.isSelected = false;
            target.isSelected = true;
            activeSpells.value = new List<Spell>(target.spells);
            if (target.saveEquipment) activeEquipment.SetStack(target.equipment);
            if (target.saveAlchemy) activeAlchemy.FromStackedRecord(target.alchemy);
        }
        if (BreakAfterSetLoadout) target.ThrowOnSelectedRead = true;
        if (ThrowAfterSetLoadout || BreakAfterSetLoadout)
            throw new InvalidOperationException("injected failure after loadout switch");
    }

    public void SaveActiveLoadout()
    {
        SaveActiveCalls++;
        var active = playerLoadouts.GetActiveLoadout();
        if (active is null) return;
        active.spells = new List<Spell>(activeSpells.value);
        if (active.saveEquipment) active.equipment = activeEquipment.GetStackedRecord();
        if (active.saveAlchemy) active.alchemy = activeAlchemy.CreateStackedRecord();
    }
}

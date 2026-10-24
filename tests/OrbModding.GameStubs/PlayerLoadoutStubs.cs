using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public sealed class PlayerLoadout
{
    public sealed class LoadoutLabel
    {
        private string labelName;
        private int iconIndex;
        private int colorIndex;

        public LoadoutLabel(string name = "Loadout") => labelName = name;
        public string GetName() => labelName;
        public int GetIconIndex() => iconIndex;
        public int GetColorIndex() => colorIndex;
        public void SetName(string value) => labelName = value ?? string.Empty;
        public void SetIconIndex(int value) => iconIndex = value;
        public void SetColorIndex(int value) => colorIndex = value;
    }

    public GuidContainer guidContainer;
    public LoadoutLabel label;
    public bool isSelected;
    public bool saveEquipment;
    public bool saveAlchemy;
    public List<Spell> spells = new List<Spell>();
    public Stacked.StackedIdRecord<EquipmentSO> equipment =
        new Stacked.StackedIdRecord<EquipmentSO>();
    public Stacked.StackedIdRecord<AlchemyRecipeSO> alchemy =
        new Stacked.StackedIdRecord<AlchemyRecipeSO>();
    public bool SuppressSectionMutation { get; set; }
    public bool SuppressLabelMutation { get; set; }

    public PlayerLoadout() : this(Guid.NewGuid(), "Loadout")
    {
    }

    public PlayerLoadout(Guid id, string name)
    {
        guidContainer = new GuidContainer(id);
        label = new LoadoutLabel(name);
    }

    public Guid GetGuid() => guidContainer.guid;
    public string GetName() => label.GetName();
    public bool ThrowOnSelectedRead { get; set; }
    public bool IsSelected() =>
        ThrowOnSelectedRead ? throw new InvalidOperationException("injected failure reading the selected loadout") : isSelected;
    public bool HasEquipment() => saveEquipment;
    public bool HasAlchemy() => saveAlchemy;
    public List<Spell> GetSpells() => new List<Spell>(spells);
    public LoadoutLabel GetLabel() => label;
    public void SetSaveEquipment(bool value)
    {
        if (!SuppressSectionMutation) saveEquipment = value;
    }
    public void SetSaveAlchemy(bool value)
    {
        if (!SuppressSectionMutation) saveAlchemy = value;
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbModding.Tests.Runtime.World;

internal sealed class FakeLoadoutRecord<T>
    where T : class
{
    private readonly List<(T, int)> _entries = new();

    public List<(T, int)> GetEntries() => new(_entries);

    public void Set(T item, int quantity)
    {
        var index = _entries.FindIndex(entry => ReferenceEquals(entry.Item1, item));
        if (quantity <= 0)
        {
            if (index >= 0) _entries.RemoveAt(index);
            return;
        }
        if (index >= 0) _entries[index] = (item, quantity);
        else _entries.Add((item, quantity));
    }
}

internal sealed class FakePlayerLoadoutListVariable
{
    public List<FakePlayerLoadout> value = new();

}

internal sealed class FakePlayerLoadout : FakeIdRegistry
{
    public sealed class LoadoutLabel
    {
        private string _name = string.Empty;
        private int _icon;
        private int _color;

        public string GetName() => _name;
        public int GetIconIndex() => _icon;
        public int GetColorIndex() => _color;
        public void SetName(string value) => _name = value ?? string.Empty;
        public void SetIconIndex(int value) => _icon = value;
        public void SetColorIndex(int value) => _color = value;
    }

    public bool isSelected;
    public bool saveEquipment;
    public bool saveAlchemy;
    public List<FakeSpell> spells = new();
    public FakeLoadoutRecord<FakeEquipment> equipment = new();
    public FakeLoadoutRecord<FakeAlchemyRecipe> alchemy = new();
    private readonly LoadoutLabel _label = new();

    public string GetName() => _label.GetName();
    public bool IsSelected() => isSelected;
    public bool HasEquipment() => saveEquipment;
    public bool HasAlchemy() => saveAlchemy;
    public bool throwsOnSpells;
    public List<FakeSpell> GetSpells() =>
        throwsOnSpells ? throw new InvalidOperationException("GetSpells failed") : spells;
    public LoadoutLabel GetLabel() => _label;
    public void SetSaveEquipment(bool value) => saveEquipment = value;
    public void SetSaveAlchemy(bool value) => saveAlchemy = value;
}

internal abstract class FakeSnapshot<T>
    where T : class
{
    protected FakeLoadoutRecord<T> Record = new();

    public bool IsEmpty() => Record.GetEntries().Count == 0;
    public void Clear() => Record = new FakeLoadoutRecord<T>();
    public FakeLoadoutRecord<T> GetRecord() => Record;
    public void SaveSnapshot(FakeLoadoutRecord<T> record) => Record = Copy(record);

    private static FakeLoadoutRecord<T> Copy(FakeLoadoutRecord<T> source)
    {
        var result = new FakeLoadoutRecord<T>();
        foreach (var entry in source.GetEntries()) result.Set(entry.Item1, entry.Item2);
        return result;
    }
}

internal sealed class FakeAlchemySnapshot : FakeSnapshot<FakeAlchemyRecipe>
{
}

internal sealed class FakeEquipmentSnapshot : FakeSnapshot<FakeEquipment>
{
}

internal sealed class FakeAlchemySnapshotListVariable : FakeIdRegistry
{
    public List<FakeAlchemySnapshot> value = new();

}

internal sealed class FakeEquipmentSnapshotListVariable : FakeIdRegistry
{
    public List<FakeEquipmentSnapshot> value = new();

}

internal sealed class FakeLoadoutManager
{
    public static FakeLoadoutManager instance = new();
    public FakePlayerLoadoutListVariable playerLoadouts = new();
    public FakeAlchemySnapshotListVariable alchemyLoadouts = new();
    public FakeEquipmentSnapshotListVariable equipmentLoadouts = new();
    public FakeSpellLoadout activeSpells = new();
    public int RefusalFeedbacks { get; private set; }

    /// <summary>The game's refusal plays the error sound and raises a popup, counted here.</summary>
    private bool CanSwapLoadouts()
    {
        if (activeSpells.value.All(spell => !spell!.IsCasting() && !spell.IsReadyingCast()))
            return true;
        RefusalFeedbacks++;
        return false;
    }
}

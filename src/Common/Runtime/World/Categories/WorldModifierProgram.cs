using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>The narrowly published formula role of one native modifier record or list.</summary>
internal enum WorldModifierProgramRole
{
    ConceptDrain = 0,
    ConceptSpeed = 1,
    ConceptFreeUsageSlots = 2,
    ConceptOverdriveSpeed = 3,
    ConceptOverdriveDrain = 4,
    ConceptCompletionCost = 5,
    ConceptDrainLevel = 6,
    InstanceScalingCost = 7,
    InstanceScalingSpeed = 8,
    SpellLevelingStandard = 9,
}

internal enum WorldModifierProgramEntrySet
{
    Passive = 0,
    Active = 1,
    Modifier = 2,
    Exponent = 3,
}

/// <summary>
/// One modifier program header. Record programs preserve the game's clean-memo branch; list
/// programs carry only entries and therefore use zero/default header values.
/// </summary>
internal readonly struct WorldModifierProgram : IWorldEntity
{
    internal WorldModifierProgram(
        Guid ownerId,
        WorldModifierProgramRole role,
        bool isRecord,
        double baseValue,
        bool calculationDirty,
        BigDouble calculatedValue)
    {
        OwnerId = ownerId;
        Role = role;
        IsRecord = isRecord;
        BaseValue = baseValue;
        CalculationDirty = calculationDirty;
        CalculatedValue = calculatedValue;
    }

    internal Guid OwnerId { get; }
    public Guid EntityId => OwnerId;

    [WorldRowKeyPart(0)]
    internal WorldModifierProgramRole Role { get; }
    internal bool IsRecord { get; }
    internal double BaseValue { get; }
    internal bool CalculationDirty { get; }
    internal BigDouble CalculatedValue { get; }
}

/// <summary>One ordered modifier entry, including the native dictionary identity when one exists.</summary>
internal readonly struct WorldModifierProgramEntry : IWorldEntity
{
    internal WorldModifierProgramEntry(
        Guid ownerId,
        WorldModifierProgramRole role,
        WorldModifierProgramEntrySet set,
        int position,
        Guid modifierId,
        GameValueModifierType type,
        int order,
        BigDouble amount)
    {
        OwnerId = ownerId;
        Role = role;
        Set = set;
        Position = position;
        ModifierId = modifierId;
        Type = type;
        Order = order;
        Amount = amount;
    }

    internal Guid OwnerId { get; }
    public Guid EntityId => OwnerId;

    [WorldRowKeyPart(0)]
    internal WorldModifierProgramRole Role { get; }

    [WorldRowKeyPart(1)]
    internal WorldModifierProgramEntrySet Set { get; }

    [WorldRowKeyPart(2)]
    internal int Position { get; }
    /// <summary>Native dictionary key for records; empty for positional ValueModifierList entries.</summary>
    internal Guid ModifierId { get; }
    internal GameValueModifierType Type { get; }
    internal int Order { get; }
    internal BigDouble Amount { get; }
    internal GameValueModifier Modifier => new(Type, Amount, Order);
}

internal sealed class WorldModifierProgramBuffer
{
    private WorldModifierProgram[] _rows = new WorldModifierProgram[64];
    private int _count;
    internal int Count => _count;
    internal ref readonly WorldModifierProgram this[int index] => ref _rows[index];
    internal void Reset() => _count = 0;
    internal void Append(in WorldModifierProgram row)
    {
        if (_count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[_count++] = row;
    }
}

internal sealed class WorldModifierProgramEntryBuffer
{
    private WorldModifierProgramEntry[] _rows = new WorldModifierProgramEntry[128];
    private int _count;
    internal int Count => _count;
    internal ref readonly WorldModifierProgramEntry this[int index] => ref _rows[index];
    internal void Reset() => _count = 0;
    internal void Append(in WorldModifierProgramEntry row)
    {
        if (_count == _rows.Length) Array.Resize(ref _rows, _rows.Length * 2);
        _rows[_count++] = row;
    }
}

internal static class WorldModifierProgramDeriver
{
    internal static PublicationTable<WorldModifierProgram> Build(WorldModifierProgramBuffer buffer)
    {
        var rows = new WorldModifierProgram[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) => Compare(left.OwnerId, left.Role, right.OwnerId, right.Role));
        return PublicationTable<WorldModifierProgram>.Create(rows, rows.Length);
    }

    internal static PublicationTable<WorldModifierProgramEntry> Build(
        WorldModifierProgramEntryBuffer buffer)
    {
        var rows = new WorldModifierProgramEntry[buffer.Count];
        for (var index = 0; index < rows.Length; index++) rows[index] = buffer[index];
        Array.Sort(rows, static (left, right) =>
        {
            var key = Compare(left.OwnerId, left.Role, right.OwnerId, right.Role);
            if (key != 0) return key;
            var set = ((int)left.Set).CompareTo((int)right.Set);
            return set != 0 ? set : left.Position.CompareTo(right.Position);
        });
        return PublicationTable<WorldModifierProgramEntry>.Create(rows, rows.Length);
    }

    private static int Compare(
        Guid leftOwner,
        WorldModifierProgramRole leftRole,
        Guid rightOwner,
        WorldModifierProgramRole rightRole)
    {
        var owner = leftOwner.CompareTo(rightOwner);
        return owner != 0 ? owner : ((int)leftRole).CompareTo((int)rightRole);
    }
}

internal static class WorldModifierProgramMath
{
    internal static bool TryFoldRecord(
        PublicationTable<WorldModifierProgram> programs,
        PublicationTable<WorldModifierProgramEntry> entries,
        Guid ownerId,
        WorldModifierProgramRole role,
        out BigDouble value)
    {
        value = default;
        if (!TryFind(programs, ownerId, role, out var program) || !program.IsRecord) return false;
        if (!program.CalculationDirty)
        {
            value = program.CalculatedValue;
            return true;
        }

        if (!TryFindEntries(entries, ownerId, role, out var start, out var count))
        {
            value = new BigDouble(program.BaseValue);
            return true;
        }

        var modifiers = new GameValueModifier[count];
        for (var index = 0; index < count; index++) modifiers[index] = entries[start + index].Modifier;
        value = GameModifierStack.AdjustWith(new BigDouble(program.BaseValue), modifiers);
        return true;
    }

    internal static bool TryAdjustScaledList(
        PublicationTable<WorldModifierProgram> programs,
        PublicationTable<WorldModifierProgramEntry> entries,
        Guid ownerId,
        WorldModifierProgramRole role,
        BigDouble scalar,
        BigDouble baseValue,
        out BigDouble value)
    {
        value = baseValue;
        if (!TryFind(programs, ownerId, role, out var program) || program.IsRecord) return false;
        if (!TryFindEntries(entries, ownerId, role, out var start, out var count)) return true;

        var modifierCount = 0;
        var exponentCount = 0;
        for (var index = 0; index < count; index++)
        {
            if (entries[start + index].Set == WorldModifierProgramEntrySet.Exponent) exponentCount++;
            else modifierCount++;
        }

        var modifiers = new GameValueModifier[modifierCount];
        var exponents = new GameValueModifier[exponentCount];
        var modifierIndex = 0;
        var exponentIndex = 0;
        for (var index = 0; index < count; index++)
        {
            var entry = entries[start + index];
            var scaled = entry.Modifier.MultiplyScalar(scalar);
            if (entry.Set == WorldModifierProgramEntrySet.Exponent)
                exponents[exponentIndex++] = scaled;
            else
                modifiers[modifierIndex++] = scaled;
        }

        var scratch = new GameValueModifier[modifierCount];
        value = GameModifierStack.AdjustWith(baseValue, modifiers, exponents, scratch);
        return true;
    }

    internal static bool TryFind(
        PublicationTable<WorldModifierProgram> table,
        Guid ownerId,
        WorldModifierProgramRole role,
        out WorldModifierProgram row)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            var comparison = Compare(rows[middle].OwnerId, rows[middle].Role, ownerId, role);
            if (comparison == 0) { row = rows[middle]; return true; }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        row = default;
        return false;
    }

    private static bool TryFindEntries(
        PublicationTable<WorldModifierProgramEntry> table,
        Guid ownerId,
        WorldModifierProgramRole role,
        out int start,
        out int count)
    {
        var rows = table.AsSpan();
        var low = 0;
        var high = rows.Length - 1;
        while (low <= high)
        {
            var middle = low + ((high - low) / 2);
            if (Compare(rows[middle].OwnerId, rows[middle].Role, ownerId, role) < 0) low = middle + 1;
            else high = middle - 1;
        }
        start = low;
        count = 0;
        while (start + count < rows.Length &&
               Compare(rows[start + count].OwnerId, rows[start + count].Role, ownerId, role) == 0)
            count++;
        return count > 0;
    }

    private static int Compare(
        Guid leftOwner,
        WorldModifierProgramRole leftRole,
        Guid rightOwner,
        WorldModifierProgramRole rightRole)
    {
        var owner = leftOwner.CompareTo(rightOwner);
        return owner != 0 ? owner : ((int)leftRole).CompareTo((int)rightRole);
    }
}

/// <summary>Copies native modifier records/lists as data without invoking their arithmetic.</summary>
internal sealed class NativeModifierProgramReader
{
    private const BindingFlags Instance =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Func<object, BigDouble>? _memo;
    private readonly Func<object, bool>? _dirty;
    private readonly Func<object, double>? _base;
    private readonly Func<object, object?>? _passive;
    private readonly Func<object, object?>? _active;
    private readonly Func<object, IList?>? _modifiers;
    private readonly Func<object, IList?>? _exponents;
    private readonly Func<object, int>? _type;
    private readonly Func<object, int>? _order;
    private readonly Func<object, BigDouble>? _amount;

    internal NativeModifierProgramReader(Type? recordType, Type? listType)
    {
        _memo = NativeAccessorBinder.Field<BigDouble>(recordType, "calculatedValue");
        _dirty = NativeAccessorBinder.Field<bool>(recordType, "calculationDirty");
        _base = NativeAccessorBinder.Field<double>(recordType, "baseValue");
        _passive = NativeAccessorBinder.Reference(recordType, "passiveModifiers");
        _active = NativeAccessorBinder.Reference(recordType, "activeModifiers");
        _modifiers = NativeAccessorBinder.CollectionField(listType, "modifiers");
        _exponents = NativeAccessorBinder.CollectionField(listType, "exponents");

        var modifierType = NativeAccessorBinder.CollectionElementType(listType, "modifiers") ??
            recordType?.GetField("passiveModifiers", Instance)?.FieldType.GetGenericArguments()[1];
        _type = NativeAccessorBinder.EnumField(modifierType, "type");
        _order = NativeAccessorBinder.Field<int>(modifierType, "order");
        _amount = NativeAccessorBinder.Field<BigDouble>(modifierType, "adjustReal");
    }

    internal bool IsAvailable =>
        _memo is not null && _dirty is not null && _base is not null &&
        _passive is not null && _active is not null && _modifiers is not null &&
        _exponents is not null && _type is not null && _order is not null && _amount is not null;

    internal void CaptureRecord(
        Guid ownerId,
        WorldModifierProgramRole role,
        object record,
        WorldModifierProgramBuffer programs,
        WorldModifierProgramEntryBuffer entries)
    {
        programs.Append(new WorldModifierProgram(
            ownerId, role, isRecord: true, _base!(record), _dirty!(record), _memo!(record)));
        AppendDictionary(ownerId, role, WorldModifierProgramEntrySet.Passive, _passive!(record), entries);
        AppendDictionary(ownerId, role, WorldModifierProgramEntrySet.Active, _active!(record), entries);
    }

    internal void CaptureList(
        Guid ownerId,
        WorldModifierProgramRole role,
        object? list,
        WorldModifierProgramBuffer programs,
        WorldModifierProgramEntryBuffer entries)
    {
        programs.Append(new WorldModifierProgram(ownerId, role, false, 0d, false, default));
        if (list is null) return;
        AppendList(ownerId, role, WorldModifierProgramEntrySet.Modifier, _modifiers!(list), entries);
        AppendList(ownerId, role, WorldModifierProgramEntrySet.Exponent, _exponents!(list), entries);
    }

    private void AppendDictionary(
        Guid ownerId,
        WorldModifierProgramRole role,
        WorldModifierProgramEntrySet set,
        object? source,
        WorldModifierProgramEntryBuffer destination)
    {
        if (source is not IDictionary dictionary) return;
        var position = 0;
        foreach (DictionaryEntry pair in dictionary)
        {
            if (pair.Value is null) continue;
            destination.Append(new WorldModifierProgramEntry(
                ownerId, role, set, position++, pair.Key is Guid id ? id : Guid.Empty,
                (GameValueModifierType)_type!(pair.Value), _order!(pair.Value), _amount!(pair.Value)));
        }
    }

    private void AppendList(
        Guid ownerId,
        WorldModifierProgramRole role,
        WorldModifierProgramEntrySet set,
        IList? source,
        WorldModifierProgramEntryBuffer destination)
    {
        if (source is null) return;
        for (var index = 0; index < source.Count; index++)
        {
            var value = source[index];
            if (value is null) continue;
            destination.Append(new WorldModifierProgramEntry(
                ownerId, role, set, index, Guid.Empty,
                (GameValueModifierType)_type!(value), _order!(value), _amount!(value)));
        }
    }
}

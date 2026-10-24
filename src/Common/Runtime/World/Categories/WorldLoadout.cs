using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldPlayerLoadout : IWorldEntity
{
    internal WorldPlayerLoadout(Guid id, string name, bool selected,
        bool savesEquipment, bool savesAlchemy, int icon, int color, bool canSwitchNow)
    {
        EntityId = id;
        Name = name ?? string.Empty;
        Selected = selected;
        SavesEquipment = savesEquipment;
        SavesAlchemy = savesAlchemy;
        Icon = icon;
        Color = color;
        CanSwitchNow = canSwitchNow;
    }

    public Guid EntityId { get; }
    internal string Name { get; }
    internal bool Selected { get; }
    internal bool SavesEquipment { get; }
    internal bool SavesAlchemy { get; }
    internal int Icon { get; }
    internal int Color { get; }
    internal bool CanSwitchNow { get; }
}

internal enum WorldLoadoutEntryKind { Spell = 1, Equipment = 2, Alchemy = 3 }

internal readonly struct WorldLoadoutEntry
{
    internal WorldLoadoutEntry(Guid ownerId, WorldLoadoutEntryKind kind,
        Guid entryId, Guid referenceId, int quantity)
    {
        OwnerId = ownerId;
        Kind = kind;
        EntryId = entryId;
        ReferenceId = referenceId;
        Quantity = quantity;
    }

    internal Guid OwnerId { get; }
    internal WorldLoadoutEntryKind Kind { get; }
    internal Guid EntryId { get; }
    internal Guid ReferenceId { get; }
    internal int Quantity { get; }
}

internal enum WorldSnapshotLoadoutKind { Alchemy = 1, Equipment = 2 }

internal readonly struct WorldSnapshotLoadout : IWorldEntity
{
    internal WorldSnapshotLoadout(Guid id, WorldSnapshotLoadoutKind kind, int slots)
    {
        EntityId = id;
        Kind = kind;
        Slots = slots;
    }

    public Guid EntityId { get; }
    internal WorldSnapshotLoadoutKind Kind { get; }
    internal int Slots { get; }
}

internal readonly struct WorldSnapshotSlot
{
    internal WorldSnapshotSlot(Guid ownerId, int slot, bool populated)
    {
        OwnerId = ownerId;
        Slot = slot;
        Populated = populated;
    }

    internal Guid OwnerId { get; }
    internal int Slot { get; }
    internal bool Populated { get; }
}

internal readonly struct WorldSnapshotEntry
{
    internal WorldSnapshotEntry(Guid ownerId, int slot, Guid entryId, int quantity)
    {
        OwnerId = ownerId;
        Slot = slot;
        EntryId = entryId;
        Quantity = quantity;
    }

    internal Guid OwnerId { get; }
    internal int Slot { get; }
    internal Guid EntryId { get; }
    internal int Quantity { get; }
}

internal sealed class WorldLoadoutReader : IWorldCategoryReader
{
    private readonly Type? _managerType;
    private readonly Type? _playerType;
    private readonly Type? _spellType;
    private readonly Type? _equipmentType;
    private readonly Type? _alchemyRecipeType;
    private readonly Type? _alchemySnapshotListType;
    private readonly Type? _equipmentSnapshotListType;
    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _playerLoadouts;
    private readonly Func<object, object?>? _alchemySnapshots;
    private readonly Func<object, object?>? _equipmentSnapshots;
    private readonly Func<object, bool>? _canSwap;
    private readonly Func<object, IList?>? _playerValues;
    private readonly Func<object, Guid>? _playerId;
    private readonly Func<object, string>? _playerName;
    private readonly Func<object, bool>? _playerSelected;
    private readonly Func<object, bool>? _equipmentEnabled;
    private readonly Func<object, bool>? _alchemyEnabled;
    private readonly Func<object, IList?>? _playerSpells;
    private readonly Func<object, object?>? _playerEquipmentRecord;
    private readonly Func<object, object?>? _playerAlchemyRecord;
    private readonly Func<object, object?>? _playerLabel;
    private readonly Func<object, int>? _labelIcon;
    private readonly Func<object, int>? _labelColor;
    private readonly Func<object, Guid>? _identity;
    private readonly Func<object, Guid>? _spellId;
    private readonly Func<object, object?>? _spellReference;
    private readonly Func<object, bool>? _spellEmpty;
    private readonly Func<object, IList?>? _alchemySnapshotValues;
    private readonly Func<object, IList?>? _equipmentSnapshotValues;
    private readonly Func<object, bool>? _alchemySnapshotEmpty;
    private readonly Func<object, bool>? _equipmentSnapshotEmpty;
    private readonly Func<object, object?>? _alchemySnapshotRecord;
    private readonly Func<object, object?>? _equipmentSnapshotRecord;
    private readonly Func<object, IList?>? _alchemyRecordEntries;
    private readonly Func<object, IList?>? _equipmentRecordEntries;
    private readonly string _unavailable;

    internal WorldLoadoutReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        var missing = new List<string>();
        Type? Resolve(string name)
        {
            var type = resolveType(name);
            if (type is null) missing.Add(name);
            return type;
        }

        _managerType = Resolve("LoadoutManager");
        var playerListType = Resolve("PlayerLoadoutListVariable");
        _playerType = Resolve("PlayerLoadout");
        var labelType = Resolve("PlayerLoadout+LoadoutLabel");
        _spellType = Resolve("Spell");
        var spellListType = Resolve("SpellListVariable");
        var spellRecipeType = Resolve("SpellRecipeSO");
        _equipmentType = Resolve("EquipmentSO");
        _alchemyRecipeType = Resolve("AlchemyRecipeSO");
        _alchemySnapshotListType = Resolve("AlchemySnapshotListVariable");
        _equipmentSnapshotListType = Resolve("EquipmentSnapshotListVariable");
        var alchemySnapshotType = Resolve("AlchemySnapshot");
        var equipmentSnapshotType = Resolve("EquipmentSnapshot");
        var identityType = Resolve("IdScriptableObject");
        var stackedType = Resolve("Stacked.StackedIdRecord`1");
        if (missing.Count > 0)
        {
            _unavailable = string.Join(", ", missing) + " did not resolve on this build";
            return;
        }

        var alchemyRecordType = stackedType!.MakeGenericType(_alchemyRecipeType!);
        var equipmentRecordType = stackedType.MakeGenericType(_equipmentType!);

        _manager = NativeAccessorBinder.StaticReference(_managerType, "instance", _managerType);
        var manager = new WorldMemberBinding(_managerType!, "LoadoutManager");
        _playerLoadouts = manager.Reference("playerLoadouts", playerListType);
        _alchemySnapshots = manager.Reference("alchemyLoadouts", _alchemySnapshotListType);
        _equipmentSnapshots = manager.Reference("equipmentLoadouts", _equipmentSnapshotListType);
        var activeSpells = manager.Reference("activeSpells", spellListType);

        var playerList = new WorldMemberBinding(playerListType!, "PlayerLoadoutListVariable");
        _playerValues = playerList.CollectionField("value", _playerType);

        var spellList = new WorldMemberBinding(spellListType!, "SpellListVariable");
        var spellValues = spellList.CollectionField("value", _spellType);

        var player = new WorldMemberBinding(_playerType!, "PlayerLoadout");
        _playerId = player.StableUuid();
        _playerName = player.Call<string>("GetName");
        _playerSelected = player.Call<bool>("IsSelected");
        _equipmentEnabled = player.Call<bool>("HasEquipment");
        _alchemyEnabled = player.Call<bool>("HasAlchemy");
        _playerSpells = player.CallList("GetSpells", _spellType);
        _playerEquipmentRecord = player.Reference("equipment", equipmentRecordType);
        _playerAlchemyRecord = player.Reference("alchemy", alchemyRecordType);
        _playerLabel = player.CallObject("GetLabel", labelType);

        var label = new WorldMemberBinding(labelType!, "PlayerLoadout+LoadoutLabel");
        _labelIcon = label.Call<int>("GetIconIndex");
        _labelColor = label.Call<int>("GetColorIndex");

        var spell = new WorldMemberBinding(_spellType!, "Spell");
        _spellEmpty = spell.Call<bool>("IsEmpty");
        _spellReference = spell.CallObject("get_reference", spellRecipeType);
        _spellId = spell.Call<Guid>("GetId");
        var casting = spell.Call<bool>("IsCasting");
        var readying = spell.Call<bool>("IsReadyingCast");

        var alchemySnapshotList = new WorldMemberBinding(_alchemySnapshotListType!, "AlchemySnapshotListVariable");
        _alchemySnapshotValues = alchemySnapshotList.CollectionField("value", alchemySnapshotType);
        var equipmentSnapshotList = new WorldMemberBinding(_equipmentSnapshotListType!, "EquipmentSnapshotListVariable");
        _equipmentSnapshotValues = equipmentSnapshotList.CollectionField("value", equipmentSnapshotType);

        var alchemySnapshot = new WorldMemberBinding(alchemySnapshotType!, "AlchemySnapshot");
        _alchemySnapshotEmpty = alchemySnapshot.Call<bool>("IsEmpty");
        _alchemySnapshotRecord = alchemySnapshot.CallObject("GetRecord", alchemyRecordType);
        var equipmentSnapshot = new WorldMemberBinding(equipmentSnapshotType!, "EquipmentSnapshot");
        _equipmentSnapshotEmpty = equipmentSnapshot.Call<bool>("IsEmpty");
        _equipmentSnapshotRecord = equipmentSnapshot.CallObject("GetRecord", equipmentRecordType);

        var alchemyRecord = new WorldMemberBinding(alchemyRecordType, "StackedIdRecord<AlchemyRecipeSO>");
        _alchemyRecordEntries = alchemyRecord.CallList("GetEntries",
            typeof(ValueTuple<,>).MakeGenericType(_alchemyRecipeType!, typeof(int)));
        var equipmentRecord = new WorldMemberBinding(equipmentRecordType, "StackedIdRecord<EquipmentSO>");
        _equipmentRecordEntries = equipmentRecord.CallList("GetEntries",
            typeof(ValueTuple<,>).MakeGenericType(_equipmentType!, typeof(int)));

        var identity = new WorldMemberBinding(identityType!, "IdScriptableObject");
        _identity = identity.StableUuid();

        if (activeSpells is not null && spellValues is not null && casting is not null && readying is not null)
            _canSwap = LoadoutNativeShapes.SpellsAllowSwap(activeSpells, spellValues, casting, readying);

        _unavailable = WorldFailures.Join(
            _manager is null ? "LoadoutManager did not expose instance on this build" : string.Empty,
            manager.Failure, playerList.Failure, spellList.Failure, player.Failure, label.Failure,
            spell.Failure, alchemySnapshotList.Failure, equipmentSnapshotList.Failure,
            alchemySnapshot.Failure, equipmentSnapshot.Failure, alchemyRecord.Failure,
            equipmentRecord.Failure, identity.Failure);
    }

    public string Category => "loadouts";
    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        ResetRows(frame);
        if (!IsAvailable)
            return WorldCategoryReport.Missing(Category, _unavailable);
        var readOwnerId = Guid.Empty;
        try
        {
            var manager = _manager!();
            if (manager is null || manager.GetType() != _managerType)
                return WorldCategoryReport.Missing(Category,
                    "LoadoutManager.instance is unavailable in this scene");
            var sampled = 0;
            var skipped = 0;
            var firstFailure = default(WorldCollectionFailure);
            var playerList = _playerLoadouts!(manager);
            var players = playerList is null ? null : _playerValues!(playerList);
            var canSwap = _canSwap!(manager);
            for (var index = 0; index < (players?.Count ?? 0); index++)
            {
                readOwnerId = Guid.Empty;
                var player = players![index];
                if (player is null || player.GetType() != _playerType)
                {
                    WorldSkipTally.Skip(ref skipped, ref firstFailure,
                        "a player loadout had an unexpected native type");
                    continue;
                }
                var id = readOwnerId = _playerId!(player);
                if (id == Guid.Empty)
                {
                    WorldSkipTally.Skip(ref skipped, ref firstFailure,
                        "a player loadout had an empty identity");
                    continue;
                }
                var label = _playerLabel!(player);
                if (label is null)
                {
                    WorldSkipTally.Skip(ref skipped, ref firstFailure,
                        "a player loadout had no label");
                    continue;
                }
                if (!claimed.Add(id))
                {
                    WorldSkipTally.Skip(ref skipped, ref firstFailure,
                        "a player loadout had a duplicate identity");
                    continue;
                }
                var selected = _playerSelected!(player);
                frame.PlayerLoadouts.Append(new WorldPlayerLoadout(id,
                    _playerName!(player), selected, _equipmentEnabled!(player),
                    _alchemyEnabled!(player), _labelIcon!(label),
                    _labelColor!(label), !selected && canSwap));
                AppendPlayerEntries(player, id, frame.PlayerLoadoutEntries,
                    ref skipped, ref firstFailure);
                sampled++;
            }
            readOwnerId = Guid.Empty;
            sampled += AppendSnapshotList(_alchemySnapshots!(manager), true,
                claimed, frame, ref skipped, ref firstFailure, ref readOwnerId);
            readOwnerId = Guid.Empty;
            sampled += AppendSnapshotList(_equipmentSnapshots!(manager), false,
                claimed, frame, ref skipped, ref firstFailure, ref readOwnerId);
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected,
                sampled, skipped, firstFailure);
        }
        catch (Exception exception) when (NativeFailureClassifier.IsExpected(exception))
        {
            ResetRows(frame);
            return WorldCategoryReport.Missing(Category,
                WorldFailures.ReadFault("reading loadouts threw", exception, readOwnerId));
        }
    }

    private static void ResetRows(GameWorldCycleFrame frame)
    {
        frame.PlayerLoadouts.Reset();
        frame.PlayerLoadoutEntries.Reset();
        frame.SnapshotLoadouts.Reset();
        frame.SnapshotSlots.Reset();
        frame.SnapshotEntries.Reset();
    }

    private void AppendPlayerEntries(object player, Guid ownerId,
        WorldRelationBuffer<WorldLoadoutEntry> destination,
        ref int skipped, ref WorldCollectionFailure firstFailure)
    {
        var spells = _playerSpells!(player);
        for (var index = 0; index < (spells?.Count ?? 0); index++)
        {
            var spell = spells![index];
            if (spell is null) continue;
            if (spell.GetType() != _spellType)
            {
                WorldSkipTally.Skip(ref skipped, ref firstFailure,
                    "a loadout's spell entry had an unexpected native type");
                continue;
            }
            if (_spellEmpty!(spell)) continue;
            var reference = _spellReference!(spell);
            var spellId = _spellId!(spell);
            var recipeId = reference is null ? Guid.Empty : _identity!(reference);
            if (spellId == Guid.Empty || recipeId == Guid.Empty)
            {
                WorldSkipTally.Skip(ref skipped, ref firstFailure,
                    "a loadout's spell entry had an empty identity");
                continue;
            }
            destination.Append(new WorldLoadoutEntry(ownerId,
                WorldLoadoutEntryKind.Spell, spellId, recipeId, 1));
        }
        AppendRecord(_playerEquipmentRecord!(player), ownerId,
            WorldLoadoutEntryKind.Equipment, destination, ref skipped, ref firstFailure);
        AppendRecord(_playerAlchemyRecord!(player), ownerId,
            WorldLoadoutEntryKind.Alchemy, destination, ref skipped, ref firstFailure);
    }

    private void AppendRecord(object? record, Guid ownerId, WorldLoadoutEntryKind kind,
        WorldRelationBuffer<WorldLoadoutEntry> destination,
        ref int skipped, ref WorldCollectionFailure firstFailure)
    {
        var alchemy = kind == WorldLoadoutEntryKind.Alchemy;
        var entries = record is null
            ? null
            : alchemy ? _alchemyRecordEntries!(record) : _equipmentRecordEntries!(record);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            if (TryReadEntry(entries![index], alchemy, out var id, out var quantity,
                    ref skipped, ref firstFailure))
                destination.Append(new WorldLoadoutEntry(ownerId, kind, id, Guid.Empty, quantity));
        }
    }

    private bool TryReadEntry(object? entry, bool alchemy,
        out Guid id, out int quantity, ref int skipped, ref WorldCollectionFailure firstFailure)
    {
        id = Guid.Empty;
        var kind = alchemy ? "alchemy" : "equipment";
        if (entry is null || !LoadoutNativeShapes.TryReadEntry(entry,
                alchemy ? _alchemyRecipeType! : _equipmentType!,
                out var item, out quantity))
        {
            quantity = 0;
            WorldSkipTally.Skip(ref skipped, ref firstFailure, $"a loadout's {kind} entry could not be read");
            return false;
        }
        if (quantity <= 0) return false;
        id = _identity!(item!);
        if (id != Guid.Empty) return true;
        WorldSkipTally.Skip(ref skipped, ref firstFailure, $"a loadout's {kind} entry had an empty identity");
        return false;
    }

    private int AppendSnapshotList(object? owner, bool alchemy, HashSet<Guid> claimed,
        GameWorldCycleFrame frame, ref int skipped, ref WorldCollectionFailure firstFailure, ref Guid readOwnerId)
    {
        var expected = alchemy ? _alchemySnapshotListType : _equipmentSnapshotListType;
        if (owner is null || owner.GetType() != expected)
        {
            WorldSkipTally.Skip(ref skipped, ref firstFailure,
                "a snapshot list had an unexpected native type");
            return 0;
        }
        var id = readOwnerId = _identity!(owner);
        if (id == Guid.Empty || !claimed.Add(id))
        {
            WorldSkipTally.Skip(ref skipped, ref firstFailure,
                "a snapshot list had an empty or duplicate identity");
            return 0;
        }
        var values = alchemy
            ? _alchemySnapshotValues!(owner)
            : _equipmentSnapshotValues!(owner);
        frame.SnapshotLoadouts.Append(new WorldSnapshotLoadout(id,
            alchemy ? WorldSnapshotLoadoutKind.Alchemy : WorldSnapshotLoadoutKind.Equipment,
            values?.Count ?? 0));
        for (var slot = 0; slot < (values?.Count ?? 0); slot++)
        {
            var snapshot = values![slot];
            if (snapshot is null)
            {
                WorldSkipTally.Skip(ref skipped, ref firstFailure,
                    "a snapshot slot was unavailable");
                continue;
            }
            var empty = alchemy
                ? _alchemySnapshotEmpty!(snapshot)
                : _equipmentSnapshotEmpty!(snapshot);
            frame.SnapshotSlots.Append(new WorldSnapshotSlot(id, slot, !empty));
            var record = alchemy
                ? _alchemySnapshotRecord!(snapshot)
                : _equipmentSnapshotRecord!(snapshot);
            var entries = record is null
                ? null
                : alchemy ? _alchemyRecordEntries!(record) : _equipmentRecordEntries!(record);
            for (var index = 0; index < (entries?.Count ?? 0); index++)
            {
                if (TryReadEntry(entries![index], alchemy, out var entryId,
                        out var quantity, ref skipped, ref firstFailure))
                    frame.SnapshotEntries.Append(new WorldSnapshotEntry(
                        id, slot, entryId, quantity));
            }
        }
        return 1;
    }
}

internal static class WorldLoadoutLookup
{
    internal static readonly Comparison<WorldLoadoutEntry> EntryOrder =
        static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var kind = left.Kind.CompareTo(right.Kind);
            return kind != 0 ? kind : left.EntryId.CompareTo(right.EntryId);
        };

    internal static readonly Comparison<WorldSnapshotSlot> SlotOrder =
        static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            return owner != 0 ? owner : left.Slot.CompareTo(right.Slot);
        };

    internal static readonly Comparison<WorldSnapshotEntry> SnapshotEntryOrder =
        static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var slot = left.Slot.CompareTo(right.Slot);
            return slot != 0 ? slot : left.EntryId.CompareTo(right.EntryId);
        };

    internal static bool TryFindPlayer(PublicationTable<WorldPlayerLoadout> rows,
        Guid id, out WorldPlayerLoadout value) => WorldLookup.TryFind(rows, id, out value);

    internal static bool TryFindSnapshot(PublicationTable<WorldSnapshotLoadout> rows,
        Guid id, out WorldSnapshotLoadout value) => WorldLookup.TryFind(rows, id, out value);
}

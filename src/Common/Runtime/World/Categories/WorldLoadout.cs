using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
    private readonly LoadoutNativeBindings? _bindings;
    private readonly string _unavailable;

    internal WorldLoadoutReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        if (LoadoutNativeBindings.TryCreate(out var bindings, out var reason, resolveType))
        {
            _bindings = bindings;
            _unavailable = string.Empty;
        }
        else
        {
            _unavailable = reason;
        }
    }

    public string Category => "loadouts";
    public bool IsAvailable => _bindings is not null;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        frame.PlayerLoadouts.Reset();
        frame.PlayerLoadoutEntries.Reset();
        frame.SnapshotLoadouts.Reset();
        frame.SnapshotSlots.Reset();
        frame.SnapshotEntries.Reset();
        if (_bindings is not { } native)
            return WorldCategoryReport.Missing(Category, _unavailable);
        try
        {
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return WorldCategoryReport.Missing(Category,
                    "LoadoutManager.instance is unavailable in this scene");
            var sampled = 0;
            var skipped = 0;
            var firstFailure = string.Empty;
            var playerList = native.PlayerLoadouts(manager);
            var players = playerList is null ? null : native.PlayerValues(playerList);
            var canSwap = native.CanSwap(manager);
            for (var index = 0; index < (players?.Count ?? 0); index++)
            {
                var player = players![index];
                if (player is null || player.GetType() != native.PlayerLoadoutType)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a player loadout had an unexpected native type");
                    continue;
                }
                var id = native.PlayerId(player);
                if (id == Guid.Empty)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a player loadout had an empty identity");
                    continue;
                }
                var label = native.PlayerLabel(player);
                if (label is null)
                {
                    Skip(ref skipped, ref firstFailure,
                        "a player loadout had no label");
                    continue;
                }
                if (!claimed.Add(id))
                {
                    Skip(ref skipped, ref firstFailure,
                        "a player loadout had a duplicate identity");
                    continue;
                }
                var selected = native.PlayerSelected(player);
                frame.PlayerLoadouts.Append(new WorldPlayerLoadout(id,
                    native.PlayerName(player), selected, native.EquipmentEnabled(player),
                    native.AlchemyEnabled(player), native.LabelIcon(label),
                    native.LabelColor(label), !selected && canSwap));
                AppendPlayerEntries(native, player, id, frame.PlayerLoadoutEntries);
                sampled++;
            }
            sampled += AppendSnapshotList(native, native.AlchemySnapshots(manager), true,
                claimed, frame, ref skipped, ref firstFailure);
            sampled += AppendSnapshotList(native, native.EquipmentSnapshots(manager), false,
                claimed, frame, ref skipped, ref firstFailure);
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected,
                sampled, skipped, firstFailure);
        }
        catch (Exception exception) when (exception is TargetInvocationException or
            ArgumentException or InvalidOperationException or OverflowException)
        {
            return WorldCategoryReport.Missing(Category,
                "reading loadouts threw: " + exception.GetBaseException().Message);
        }
    }

    private static void AppendPlayerEntries(LoadoutNativeBindings native, object player,
        Guid ownerId, WorldRelationBuffer<WorldLoadoutEntry> destination)
    {
        var spells = native.PlayerSpells(player);
        for (var index = 0; index < (spells?.Count ?? 0); index++)
        {
            var spell = spells![index];
            if (spell is null || spell.GetType() != native.SpellType || native.SpellEmpty(spell)) continue;
            var reference = native.SpellReference(spell);
            if (reference is null) continue;
            var spellId = native.SpellId(spell);
            var recipeId = native.Identity(reference);
            if (spellId != Guid.Empty && recipeId != Guid.Empty)
                destination.Append(new WorldLoadoutEntry(ownerId,
                    WorldLoadoutEntryKind.Spell, spellId, recipeId, 1));
        }
        AppendRecord(native, native.PlayerEquipmentRecord(player), ownerId,
            WorldLoadoutEntryKind.Equipment, destination);
        AppendRecord(native, native.PlayerAlchemyRecord(player), ownerId,
            WorldLoadoutEntryKind.Alchemy, destination);
    }

    private static void AppendRecord(LoadoutNativeBindings native, object? record,
        Guid ownerId, WorldLoadoutEntryKind kind,
        WorldRelationBuffer<WorldLoadoutEntry> destination)
    {
        var entries = kind == WorldLoadoutEntryKind.Equipment
            ? record is null ? null : native.EquipmentRecordEntries(record)
            : record is null ? null : native.AlchemyRecordEntries(record);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index]!;
            var expectedType = kind == WorldLoadoutEntryKind.Equipment
                ? native.EquipmentType : native.AlchemyRecipeType;
            if (!LoadoutNativeBindings.TryReadEntry(entry, expectedType,
                    out var item, out var quantity) || item is null) continue;
            var id = native.Identity(item);
            if (id != Guid.Empty && quantity > 0)
                destination.Append(new WorldLoadoutEntry(ownerId, kind, id, Guid.Empty, quantity));
        }
    }

    private static int AppendSnapshotList(LoadoutNativeBindings native, object? owner,
        bool alchemy, HashSet<Guid> claimed, GameWorldCycleFrame frame,
        ref int skipped, ref string firstFailure)
    {
        var expected = alchemy ? native.AlchemySnapshotListType : native.EquipmentSnapshotListType;
        if (owner is null || owner.GetType() != expected)
        {
            Skip(ref skipped, ref firstFailure,
                "a snapshot list had an unexpected native type");
            return 0;
        }
        var id = native.Identity(owner);
        if (id == Guid.Empty || !claimed.Add(id))
        {
            Skip(ref skipped, ref firstFailure,
                "a snapshot list had an empty or duplicate identity");
            return 0;
        }
        var values = alchemy
            ? native.AlchemySnapshotValues(owner)
            : native.EquipmentSnapshotValues(owner);
        frame.SnapshotLoadouts.Append(new WorldSnapshotLoadout(id,
            alchemy ? WorldSnapshotLoadoutKind.Alchemy : WorldSnapshotLoadoutKind.Equipment,
            values?.Count ?? 0));
        for (var slot = 0; slot < (values?.Count ?? 0); slot++)
        {
            var snapshot = values![slot];
            if (snapshot is null)
            {
                Skip(ref skipped, ref firstFailure,
                    "a snapshot slot was unavailable");
                continue;
            }
            var empty = alchemy
                ? native.AlchemySnapshotEmpty(snapshot)
                : native.EquipmentSnapshotEmpty(snapshot);
            frame.SnapshotSlots.Append(new WorldSnapshotSlot(id, slot, !empty));
            var record = alchemy
                ? native.AlchemySnapshotRecord(snapshot)
                : native.EquipmentSnapshotRecord(snapshot);
            var entries = alchemy
                ? record is null ? null : native.AlchemyRecordEntries(record)
                : record is null ? null : native.EquipmentRecordEntries(record);
            for (var index = 0; index < (entries?.Count ?? 0); index++)
            {
                var entry = entries![index]!;
                var expectedType = alchemy ? native.AlchemyRecipeType : native.EquipmentType;
                if (!LoadoutNativeBindings.TryReadEntry(entry, expectedType,
                        out var item, out var quantity) || item is null) continue;
                var entryId = native.Identity(item);
                if (entryId != Guid.Empty && quantity > 0)
                    frame.SnapshotEntries.Append(new WorldSnapshotEntry(
                        id, slot, entryId, quantity));
            }
        }
        return 1;
    }

    private static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}

internal static class WorldLoadoutDeriver
{
    internal static PublicationTable<WorldLoadoutEntry> BuildEntries(
        WorldRelationBuffer<WorldLoadoutEntry> source) =>
        WorldScribeRelationDeriver.Build(source, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var kind = left.Kind.CompareTo(right.Kind);
            return kind != 0 ? kind : left.EntryId.CompareTo(right.EntryId);
        });

    internal static PublicationTable<WorldSnapshotSlot> BuildSlots(
        WorldRelationBuffer<WorldSnapshotSlot> source) =>
        WorldScribeRelationDeriver.Build(source, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            return owner != 0 ? owner : left.Slot.CompareTo(right.Slot);
        });

    internal static PublicationTable<WorldSnapshotEntry> BuildSnapshotEntries(
        WorldRelationBuffer<WorldSnapshotEntry> source) =>
        WorldScribeRelationDeriver.Build(source, static (left, right) =>
        {
            var owner = left.OwnerId.CompareTo(right.OwnerId);
            if (owner != 0) return owner;
            var slot = left.Slot.CompareTo(right.Slot);
            return slot != 0 ? slot : left.EntryId.CompareTo(right.EntryId);
        });
}

internal static class WorldLoadoutLookup
{
    internal static bool TryFindPlayer(PublicationTable<WorldPlayerLoadout> rows,
        Guid id, out WorldPlayerLoadout value) => WorldLookup.TryFind(rows, id, out value);

    internal static bool TryFindSnapshot(PublicationTable<WorldSnapshotLoadout> rows,
        Guid id, out WorldSnapshotLoadout value) => WorldLookup.TryFind(rows, id, out value);
}

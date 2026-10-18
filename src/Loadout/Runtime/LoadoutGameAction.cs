using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for player loadouts and Equipment/Alchemy snapshots.</summary>
internal sealed class LoadoutGameAction : NativeGameAction<LoadoutNativeBindings>
{
    private const string Screen = "Workshop > Artifacts and Alchemy";

    private static readonly GameActionCodes Codes = new(
        LoadoutActionResultCodes.WrongThread,
        LoadoutActionResultCodes.ContractUnavailable,
        LoadoutActionResultCodes.MutationPermitUnavailable,
        LoadoutActionResultCodes.PostCommitFault,
        LoadoutActionResultCodes.VerificationFailed);

    private readonly EquipmentLoadoutGameAction _equipment;
    private readonly AlchemyLoadoutGameAction _alchemy;

    internal LoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        EquipmentLoadoutGameAction equipment,
        AlchemyLoadoutGameAction alchemy, Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
        : base("Loadout", Screen, Codes, readLifecycleEpoch, tryCaptureMutationPermit,
            readOwnershipFailure,
            (out LoadoutNativeBindings? bindings, out string reason) =>
                LoadoutNativeBindings.TryCreate(
                    out bindings, out reason, resolveType, includeContract))
    {
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _alchemy = alchemy ?? throw new ArgumentNullException(nameof(alchemy));
    }

    internal LoadoutSubmission Submit(in LoadoutAction action)
    {
        var request = action;
        return Admit(action.LifecycleEpoch, static outcome => new LoadoutSubmission(outcome),
            native => Press(in request, native));
    }

    private LoadoutSubmission Press(in LoadoutAction action, LoadoutNativeBindings native)
    {
        var manager = native.Manager();
        if (manager is null || manager.GetType() != native.ManagerType)
            return LoadoutSubmission.Reject(LoadoutActionResultCodes.ContractUnavailable,
                "Loadouts are not available in this scene.");
        object? target;
        var snapshotIsAlchemy = false;
        var expectedIndex = 0;

        if (action.Kind <= LoadoutActionKind.NextColor)
        {
            if (!TryFindPlayer(native, manager, action.TargetId, out target))
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.IdentityUnavailable,
                    "That player loadout is not present in the current game.");
            if (action.Kind == LoadoutActionKind.Select)
            {
                if (native.PlayerSelected(target!))
                    return LoadoutSubmission.Reject(LoadoutActionResultCodes.AlreadyInRequestedState,
                        native.PlayerName(target!) + " is already selected.");
                // The game's whole admission for a swap is CanSwapLoadouts() plus the
                // same-index early return above; LoadLoadout and SwapOnLoadout validate
                // nothing. The suite does not get to be stricter than the transaction.
                if (!native.CanSwap(manager))
                    return LoadoutSubmission.Reject(LoadoutActionResultCodes.SwitchBlocked,
                        "Finish casting or readying the active spell before switching loadouts.");
            }
            else
            {
                if (!native.PlayerSelected(target!))
                    return LoadoutSubmission.Reject(LoadoutActionResultCodes.WrongTargetType,
                        "Edit the currently selected player loadout.");
                var refusal = ValidatePlayerEdit(in action, native, target!, out expectedIndex);
                if (refusal.HasValue) return refusal.Value;
            }
        }
        else
        {
            if (!TryFindSnapshot(native, manager, action.TargetId, action.Slot,
                    out target, out snapshotIsAlchemy, out var refusal))
                return refusal;
            var reason = string.Empty;
            var empty = IsSnapshotEmpty(native, target!, snapshotIsAlchemy);
            if (action.Kind == LoadoutActionKind.SnapshotSave && !empty)
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.SlotOccupied,
                    "Clear snapshot slot " + (action.Slot + 1) + " before saving into it.");
            if (action.Kind is LoadoutActionKind.SnapshotLoad or LoadoutActionKind.SnapshotClear && empty)
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.SlotEmpty,
                    "Snapshot slot " + (action.Slot + 1) + " is empty.");

            // Nothing staged is answered before the record is validated. A save reads the
            // active section, and an empty one has no entry that could fail a limit — the
            // ordering, not the checks, is what made an empty section claim it overflowed.
            if (action.Kind == LoadoutActionKind.SnapshotSave &&
                IsActiveSectionEmpty(native, manager, snapshotIsAlchemy))
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.ActiveSectionEmpty,
                    "There is nothing staged in the active " +
                    (snapshotIsAlchemy ? "Alchemy" : "Equipment") +
                    " section to save.");
            if (action.Kind == LoadoutActionKind.SnapshotSave &&
                !TryValidateActive(action.LifecycleEpoch, native, manager, snapshotIsAlchemy, out reason))
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.EntryUnavailable, reason);
            if (action.Kind == LoadoutActionKind.SnapshotLoad)
            {
                if (!TryValidateSnapshot(action.LifecycleEpoch, native, manager, target!,
                        snapshotIsAlchemy, out reason))
                    return LoadoutSubmission.Reject(LoadoutActionResultCodes.EntryUnavailable, reason);
                if (RecordsEqual(native, ActiveRecord(native, manager, snapshotIsAlchemy),
                        SnapshotRecord(native, target!, snapshotIsAlchemy), snapshotIsAlchemy))
                    return LoadoutSubmission.Reject(LoadoutActionResultCodes.AlreadyInRequestedState,
                        "The active " + (snapshotIsAlchemy ? "Alchemy" : "Equipment") +
                        " section already matches snapshot slot " + (action.Slot + 1) + ".");
            }
        }

        if (!TryPermit(out var permitRefusal)) return new LoadoutSubmission(permitRefusal);
        var request = action;
        var subject = target!;
        return new LoadoutSubmission(Execute(EntityIdentityFormatter.PlayerName(action.TargetId),
            "The requested loadout transition is visible.", false,
            () => OutcomeObserved(in request, native, manager, subject, snapshotIsAlchemy, expectedIndex),
            () => Call(in request, native, manager, subject, snapshotIsAlchemy, expectedIndex),
            static (_, observed) => observed));
    }

    private static LoadoutSubmission? ValidatePlayerEdit(in LoadoutAction action,
        LoadoutNativeBindings native, object player, out int expectedIndex)
    {
        expectedIndex = 0;
        if (action.Kind == LoadoutActionKind.SetEquipmentSection &&
            native.EquipmentEnabled(player) == action.Enabled)
            return LoadoutSubmission.Reject(LoadoutActionResultCodes.AlreadyInRequestedState,
                "Equipment saving is already " + (action.Enabled ? "on." : "off."));
        if (action.Kind == LoadoutActionKind.SetAlchemySection &&
            native.AlchemyEnabled(player) == action.Enabled)
            return LoadoutSubmission.Reject(LoadoutActionResultCodes.AlreadyInRequestedState,
                "Alchemy saving is already " + (action.Enabled ? "on." : "off."));
        var label = native.PlayerLabel(player);
        if (label is null)
            return LoadoutSubmission.Reject(LoadoutActionResultCodes.ContractUnavailable,
                "The selected loadout label is unavailable.");
        if (action.Kind == LoadoutActionKind.Rename)
        {
            if (action.Name.Length > 24)
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.NameOutOfRange,
                    "Loadout names may contain at most 24 characters.");
            if (native.LabelName(label) == action.Name)
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.AlreadyInRequestedState,
                    "The loadout already has that name.");
        }
        else if (action.Kind == LoadoutActionKind.NextIcon)
        {
            var count = native.CustomIcons()?.Count ?? 0;
            if (count <= 0)
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.ContractUnavailable,
                    "No loadout icons are available.");
            expectedIndex = (native.LabelIcon(label) + 1) % count;
        }
        else if (action.Kind == LoadoutActionKind.NextColor)
        {
            var count = native.CustomColors()?.Count ?? 0;
            if (count <= 0)
                return LoadoutSubmission.Reject(LoadoutActionResultCodes.ContractUnavailable,
                    "No loadout colors are available.");
            expectedIndex = (native.LabelColor(label) + 1) % count;
        }
        return null;
    }

    private static void Call(in LoadoutAction action, LoadoutNativeBindings native,
        object manager, object target, bool alchemySnapshot, int expectedIndex)
    {
        switch (action.Kind)
        {
            case LoadoutActionKind.Select:
                native.SetLoadout(manager, target);
                break;
            case LoadoutActionKind.SetEquipmentSection:
                native.SetEquipmentEnabled(target, action.Enabled);
                if (action.Enabled) native.SaveActive(manager);
                break;
            case LoadoutActionKind.SetAlchemySection:
                native.SetAlchemyEnabled(target, action.Enabled);
                if (action.Enabled) native.SaveActive(manager);
                break;
            case LoadoutActionKind.Rename:
                native.SetLabelName(native.PlayerLabel(target)!, action.Name);
                break;
            case LoadoutActionKind.NextIcon:
                native.SetLabelIcon(native.PlayerLabel(target)!, expectedIndex);
                break;
            case LoadoutActionKind.NextColor:
                native.SetLabelColor(native.PlayerLabel(target)!, expectedIndex);
                break;
            case LoadoutActionKind.SnapshotSave:
                var active = ActiveRecord(native, manager, alchemySnapshot);
                if (alchemySnapshot) native.SaveAlchemySnapshot(target, active);
                else native.SaveEquipmentSnapshot(target, active);
                break;
            case LoadoutActionKind.SnapshotLoad:
                var saved = SnapshotRecord(native, target, alchemySnapshot);
                if (alchemySnapshot) native.SetAlchemyRecord(native.ActiveAlchemy(manager)!, saved);
                else native.SetEquipmentRecord(native.ActiveEquipment(manager)!, saved);
                break;
            case LoadoutActionKind.SnapshotClear:
                if (alchemySnapshot) native.ClearAlchemySnapshot(target);
                else native.ClearEquipmentSnapshot(target);
                break;
        }
    }

    private bool TryValidateActive(long epoch, LoadoutNativeBindings native, object manager,
        bool alchemy, out string reason)
    {
        reason = string.Empty;
        if (alchemy)
        {
            var list = native.ActiveAlchemy(manager);
            var record = list is null ? null : native.CreateAlchemyRecord(list);
            return list is not null && record is not null &&
                TryValidateAlchemy(epoch, native, record, list, out _, out _, out reason);
        }
        var equipment = native.ActiveEquipment(manager);
        var equipmentRecord = equipment is null ? null : native.CreateEquipmentRecord(equipment);
        return equipment is not null && equipmentRecord is not null &&
            TryValidateEquipment(epoch, native, equipmentRecord, equipment, out _, out _, out reason);
    }

    private bool TryValidateSnapshot(long epoch, LoadoutNativeBindings native, object manager,
        object snapshot, bool alchemy, out string reason)
    {
        reason = string.Empty;
        var record = SnapshotRecord(native, snapshot, alchemy);
        if (alchemy)
        {
            var list = native.ActiveAlchemy(manager);
            return list is not null &&
                TryValidateAlchemy(epoch, native, record, list, out var target, out var current, out reason) &&
                UsageFits(native, target, current, "saved Alchemy snapshot", out reason);
        }
        var equipment = native.ActiveEquipment(manager);
        return equipment is not null &&
            TryValidateEquipment(epoch, native, record, equipment,
                out var equipmentTarget, out var equipmentCurrent, out reason) &&
            UsageFits(native, equipmentTarget, equipmentCurrent,
                "saved Equipment snapshot", out reason);
    }

    private static bool UsageFits(LoadoutNativeBindings native, object target,
        object current, string subject, out string reason)
    {
        if (native.HasEnough(native.SubtractCost(target, current)))
        {
            reason = string.Empty;
            return true;
        }
        reason = "The " + subject + " exceeds the resources currently available for usage.";
        return false;
    }

    private bool TryValidateEquipment(long epoch, LoadoutNativeBindings native, object record,
        object activeList, out object targetCost, out object currentCost, out string reason)
    {
        reason = string.Empty;
        targetCost = native.CreateCost();
        currentCost = native.CreateCost();
        var entries = native.EquipmentRecordEntries(record);
        if (entries is null)
        {
            reason = "The saved Equipment entries are unavailable.";
            return false;
        }
        var maximum = native.EquipmentMaximum(activeList);
        if (entries.Count > maximum)
        {
            reason = "The saved Equipment section uses " + entries.Count +
                " slots, but only " + maximum + " are available.";
            return false;
        }
        var typeCounts = new List<(object Type, int Count, int Maximum)>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index]!;
            if (!LoadoutNativeShapes.TryReadEntry(entry, native.EquipmentType,
                    out var item, out var quantity) || item is null ||
                !_equipment.TryValidateStoredEntry(epoch, item, quantity,
                    out var type, out var typeMaximum, out var cost, out reason) ||
                type is null || cost is null) return false;
            // A type slot is one artifact, however deep its stack. Native
            // EquipmentListVariable.GetTypesEquipped counts list entries and
            // EquipmentTypeSO.GetMaxTypeSlots bounds that count, while StackableListVariable.Stack
            // adds an item to the list once and its quantity to a separate record. Counting stacks
            // here refused live sections the game itself equipped.
            AddType(typeCounts, type, 1, typeMaximum);
            targetCost = native.AddCost(targetCost,
                native.MultiplyCost(cost, new BigDouble(quantity)));
        }
        for (var index = 0; index < typeCounts.Count; index++)
        {
            if (typeCounts[index].Count <= typeCounts[index].Maximum) continue;
            reason = "The saved Equipment section exceeds one artifact type's slot limit.";
            return false;
        }
        var current = native.CreateEquipmentRecord(activeList);
        if (current is null || !TrySumEquipment(epoch, native, current, ref currentCost, out reason))
            return false;
        reason = string.Empty;
        return true;
    }

    private bool TrySumEquipment(long epoch, LoadoutNativeBindings native, object record,
        ref object total, out string reason)
    {
        reason = string.Empty;
        var entries = native.EquipmentRecordEntries(record);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index]!;
            if (!LoadoutNativeShapes.TryReadEntry(entry, native.EquipmentType,
                    out var item, out var quantity) || item is null ||
                !_equipment.TryValidateStoredEntry(epoch, item, quantity,
                    out _, out _, out var cost, out reason) || cost is null) return false;
            total = native.AddCost(total, native.MultiplyCost(cost, new BigDouble(quantity)));
        }
        reason = string.Empty;
        return true;
    }

    private bool TryValidateAlchemy(long epoch, LoadoutNativeBindings native, object record,
        object activeList, out object targetCost, out object currentCost, out string reason)
    {
        reason = string.Empty;
        targetCost = native.CreateCost();
        currentCost = native.CreateCost();
        var entries = native.AlchemyRecordEntries(record);
        if (entries is null)
        {
            reason = "The saved Alchemy entries are unavailable.";
            return false;
        }
        var maximum = native.AlchemyMaximum(activeList);
        if (entries.Count > maximum)
        {
            reason = "The saved Alchemy section uses " + entries.Count +
                " slots, but only " + maximum + " are available.";
            return false;
        }
        if (!TrySumAlchemy(epoch, native, record, ref targetCost, out reason)) return false;
        var current = native.CreateAlchemyRecord(activeList);
        if (current is null || !TrySumAlchemy(epoch, native, current, ref currentCost, out reason))
            return false;
        reason = string.Empty;
        return true;
    }

    private bool TrySumAlchemy(long epoch, LoadoutNativeBindings native, object record,
        ref object total, out string reason)
    {
        reason = string.Empty;
        var entries = native.AlchemyRecordEntries(record);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index]!;
            if (!LoadoutNativeShapes.TryReadEntry(entry, native.AlchemyRecipeType,
                    out var item, out var quantity) || item is null ||
                !_alchemy.TryValidateStoredEntry(epoch, item, quantity,
                    out var free, out var cost, out reason) || cost is null) return false;
            total = native.AddCost(total, native.MultiplyCost(cost,
                new BigDouble(Math.Max(quantity - free, 0))));
        }
        reason = string.Empty;
        return true;
    }

    private static void AddType(List<(object Type, int Count, int Maximum)> values,
        object type, int count, int maximum)
    {
        for (var index = 0; index < values.Count; index++)
        {
            if (!ReferenceEquals(values[index].Type, type)) continue;
            values[index] = (type, checked(values[index].Count + count),
                Math.Min(values[index].Maximum, maximum));
            return;
        }
        values.Add((type, count, maximum));
    }

    private static bool TryFindPlayer(LoadoutNativeBindings native, object manager,
        Guid id, out object? player)
    {
        player = null;
        var list = native.PlayerLoadouts(manager);
        var values = list is null ? null : native.PlayerValues(list);
        for (var index = 0; index < (values?.Count ?? 0); index++)
        {
            var candidate = values![index];
            if (candidate is null || candidate.GetType() != native.PlayerLoadoutType ||
                native.PlayerId(candidate) != id) continue;
            player = candidate;
            return true;
        }
        return false;
    }

    private static bool TryFindSnapshot(LoadoutNativeBindings native, object manager,
        Guid ownerId, int slot, out object? snapshot, out bool alchemy,
        out LoadoutSubmission refusal)
    {
        var owner = native.AlchemySnapshots(manager);
        alchemy = true;
        if (owner is null || owner.GetType() != native.AlchemySnapshotListType ||
            native.Identity(owner) != ownerId)
        {
            owner = native.EquipmentSnapshots(manager);
            alchemy = false;
        }
        if (owner is null || native.Identity(owner) != ownerId)
        {
            snapshot = null;
            refusal = LoadoutSubmission.Reject(LoadoutActionResultCodes.IdentityUnavailable,
                "That Equipment or Alchemy snapshot list is not present in the current game.");
            return false;
        }
        var values = alchemy
            ? native.AlchemySnapshotValues(owner)
            : native.EquipmentSnapshotValues(owner);
        var count = values?.Count ?? 0;
        if (slot < 0 || slot >= count)
        {
            snapshot = null;
            refusal = LoadoutSubmission.RejectSlotOutOfRange(
                count == 0
                    ? "This save owns zero snapshot slots."
                    : "The snapshot slot must be between 1 and " + count + ".",
                count == 0 ? -1 : 1,
                count);
            return false;
        }
        snapshot = values![slot];
        refusal = LoadoutSubmission.Reject(LoadoutActionResultCodes.ContractUnavailable,
            "The requested snapshot slot is unavailable.");
        return snapshot is not null;
    }

    private static object ActiveRecord(LoadoutNativeBindings native, object manager,
        bool alchemy) => alchemy
        ? native.CreateAlchemyRecord(native.ActiveAlchemy(manager)!)!
        : native.CreateEquipmentRecord(native.ActiveEquipment(manager)!)!;

    private static object SnapshotRecord(LoadoutNativeBindings native, object snapshot,
        bool alchemy) => alchemy
        ? native.AlchemySnapshotRecord(snapshot)!
        : native.EquipmentSnapshotRecord(snapshot)!;

    private static bool IsSnapshotEmpty(LoadoutNativeBindings native, object snapshot,
        bool alchemy) => alchemy
        ? native.AlchemySnapshotEmpty(snapshot)
        : native.EquipmentSnapshotEmpty(snapshot);

    private static bool IsActiveSectionEmpty(
        LoadoutNativeBindings native,
        object manager,
        bool alchemy)
    {
        var record = ActiveRecord(native, manager, alchemy);
        var entries = alchemy
            ? native.AlchemyRecordEntries(record)
            : native.EquipmentRecordEntries(record);
        return entries?.Count == 0;
    }

    private static bool RecordsEqual(LoadoutNativeBindings native, object left,
        object right, bool alchemy)
    {
        var first = alchemy ? native.AlchemyRecordEntries(left) : native.EquipmentRecordEntries(left);
        var second = alchemy ? native.AlchemyRecordEntries(right) : native.EquipmentRecordEntries(right);
        if (first is null || second is null || first.Count != second.Count) return false;
        for (var index = 0; index < first.Count; index++)
        {
            var expectedType = alchemy ? native.AlchemyRecipeType : native.EquipmentType;
            if (!LoadoutNativeShapes.TryReadEntry(first[index]!, expectedType,
                    out var item, out var count)) return false;
            var found = false;
            for (var candidate = 0; candidate < second.Count; candidate++)
            {
                if (!LoadoutNativeShapes.TryReadEntry(second[candidate]!, expectedType,
                        out var other, out var otherCount)) return false;
                if (!ReferenceEquals(item, other) || count != otherCount) continue;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }

    private static bool OutcomeObserved(in LoadoutAction action,
        LoadoutNativeBindings native, object manager, object target,
        bool alchemySnapshot, int expectedIndex) => action.Kind switch
    {
        LoadoutActionKind.Select => native.PlayerSelected(target),
        LoadoutActionKind.SetEquipmentSection => native.EquipmentEnabled(target) == action.Enabled,
        LoadoutActionKind.SetAlchemySection => native.AlchemyEnabled(target) == action.Enabled,
        LoadoutActionKind.Rename => native.LabelName(native.PlayerLabel(target)!) == action.Name,
        LoadoutActionKind.NextIcon => native.LabelIcon(native.PlayerLabel(target)!) == expectedIndex,
        LoadoutActionKind.NextColor => native.LabelColor(native.PlayerLabel(target)!) == expectedIndex,
        LoadoutActionKind.SnapshotSave => !IsSnapshotEmpty(native, target, alchemySnapshot),
        LoadoutActionKind.SnapshotClear => IsSnapshotEmpty(native, target, alchemySnapshot),
        LoadoutActionKind.SnapshotLoad => RecordsEqual(native,
            ActiveRecord(native, manager, alchemySnapshot),
            SnapshotRecord(native, target, alchemySnapshot), alchemySnapshot),
        _ => false,
    };
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>Unity-main-thread boundary for player loadouts and Equipment/Alchemy snapshots.</summary>
internal sealed class LoadoutGameAction : IDisposable
{
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<bool> _tryCaptureMutationPermit;
    private readonly Func<string> _readOwnershipFailure;
    private readonly EquipmentLoadoutGameAction _equipment;
    private readonly AlchemyLoadoutGameAction _alchemy;
    private readonly Func<string, Type?>? _resolveType;
    private readonly Func<string, bool>? _includeContract;
    private readonly int _mainThreadId;
    private LoadoutNativeBindings? _bindings;
    private string _bindingFailure = string.Empty;

    internal LoadoutGameAction(Func<long> readLifecycleEpoch,
        Func<bool> tryCaptureMutationPermit, Func<string> readOwnershipFailure,
        EquipmentLoadoutGameAction equipment,
        AlchemyLoadoutGameAction alchemy, Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _tryCaptureMutationPermit = tryCaptureMutationPermit ?? throw new ArgumentNullException(nameof(tryCaptureMutationPermit));
        _readOwnershipFailure = readOwnershipFailure ?? throw new ArgumentNullException(nameof(readOwnershipFailure));
        _equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
        _alchemy = alchemy ?? throw new ArgumentNullException(nameof(alchemy));
        _resolveType = resolveType;
        _includeContract = includeContract;
        _mainThreadId = Environment.CurrentManagedThreadId;
        BindLifecycle();
    }

    internal bool BindingsAvailable => _bindings is not null;
    internal string BindingFailure => _bindingFailure;

    internal LoadoutSubmission Submit(in LoadoutAction action)
    {
        if (Environment.CurrentManagedThreadId != _mainThreadId)
            return Reject(LoadoutPreflight.WrongThread,
                GameActionAnswer.SuiteStopped());
        if (_bindings is not { } native)
            return Reject(LoadoutPreflight.ContractUnavailable,
                GameActionAnswer.NotAttached("Workshop > Artifacts and Alchemy"));
        long epoch;
        try { epoch = _readLifecycleEpoch(); }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(LoadoutPreflight.LifecycleReplaced,
                GameActionAnswer.CouldNotRead("Workshop > Artifacts and Alchemy", exception));
        }
        if (action.LifecycleEpoch != epoch)
            return Reject(LoadoutPreflight.LifecycleReplaced,
                GameActionAnswer.RunChanged());

        try
        {
            var manager = native.Manager();
            if (manager is null || manager.GetType() != native.ManagerType)
                return Reject(LoadoutPreflight.ContractUnavailable,
                    "Loadouts are not available in this scene.");
            object? target;
            var snapshotIsAlchemy = false;
            var expectedIndex = 0;

            if (action.Kind <= LoadoutActionKind.NextColor)
            {
                if (!TryFindPlayer(native, manager, action.TargetId, out target))
                    return Reject(LoadoutPreflight.IdentityUnavailable,
                        "That player loadout is not present in the current game.");
                if (action.Kind == LoadoutActionKind.Select)
                {
                    if (native.PlayerSelected(target!))
                        return Reject(LoadoutPreflight.AlreadyInRequestedState,
                            native.PlayerName(target!) + " is already selected.");
                    // The game's whole admission for a swap is CanSwapLoadouts() plus the
                    // same-index early return above; LoadLoadout and SwapOnLoadout validate
                    // nothing. The suite does not get to be stricter than the transaction.
                    if (!native.CanSwap(manager))
                        return Reject(LoadoutPreflight.SwitchBlocked,
                            "Finish casting or readying the active spell before switching loadouts.");
                }
                else
                {
                    if (!native.PlayerSelected(target!))
                        return Reject(LoadoutPreflight.WrongTargetType,
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
                    return Reject(LoadoutPreflight.SlotOccupied,
                        "Clear snapshot slot " + (action.Slot + 1) + " before saving into it.");
                if (action.Kind is LoadoutActionKind.SnapshotLoad or LoadoutActionKind.SnapshotClear && empty)
                    return Reject(LoadoutPreflight.SlotEmpty,
                        "Snapshot slot " + (action.Slot + 1) + " is empty.");

                // Nothing staged is answered before the record is validated. A save reads the
                // active section, and an empty one has no entry that could fail a limit — the
                // ordering, not the checks, is what made an empty section claim it overflowed.
                if (action.Kind == LoadoutActionKind.SnapshotSave &&
                    IsActiveSectionEmpty(native, manager, snapshotIsAlchemy))
                    return Reject(LoadoutPreflight.ActiveSectionEmpty,
                        "There is nothing staged in the active " +
                        (snapshotIsAlchemy ? "Alchemy" : "Equipment") +
                        " section to save.");
                if (action.Kind == LoadoutActionKind.SnapshotSave &&
                    !TryValidateActive(native, manager, snapshotIsAlchemy, out reason))
                    return Reject(LoadoutPreflight.EntryUnavailable, reason);
                if (action.Kind == LoadoutActionKind.SnapshotLoad)
                {
                    if (!TryValidateSnapshot(native, manager, target!, snapshotIsAlchemy, out reason))
                        return Reject(LoadoutPreflight.EntryUnavailable, reason);
                    if (RecordsEqual(native, ActiveRecord(native, manager, snapshotIsAlchemy),
                            SnapshotRecord(native, target!, snapshotIsAlchemy), snapshotIsAlchemy))
                        return Reject(LoadoutPreflight.AlreadyInRequestedState,
                            "The active " + (snapshotIsAlchemy ? "Alchemy" : "Equipment") +
                            " section already matches snapshot slot " + (action.Slot + 1) + ".");
                }
            }

            if (!_tryCaptureMutationPermit())
                return Reject(LoadoutPreflight.MutationPermitUnavailable, _readOwnershipFailure());
            return Execute(in action, native, manager, target!, snapshotIsAlchemy, expectedIndex);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            return Reject(LoadoutPreflight.ContractUnavailable,
                GameActionAnswer.CouldNotRead("Workshop > Artifacts and Alchemy", exception));
        }
    }

    internal void InvalidateLifecycle()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
        BindLifecycle();
    }

    public void Dispose()
    {
        _bindings = null;
        _bindingFailure = string.Empty;
    }

    private static LoadoutSubmission? ValidatePlayerEdit(in LoadoutAction action,
        LoadoutNativeBindings native, object player, out int expectedIndex)
    {
        expectedIndex = 0;
        if (action.Kind == LoadoutActionKind.SetEquipmentSection &&
            native.EquipmentEnabled(player) == action.Enabled)
            return Reject(LoadoutPreflight.AlreadyInRequestedState,
                "Equipment saving is already " + (action.Enabled ? "on." : "off."));
        if (action.Kind == LoadoutActionKind.SetAlchemySection &&
            native.AlchemyEnabled(player) == action.Enabled)
            return Reject(LoadoutPreflight.AlreadyInRequestedState,
                "Alchemy saving is already " + (action.Enabled ? "on." : "off."));
        var label = native.PlayerLabel(player);
        if (label is null)
            return Reject(LoadoutPreflight.ContractUnavailable,
                "The selected loadout label is unavailable.");
        if (action.Kind == LoadoutActionKind.Rename)
        {
            if (action.Name.Length > 24)
                return Reject(LoadoutPreflight.NameOutOfRange,
                    "Loadout names may contain at most 24 characters.");
            if (native.LabelName(label) == action.Name)
                return Reject(LoadoutPreflight.AlreadyInRequestedState,
                    "The loadout already has that name.");
        }
        else if (action.Kind == LoadoutActionKind.NextIcon)
        {
            var count = native.CustomIcons()?.Count ?? 0;
            if (count <= 0)
                return Reject(LoadoutPreflight.ContractUnavailable,
                    "No loadout icons are available.");
            expectedIndex = (native.LabelIcon(label) + 1) % count;
        }
        else if (action.Kind == LoadoutActionKind.NextColor)
        {
            var count = native.CustomColors()?.Count ?? 0;
            if (count <= 0)
                return Reject(LoadoutPreflight.ContractUnavailable,
                    "No loadout colors are available.");
            expectedIndex = (native.LabelColor(label) + 1) % count;
        }
        return null;
    }

    private LoadoutSubmission Execute(in LoadoutAction action, LoadoutNativeBindings native,
        object manager, object target, bool alchemySnapshot, int expectedIndex)
    {
        var stage = LoadoutNativeStage.NativeCallback;
        try
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
            stage = LoadoutNativeStage.Verification;
            return OutcomeObserved(in action, native, manager, target,
                    alchemySnapshot, expectedIndex)
                ? Verified()
                : Fault(in action, LoadoutPreflight.VerificationFailed, stage,
                    NativeMutationOutcome.PostconditionFailed,
                    GameActionAnswer.ChangeNotSeen("Workshop > Artifacts and Alchemy"));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            if (OutcomeObserved(in action, native, manager, target,
                    alchemySnapshot, expectedIndex)) return Verified();
            return Fault(in action, LoadoutPreflight.PostCommitFault, stage,
                NativeMutationOutcome.ExecutionThrew,
                GameActionAnswer.GameErrored("Workshop > Artifacts and Alchemy", exception));
        }
    }

    private bool TryValidateActive(LoadoutNativeBindings native, object manager,
        bool alchemy, out string reason)
    {
        reason = string.Empty;
        if (alchemy)
        {
            var list = native.ActiveAlchemy(manager);
            var record = list is null ? null : native.CreateAlchemyRecord(list);
            return list is not null && record is not null &&
                TryValidateAlchemy(native, record, list, out _, out _, out reason);
        }
        var equipment = native.ActiveEquipment(manager);
        var equipmentRecord = equipment is null ? null : native.CreateEquipmentRecord(equipment);
        return equipment is not null && equipmentRecord is not null &&
            TryValidateEquipment(native, equipmentRecord, equipment, out _, out _, out reason);
    }

    private bool TryValidateSnapshot(LoadoutNativeBindings native, object manager,
        object snapshot, bool alchemy, out string reason)
    {
        reason = string.Empty;
        var record = SnapshotRecord(native, snapshot, alchemy);
        if (alchemy)
        {
            var list = native.ActiveAlchemy(manager);
            return list is not null &&
                TryValidateAlchemy(native, record, list, out var target, out var current, out reason) &&
                UsageFits(native, target, current, "saved Alchemy snapshot", out reason);
        }
        var equipment = native.ActiveEquipment(manager);
        return equipment is not null &&
            TryValidateEquipment(native, record, equipment,
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

    private bool TryValidateEquipment(LoadoutNativeBindings native, object record,
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
            if (!LoadoutNativeBindings.TryReadEntry(entry, native.EquipmentType,
                    out var item, out var quantity) || item is null ||
                !_equipment.TryValidateStoredEntry(item, quantity,
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
        if (current is null || !TrySumEquipment(native, current, ref currentCost, out reason))
            return false;
        reason = string.Empty;
        return true;
    }

    private bool TrySumEquipment(LoadoutNativeBindings native, object record,
        ref object total, out string reason)
    {
        reason = string.Empty;
        var entries = native.EquipmentRecordEntries(record);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index]!;
            if (!LoadoutNativeBindings.TryReadEntry(entry, native.EquipmentType,
                    out var item, out var quantity) || item is null ||
                !_equipment.TryValidateStoredEntry(item, quantity,
                    out _, out _, out var cost, out reason) || cost is null) return false;
            total = native.AddCost(total, native.MultiplyCost(cost, new BigDouble(quantity)));
        }
        reason = string.Empty;
        return true;
    }

    private bool TryValidateAlchemy(LoadoutNativeBindings native, object record,
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
        if (!TrySumAlchemy(native, record, ref targetCost, out reason)) return false;
        var current = native.CreateAlchemyRecord(activeList);
        if (current is null || !TrySumAlchemy(native, current, ref currentCost, out reason))
            return false;
        reason = string.Empty;
        return true;
    }

    private bool TrySumAlchemy(LoadoutNativeBindings native, object record,
        ref object total, out string reason)
    {
        reason = string.Empty;
        var entries = native.AlchemyRecordEntries(record);
        for (var index = 0; index < (entries?.Count ?? 0); index++)
        {
            var entry = entries![index]!;
            if (!LoadoutNativeBindings.TryReadEntry(entry, native.AlchemyRecipeType,
                    out var item, out var quantity) || item is null ||
                !_alchemy.TryValidateStoredEntry(item, quantity,
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
            refusal = Reject(LoadoutPreflight.IdentityUnavailable,
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
        refusal = Reject(LoadoutPreflight.ContractUnavailable,
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
            if (!LoadoutNativeBindings.TryReadEntry(first[index]!, expectedType,
                    out var item, out var count)) return false;
            var found = false;
            for (var candidate = 0; candidate < second.Count; candidate++)
            {
                if (!LoadoutNativeBindings.TryReadEntry(second[candidate]!, expectedType,
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

    private static LoadoutSubmission Reject(LoadoutPreflight preflight, string reason) =>
        LoadoutSubmission.Reject(preflight, reason);

    private static LoadoutSubmission Verified() =>
        new(LoadoutPreflight.Proceeded, LoadoutNativeStage.Verification,
            NativeMutationOutcome.Verified, new NativeMutationCallOutcome(1, 1, 1),
            "The requested loadout transition is visible.");

    private static LoadoutSubmission Fault(in LoadoutAction action,
        LoadoutPreflight preflight, LoadoutNativeStage stage,
        NativeMutationOutcome outcome, string reason) =>
        new(preflight, stage, outcome, new NativeMutationCallOutcome(1, 1, 0),
            "Loadout " + stage + " failed on " + action.TargetId + ": " + reason);

    private void BindLifecycle()
    {
        if (LoadoutNativeBindings.TryCreate(out var bindings, out var reason,
                _resolveType, _includeContract))
        {
            _bindings = bindings;
            _bindingFailure = string.Empty;
            return;
        }
        _bindings = null;
        _bindingFailure = reason;
    }

    private static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or
            TargetInvocationException or OverflowException;
}

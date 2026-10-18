using System;
using System.Collections;
using System.Collections.Generic;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>Complete lifecycle binding set for player loadout and snapshot mutation.</summary>
internal sealed class LoadoutNativeBindings
{
    internal static readonly string[] ContractIds =
    {
        "loadout.manager.type-action",
        "loadout.player-list.type-action",
        "loadout.player.type-action",
        "loadout.label.type-action",
        "loadout.spell.type-action",
        "loadout.spell-list.type-action",
        "loadout.equipment.type-action",
        "loadout.alchemy-recipe.type-action",
        "loadout.alchemy-snapshot-list.type-action",
        "loadout.equipment-snapshot-list.type-action",
        "loadout.alchemy-snapshot.type-action",
        "loadout.equipment-snapshot.type-action",
        "loadout.alchemy-list.type-action",
        "loadout.equipment-list.type-action",
        "loadout.cost.type-action",
        "loadout.global-variables.type-action",
        "loadout.identity.type-action",
        "loadout.stacked-record.type-action",
        "loadout.manager-instance-action",
        "loadout.manager-player-list-action",
        "loadout.manager-alchemy-snapshots-action",
        "loadout.manager-equipment-snapshots-action",
        "loadout.manager-active-alchemy-action",
        "loadout.manager-active-equipment-action",
        "loadout.manager-active-spells-action",
        "loadout.manager-set-loadout-action",
        "loadout.manager-save-active-action",
        "loadout.list-values-action",
        "loadout.player-guid-action",
        "loadout.player-name-action",
        "loadout.player-selected-action",
        "loadout.player-equipment-enabled-action",
        "loadout.player-alchemy-enabled-action",
        "loadout.player-label-action",
        "loadout.player-set-equipment-action",
        "loadout.player-set-alchemy-action",
        "loadout.label-name-action",
        "loadout.label-icon-action",
        "loadout.label-color-action",
        "loadout.label-set-name-action",
        "loadout.label-set-icon-action",
        "loadout.label-set-color-action",
        "loadout.global-custom-icons-action",
        "loadout.global-custom-colors-action",
        "loadout.identity-guid-action",
        "loadout.spell-casting-action",
        "loadout.spell-readying-cast-action",
        "loadout.alchemy-list-maximum-action",
        "loadout.equipment-list-maximum-action",
        "loadout.alchemy-snapshot-empty-action",
        "loadout.equipment-snapshot-empty-action",
        "loadout.alchemy-snapshot-clear-action",
        "loadout.equipment-snapshot-clear-action",
        "loadout.alchemy-snapshot-record-action",
        "loadout.equipment-snapshot-record-action",
        "loadout.alchemy-snapshot-save-action",
        "loadout.equipment-snapshot-save-action",
        "loadout.alchemy-active-record-action",
        "loadout.alchemy-active-set-action",
        "loadout.equipment-active-record-action",
        "loadout.equipment-active-set-action",
        "loadout.alchemy-record-entries-action",
        "loadout.equipment-record-entries-action",
        "loadout.cost-construct-action",
        "loadout.cost-add-action",
        "loadout.cost-subtract-action",
        "loadout.cost-multiply-action",
        "loadout.cost-enough-action",
    };

    private LoadoutNativeBindings(
        Type managerType,
        Type playerLoadoutType,
        Type equipmentType,
        Type alchemyRecipeType,
        Type alchemySnapshotListType,
        Func<object?> manager,
        Func<object, object?> playerLoadouts,
        Func<object, object?> alchemySnapshots,
        Func<object, object?> equipmentSnapshots,
        Func<object, object?> activeAlchemy,
        Func<object, object?> activeEquipment,
        Func<object, bool> canSwap,
        Action<object, object> setLoadout,
        Action<object> saveActive,
        Func<object, IList?> playerValues,
        Func<object, Guid> playerId,
        Func<object, string> playerName,
        Func<object, bool> playerSelected,
        Func<object, bool> equipmentEnabled,
        Func<object, bool> alchemyEnabled,
        Func<object, object?> playerLabel,
        Action<object, bool> setEquipmentEnabled,
        Action<object, bool> setAlchemyEnabled,
        Func<object, string> labelName,
        Func<object, int> labelIcon,
        Func<object, int> labelColor,
        Action<object, string> setLabelName,
        Action<object, int> setLabelIcon,
        Action<object, int> setLabelColor,
        Func<IList?> customIcons,
        Func<IList?> customColors,
        Func<object, Guid> identity,
        Func<object, int> alchemyMaximum,
        Func<object, int> equipmentMaximum,
        Func<object, IList?> alchemySnapshotValues,
        Func<object, IList?> equipmentSnapshotValues,
        Func<object, bool> alchemySnapshotEmpty,
        Func<object, bool> equipmentSnapshotEmpty,
        Action<object> clearAlchemySnapshot,
        Action<object> clearEquipmentSnapshot,
        Func<object, object?> alchemySnapshotRecord,
        Func<object, object?> equipmentSnapshotRecord,
        Action<object, object> saveAlchemySnapshot,
        Action<object, object> saveEquipmentSnapshot,
        Func<object, object?> createAlchemyRecord,
        Action<object, object> setAlchemyRecord,
        Func<object, object?> createEquipmentRecord,
        Action<object, object> setEquipmentRecord,
        Func<object, IList?> alchemyRecordEntries,
        Func<object, IList?> equipmentRecordEntries,
        Func<object> createCost,
        Func<object, object, object> addCost,
        Func<object, object, object> subtractCost,
        Func<object, BigDouble, object> multiplyCost,
        Func<object, bool> hasEnough)
    {
        ManagerType = managerType;
        PlayerLoadoutType = playerLoadoutType;
        EquipmentType = equipmentType;
        AlchemyRecipeType = alchemyRecipeType;
        AlchemySnapshotListType = alchemySnapshotListType;
        Manager = manager;
        PlayerLoadouts = playerLoadouts;
        AlchemySnapshots = alchemySnapshots;
        EquipmentSnapshots = equipmentSnapshots;
        ActiveAlchemy = activeAlchemy;
        ActiveEquipment = activeEquipment;
        CanSwap = canSwap;
        SetLoadout = setLoadout;
        SaveActive = saveActive;
        PlayerValues = playerValues;
        PlayerId = playerId;
        PlayerName = playerName;
        PlayerSelected = playerSelected;
        EquipmentEnabled = equipmentEnabled;
        AlchemyEnabled = alchemyEnabled;
        PlayerLabel = playerLabel;
        SetEquipmentEnabled = setEquipmentEnabled;
        SetAlchemyEnabled = setAlchemyEnabled;
        LabelName = labelName;
        LabelIcon = labelIcon;
        LabelColor = labelColor;
        SetLabelName = setLabelName;
        SetLabelIcon = setLabelIcon;
        SetLabelColor = setLabelColor;
        CustomIcons = customIcons;
        CustomColors = customColors;
        Identity = identity;
        AlchemyMaximum = alchemyMaximum;
        EquipmentMaximum = equipmentMaximum;
        AlchemySnapshotValues = alchemySnapshotValues;
        EquipmentSnapshotValues = equipmentSnapshotValues;
        AlchemySnapshotEmpty = alchemySnapshotEmpty;
        EquipmentSnapshotEmpty = equipmentSnapshotEmpty;
        ClearAlchemySnapshot = clearAlchemySnapshot;
        ClearEquipmentSnapshot = clearEquipmentSnapshot;
        AlchemySnapshotRecord = alchemySnapshotRecord;
        EquipmentSnapshotRecord = equipmentSnapshotRecord;
        SaveAlchemySnapshot = saveAlchemySnapshot;
        SaveEquipmentSnapshot = saveEquipmentSnapshot;
        CreateAlchemyRecord = createAlchemyRecord;
        SetAlchemyRecord = setAlchemyRecord;
        CreateEquipmentRecord = createEquipmentRecord;
        SetEquipmentRecord = setEquipmentRecord;
        AlchemyRecordEntries = alchemyRecordEntries;
        EquipmentRecordEntries = equipmentRecordEntries;
        CreateCost = createCost;
        AddCost = addCost;
        SubtractCost = subtractCost;
        MultiplyCost = multiplyCost;
        HasEnough = hasEnough;
    }

    internal Type ManagerType { get; }
    internal Type PlayerLoadoutType { get; }
    internal Type EquipmentType { get; }
    internal Type AlchemyRecipeType { get; }
    internal Type AlchemySnapshotListType { get; }
    internal Func<object?> Manager { get; }
    internal Func<object, object?> PlayerLoadouts { get; }
    internal Func<object, object?> AlchemySnapshots { get; }
    internal Func<object, object?> EquipmentSnapshots { get; }
    internal Func<object, object?> ActiveAlchemy { get; }
    internal Func<object, object?> ActiveEquipment { get; }
    internal Func<object, bool> CanSwap { get; }
    internal Action<object, object> SetLoadout { get; }
    internal Action<object> SaveActive { get; }
    internal Func<object, IList?> PlayerValues { get; }
    internal Func<object, Guid> PlayerId { get; }
    internal Func<object, string> PlayerName { get; }
    internal Func<object, bool> PlayerSelected { get; }
    internal Func<object, bool> EquipmentEnabled { get; }
    internal Func<object, bool> AlchemyEnabled { get; }
    internal Func<object, object?> PlayerLabel { get; }
    internal Action<object, bool> SetEquipmentEnabled { get; }
    internal Action<object, bool> SetAlchemyEnabled { get; }
    internal Func<object, string> LabelName { get; }
    internal Func<object, int> LabelIcon { get; }
    internal Func<object, int> LabelColor { get; }
    internal Action<object, string> SetLabelName { get; }
    internal Action<object, int> SetLabelIcon { get; }
    internal Action<object, int> SetLabelColor { get; }
    internal Func<IList?> CustomIcons { get; }
    internal Func<IList?> CustomColors { get; }
    internal Func<object, Guid> Identity { get; }
    internal Func<object, int> AlchemyMaximum { get; }
    internal Func<object, int> EquipmentMaximum { get; }
    internal Func<object, IList?> AlchemySnapshotValues { get; }
    internal Func<object, IList?> EquipmentSnapshotValues { get; }
    internal Func<object, bool> AlchemySnapshotEmpty { get; }
    internal Func<object, bool> EquipmentSnapshotEmpty { get; }
    internal Action<object> ClearAlchemySnapshot { get; }
    internal Action<object> ClearEquipmentSnapshot { get; }
    internal Func<object, object?> AlchemySnapshotRecord { get; }
    internal Func<object, object?> EquipmentSnapshotRecord { get; }
    internal Action<object, object> SaveAlchemySnapshot { get; }
    internal Action<object, object> SaveEquipmentSnapshot { get; }
    internal Func<object, object?> CreateAlchemyRecord { get; }
    internal Action<object, object> SetAlchemyRecord { get; }
    internal Func<object, object?> CreateEquipmentRecord { get; }
    internal Action<object, object> SetEquipmentRecord { get; }
    internal Func<object, IList?> AlchemyRecordEntries { get; }
    internal Func<object, IList?> EquipmentRecordEntries { get; }
    internal Func<object> CreateCost { get; }
    internal Func<object, object, object> AddCost { get; }
    internal Func<object, object, object> SubtractCost { get; }
    internal Func<object, BigDouble, object> MultiplyCost { get; }
    internal Func<object, bool> HasEnough { get; }

    internal static bool TryCreate(out LoadoutNativeBindings? bindings, out string reason,
        Func<string, Type?>? resolveType = null, Func<string, bool>? includeContract = null) =>
        NativeBindingSet.TryBind("loadout", ContractIds, resolveType, includeContract, set =>
        {
            var manager = set.Type("loadout.manager.type-action", "LoadoutManager");
            var playerList = set.Type("loadout.player-list.type-action", "PlayerLoadoutListVariable");
            var player = set.Type("loadout.player.type-action", "PlayerLoadout");
            var label = set.Type("loadout.label.type-action", "PlayerLoadout+LoadoutLabel");
            var spell = set.Type("loadout.spell.type-action", "Spell");
            var spellList = set.Type("loadout.spell-list.type-action", "SpellListVariable");
            var equipment = set.Type("loadout.equipment.type-action", "EquipmentSO");
            var alchemyRecipe = set.Type("loadout.alchemy-recipe.type-action", "AlchemyRecipeSO");
            var alchemySnapshotList = set.Type(
                "loadout.alchemy-snapshot-list.type-action", "AlchemySnapshotListVariable");
            var equipmentSnapshotList = set.Type(
                "loadout.equipment-snapshot-list.type-action", "EquipmentSnapshotListVariable");
            var alchemySnapshot = set.Type("loadout.alchemy-snapshot.type-action", "AlchemySnapshot");
            var equipmentSnapshot = set.Type("loadout.equipment-snapshot.type-action", "EquipmentSnapshot");
            var alchemyList = set.Type("loadout.alchemy-list.type-action", "AlchemyInstanceListVariable");
            var equipmentList = set.Type("loadout.equipment-list.type-action", "EquipmentListVariable");
            var cost = set.Type("loadout.cost.type-action", "ResourceCostList");
            var globals = set.Type("loadout.global-variables.type-action", "GlobalVariables");
            var identity = set.Type("loadout.identity.type-action", "IdScriptableObject");
            var stacked = set.Type("loadout.stacked-record.type-action", "Stacked.StackedIdRecord`1");
            var alchemyRecord = stacked.MakeGenericType(alchemyRecipe);
            var equipmentRecord = stacked.MakeGenericType(equipment);
            var alchemyEntries = typeof(List<>).MakeGenericType(
                typeof(ValueTuple<,>).MakeGenericType(alchemyRecipe, typeof(int)));
            var equipmentEntries = typeof(List<>).MakeGenericType(
                typeof(ValueTuple<,>).MakeGenericType(equipment, typeof(int)));

            return new LoadoutNativeBindings(
                manager, player, equipment, alchemyRecipe, alchemySnapshotList,
                set.StaticField<Func<object?>>("loadout.manager-instance-action", manager, "instance", manager),
                set.Field<Func<object, object?>>(
                    "loadout.manager-player-list-action", manager, "playerLoadouts", playerList),
                set.Field<Func<object, object?>>(
                    "loadout.manager-alchemy-snapshots-action", manager, "alchemyLoadouts", alchemySnapshotList),
                set.Field<Func<object, object?>>(
                    "loadout.manager-equipment-snapshots-action", manager, "equipmentLoadouts", equipmentSnapshotList),
                set.Field<Func<object, object?>>(
                    "loadout.manager-active-alchemy-action", manager, "activeAlchemy", alchemyList),
                set.Field<Func<object, object?>>(
                    "loadout.manager-active-equipment-action", manager, "activeEquipment", equipmentList),
                LoadoutNativeShapes.SpellsAllowSwap(
                    set.Field<Func<object, object?>>(
                        "loadout.manager-active-spells-action", manager, "activeSpells", spellList),
                    set.Field<Func<object, IList?>>(
                        "loadout.list-values-action", spellList, "value", typeof(List<>).MakeGenericType(spell)),
                    set.Call<Func<object, bool>>("loadout.spell-casting-action", spell, "IsCasting", typeof(bool)),
                    set.Call<Func<object, bool>>(
                        "loadout.spell-readying-cast-action", spell, "IsReadyingCast", typeof(bool))),
                set.Call<Action<object, object>>(
                    "loadout.manager-set-loadout-action", manager, "SetLoadout", typeof(void), player),
                set.Call<Action<object>>(
                    "loadout.manager-save-active-action", manager, "SaveActiveLoadout", typeof(void)),
                set.Field<Func<object, IList?>>(
                    "loadout.list-values-action", playerList, "value", typeof(List<>).MakeGenericType(player)),
                set.StableUuid("loadout.player-guid-action", player),
                set.Call<Func<object, string>>("loadout.player-name-action", player, "GetName", typeof(string)),
                set.Call<Func<object, bool>>("loadout.player-selected-action", player, "IsSelected", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "loadout.player-equipment-enabled-action", player, "HasEquipment", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "loadout.player-alchemy-enabled-action", player, "HasAlchemy", typeof(bool)),
                set.Call<Func<object, object?>>("loadout.player-label-action", player, "GetLabel", label),
                set.Call<Action<object, bool>>(
                    "loadout.player-set-equipment-action", player, "SetSaveEquipment", typeof(void), typeof(bool)),
                set.Call<Action<object, bool>>(
                    "loadout.player-set-alchemy-action", player, "SetSaveAlchemy", typeof(void), typeof(bool)),
                set.Call<Func<object, string>>("loadout.label-name-action", label, "GetName", typeof(string)),
                set.Call<Func<object, int>>("loadout.label-icon-action", label, "GetIconIndex", typeof(int)),
                set.Call<Func<object, int>>("loadout.label-color-action", label, "GetColorIndex", typeof(int)),
                set.Call<Action<object, string>>(
                    "loadout.label-set-name-action", label, "SetName", typeof(void), typeof(string)),
                set.Call<Action<object, int>>(
                    "loadout.label-set-icon-action", label, "SetIconIndex", typeof(void), typeof(int)),
                set.Call<Action<object, int>>(
                    "loadout.label-set-color-action", label, "SetColorIndex", typeof(void), typeof(int)),
                set.StaticCall<Func<IList?>>("loadout.global-custom-icons-action", globals,
                    "GetCustomSprites", typeof(List<UnityEngine.Sprite>)),
                set.StaticCall<Func<IList?>>("loadout.global-custom-colors-action", globals,
                    "GetCustomColors", typeof(List<UnityEngine.Color>)),
                set.StableUuid("loadout.identity-guid-action", identity),
                set.Call<Func<object, int>>("loadout.alchemy-list-maximum-action", alchemyList, "GetMax", typeof(int)),
                set.Call<Func<object, int>>(
                    "loadout.equipment-list-maximum-action", equipmentList, "GetMax", typeof(int)),
                set.Field<Func<object, IList?>>("loadout.list-values-action", alchemySnapshotList, "value",
                    typeof(List<>).MakeGenericType(alchemySnapshot)),
                set.Field<Func<object, IList?>>("loadout.list-values-action", equipmentSnapshotList, "value",
                    typeof(List<>).MakeGenericType(equipmentSnapshot)),
                set.Call<Func<object, bool>>(
                    "loadout.alchemy-snapshot-empty-action", alchemySnapshot, "IsEmpty", typeof(bool)),
                set.Call<Func<object, bool>>(
                    "loadout.equipment-snapshot-empty-action", equipmentSnapshot, "IsEmpty", typeof(bool)),
                set.Call<Action<object>>(
                    "loadout.alchemy-snapshot-clear-action", alchemySnapshot, "Clear", typeof(void)),
                set.Call<Action<object>>(
                    "loadout.equipment-snapshot-clear-action", equipmentSnapshot, "Clear", typeof(void)),
                set.Call<Func<object, object?>>(
                    "loadout.alchemy-snapshot-record-action", alchemySnapshot, "GetRecord", alchemyRecord),
                set.Call<Func<object, object?>>(
                    "loadout.equipment-snapshot-record-action", equipmentSnapshot, "GetRecord", equipmentRecord),
                set.Call<Action<object, object>>("loadout.alchemy-snapshot-save-action",
                    alchemySnapshot, "SaveSnapshot", typeof(void), alchemyRecord),
                set.Call<Action<object, object>>("loadout.equipment-snapshot-save-action",
                    equipmentSnapshot, "SaveSnapshot", typeof(void), equipmentRecord),
                set.Call<Func<object, object?>>(
                    "loadout.alchemy-active-record-action", alchemyList, "CreateStackedRecord", alchemyRecord),
                set.Call<Action<object, object>>("loadout.alchemy-active-set-action",
                    alchemyList, "FromStackedRecord", typeof(void), alchemyRecord),
                set.Call<Func<object, object?>>(
                    "loadout.equipment-active-record-action", equipmentList, "GetStackedRecord", equipmentRecord),
                set.Call<Action<object, object>>("loadout.equipment-active-set-action",
                    equipmentList, "SetStack", typeof(void), equipmentRecord),
                set.Call<Func<object, IList?>>(
                    "loadout.alchemy-record-entries-action", alchemyRecord, "GetEntries", alchemyEntries),
                set.Call<Func<object, IList?>>(
                    "loadout.equipment-record-entries-action", equipmentRecord, "GetEntries", equipmentEntries),
                set.New<Func<object>>("loadout.cost-construct-action", cost),
                set.Call<Func<object, object, object>>("loadout.cost-add-action", cost, "Add", cost, cost),
                set.Call<Func<object, object, object>>("loadout.cost-subtract-action", cost, "Subtract", cost, cost),
                set.Call<Func<object, BigDouble, object>>(
                    "loadout.cost-multiply-action", cost, "Multiply", cost, typeof(BigDouble)),
                set.Call<Func<object, bool>>("loadout.cost-enough-action", cost, "HasEnough", typeof(bool)));
        }, out bindings, out reason);
}

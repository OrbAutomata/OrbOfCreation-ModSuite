using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace OrbModding.Common.Runtime.World;

/// <summary>Creates equivalent lifecycle binding sets for loadout collection and mutation.</summary>
internal sealed class LoadoutNativeBindings
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    internal static readonly string[] ContractIds =
    {
        "loadout.manager.type-action",
        "loadout.player-list.type-action",
        "loadout.player.type-action",
        "loadout.label.type-action",
        "loadout.spell.type-action",
        "loadout.spell-recipe.type-action",
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
        "loadout.manager-can-swap-action",
        "loadout.manager-set-loadout-action",
        "loadout.manager-save-active-action",
        "loadout.list-values-action",
        "loadout.player-guid-action",
        "loadout.player-name-action",
        "loadout.player-selected-action",
        "loadout.player-equipment-enabled-action",
        "loadout.player-alchemy-enabled-action",
        "loadout.player-spells-action",
        "loadout.player-equipment-record-action",
        "loadout.player-alchemy-record-action",
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
        "loadout.spell-guid-action",
        "loadout.spell-reference-action",
        "loadout.spell-empty-action",
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
        Type spellType,
        Type equipmentType,
        Type alchemyRecipeType,
        Type alchemySnapshotListType,
        Type equipmentSnapshotListType,
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
        Func<object, IList?> playerSpells,
        Func<object, object?> playerEquipmentRecord,
        Func<object, object?> playerAlchemyRecord,
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
        Func<object, Guid> spellId,
        Func<object, object?> spellReference,
        Func<object, bool> spellEmpty,
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
        SpellType = spellType;
        EquipmentType = equipmentType;
        AlchemyRecipeType = alchemyRecipeType;
        AlchemySnapshotListType = alchemySnapshotListType;
        EquipmentSnapshotListType = equipmentSnapshotListType;
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
        PlayerSpells = playerSpells;
        PlayerEquipmentRecord = playerEquipmentRecord;
        PlayerAlchemyRecord = playerAlchemyRecord;
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
        SpellId = spellId;
        SpellReference = spellReference;
        SpellEmpty = spellEmpty;
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
    internal Type SpellType { get; }
    internal Type EquipmentType { get; }
    internal Type AlchemyRecipeType { get; }
    internal Type AlchemySnapshotListType { get; }
    internal Type EquipmentSnapshotListType { get; }
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
    internal Func<object, IList?> PlayerSpells { get; }
    internal Func<object, object?> PlayerEquipmentRecord { get; }
    internal Func<object, object?> PlayerAlchemyRecord { get; }
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
    internal Func<object, Guid> SpellId { get; }
    internal Func<object, object?> SpellReference { get; }
    internal Func<object, bool> SpellEmpty { get; }
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

    internal static bool TryCreate(
        out LoadoutNativeBindings? bindings,
        out string reason,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        bindings = null;
        resolveType ??= ReflectionUtil.FindLoadedType;
        includeContract ??= static _ => true;
        try
        {
            for (var index = 0; index < ContractIds.Length; index++)
                if (!includeContract(ContractIds[index]))
                    throw new InvalidOperationException(ContractIds[index] + " was unavailable");

            Type T(string name) => resolveType(name) ??
                throw new InvalidOperationException(name + " was unavailable");
            var manager = T("LoadoutManager");
            var playerList = T("PlayerLoadoutListVariable");
            var player = T("PlayerLoadout");
            var label = T("PlayerLoadout+LoadoutLabel");
            var spell = T("Spell");
            var spellRecipe = T("SpellRecipeSO");
            var equipment = T("EquipmentSO");
            var alchemyRecipe = T("AlchemyRecipeSO");
            var alchemySnapshotList = T("AlchemySnapshotListVariable");
            var equipmentSnapshotList = T("EquipmentSnapshotListVariable");
            var alchemySnapshot = T("AlchemySnapshot");
            var equipmentSnapshot = T("EquipmentSnapshot");
            var alchemyList = T("AlchemyInstanceListVariable");
            var equipmentList = T("EquipmentListVariable");
            var cost = T("ResourceCostList");
            var globals = T("GlobalVariables");
            var identity = T("IdScriptableObject");
            var stackedOpen = T("Stacked.StackedIdRecord`1");
            var alchemyRecord = stackedOpen.MakeGenericType(alchemyRecipe);
            var equipmentRecord = stackedOpen.MakeGenericType(equipment);

            var playerCollection = typeof(List<>).MakeGenericType(player);
            var spellCollection = typeof(List<>).MakeGenericType(spell);
            var alchemySnapshotCollection = typeof(List<>).MakeGenericType(alchemySnapshot);
            var equipmentSnapshotCollection = typeof(List<>).MakeGenericType(equipmentSnapshot);
            var alchemyEntries = typeof(List<>).MakeGenericType(
                typeof(ValueTuple<,>).MakeGenericType(alchemyRecipe, typeof(int)));
            var equipmentEntries = typeof(List<>).MakeGenericType(
                typeof(ValueTuple<,>).MakeGenericType(equipment, typeof(int)));

            var managerInstance = Field(manager, "instance", manager, true);
            var managerPlayer = Field(manager, "playerLoadouts", playerList, false);
            var managerAlchemySnapshots = Field(manager, "alchemyLoadouts", alchemySnapshotList, false);
            var managerEquipmentSnapshots = Field(manager, "equipmentLoadouts", equipmentSnapshotList, false);
            var managerAlchemy = Field(manager, "activeAlchemy", alchemyList, false);
            var managerEquipment = Field(manager, "activeEquipment", equipmentList, false);
            var canSwap = Method(manager, "CanSwapLoadouts", typeof(bool));
            var setLoadout = Method(manager, "SetLoadout", typeof(void), player);
            var saveActive = Method(manager, "SaveActiveLoadout", typeof(void));
            var playerAll = Field(playerList, "value", playerCollection, false);
            var playerGuid = Method(player, "GetGuid", typeof(Guid));
            var playerName = Method(player, "GetName", typeof(string));
            var selected = Method(player, "IsSelected", typeof(bool));
            var equipmentEnabled = Method(player, "HasEquipment", typeof(bool));
            var alchemyEnabled = Method(player, "HasAlchemy", typeof(bool));
            var spells = Method(player, "GetSpells", spellCollection);
            var equipmentField = Field(player, "equipment", equipmentRecord, false);
            var alchemyField = Field(player, "alchemy", alchemyRecord, false);
            var getLabel = Method(player, "GetLabel", label);
            var setEquipment = Method(player, "SetSaveEquipment", typeof(void), typeof(bool));
            var setAlchemy = Method(player, "SetSaveAlchemy", typeof(void), typeof(bool));
            var labelName = Method(label, "GetName", typeof(string));
            var labelIcon = Method(label, "GetIconIndex", typeof(int));
            var labelColor = Method(label, "GetColorIndex", typeof(int));
            var setName = Method(label, "SetName", typeof(void), typeof(string));
            var setIcon = Method(label, "SetIconIndex", typeof(void), typeof(int));
            var setColor = Method(label, "SetColorIndex", typeof(void), typeof(int));
            var customIcons = Method(globals, "GetCustomSprites", null, true);
            var customColors = Method(globals, "GetCustomColors", null, true);
            var getIdentity = Method(identity, "GetGuid", typeof(Guid));
            var spellId = Method(spell, "GetId", typeof(Guid));
            var spellReference = Method(spell, "get_reference", spellRecipe);
            var spellEmpty = Method(spell, "IsEmpty", typeof(bool));
            var alchemyMaximum = Method(alchemyList, "GetMax", typeof(int));
            var equipmentMaximum = Method(equipmentList, "GetMax", typeof(int));
            var alchemySnapshots = Field(alchemySnapshotList, "value", alchemySnapshotCollection, false);
            var equipmentSnapshots = Field(equipmentSnapshotList, "value", equipmentSnapshotCollection, false);
            var alchemyEmpty = Method(alchemySnapshot, "IsEmpty", typeof(bool));
            var equipmentEmpty = Method(equipmentSnapshot, "IsEmpty", typeof(bool));
            var clearAlchemy = Method(alchemySnapshot, "Clear", typeof(void));
            var clearEquipment = Method(equipmentSnapshot, "Clear", typeof(void));
            var alchemySnapshotRecord = Method(alchemySnapshot, "GetRecord", alchemyRecord);
            var equipmentSnapshotRecord = Method(equipmentSnapshot, "GetRecord", equipmentRecord);
            var saveAlchemySnapshot = Method(alchemySnapshot, "SaveSnapshot", typeof(void), alchemyRecord);
            var saveEquipmentSnapshot = Method(equipmentSnapshot, "SaveSnapshot", typeof(void), equipmentRecord);
            var createAlchemy = Method(alchemyList, "CreateStackedRecord", alchemyRecord);
            var setAlchemyRecord = Method(alchemyList, "FromStackedRecord", typeof(void), alchemyRecord);
            var createEquipment = Method(equipmentList, "GetStackedRecord", equipmentRecord);
            var setEquipmentRecord = Method(equipmentList, "SetStack", typeof(void), equipmentRecord);
            var alchemyGetEntries = Method(alchemyRecord, "GetEntries", alchemyEntries);
            var equipmentGetEntries = Method(equipmentRecord, "GetEntries", equipmentEntries);
            var costConstructor = cost.GetConstructor(Type.EmptyTypes) ??
                throw new InvalidOperationException("ResourceCostList constructor was unavailable");
            var addCost = Method(cost, "Add", cost, cost);
            var subtractCost = Method(cost, "Subtract", cost, cost);
            var multiplyCost = Method(cost, "Multiply", cost, typeof(BigDouble));
            var enough = Method(cost, "HasEnough", typeof(bool));

            bindings = new LoadoutNativeBindings(
                manager, player, spell, equipment, alchemyRecipe,
                alchemySnapshotList, equipmentSnapshotList,
                StaticObject(managerInstance), ObjectField(managerPlayer),
                ObjectField(managerAlchemySnapshots), ObjectField(managerEquipmentSnapshots),
                ObjectField(managerAlchemy), ObjectField(managerEquipment),
                Func<bool>(canSwap), ActionObject(setLoadout), ActionVoid(saveActive),
                ListField(playerAll), Func<Guid>(playerGuid),
                Func<string>(playerName), Func<bool>(selected), Func<bool>(equipmentEnabled),
                Func<bool>(alchemyEnabled), ListFunc(spells), ObjectField(equipmentField),
                ObjectField(alchemyField), ObjectFunc(getLabel), ActionBool(setEquipment),
                ActionBool(setAlchemy), Func<string>(labelName), Func<int>(labelIcon),
                Func<int>(labelColor), ActionString(setName), ActionInt(setIcon),
                ActionInt(setColor), StaticList(customIcons), StaticList(customColors),
                Func<Guid>(getIdentity), Func<Guid>(spellId), ObjectFunc(spellReference),
                Func<bool>(spellEmpty),
                Func<int>(alchemyMaximum), Func<int>(equipmentMaximum),
                ListField(alchemySnapshots), ListField(equipmentSnapshots), Func<bool>(alchemyEmpty),
                Func<bool>(equipmentEmpty), ActionVoid(clearAlchemy), ActionVoid(clearEquipment),
                ObjectFunc(alchemySnapshotRecord), ObjectFunc(equipmentSnapshotRecord),
                ActionObject(saveAlchemySnapshot), ActionObject(saveEquipmentSnapshot),
                ObjectFunc(createAlchemy), ActionObject(setAlchemyRecord), ObjectFunc(createEquipment),
                ActionObject(setEquipmentRecord), ListFunc(alchemyGetEntries),
                ListFunc(equipmentGetEntries), Expression.Lambda<Func<object>>(
                    Expression.Convert(Expression.New(costConstructor), typeof(object))).Compile(),
                ObjectObjectFunc(addCost), ObjectObjectFunc(subtractCost),
                ObjectBigDoubleFunc(multiplyCost), Func<bool>(enough));
            reason = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is InvalidOperationException or
            ArgumentException or AmbiguousMatchException or NotSupportedException)
        {
            reason = "Loadout contracts are unavailable: " + exception.GetBaseException().Message;
            return false;
        }
    }

    internal static bool TryReadEntry(object entry, Type expectedItemType,
        out object? item, out int quantity)
    {
        item = null;
        quantity = 0;
        if (entry is not ITuple tuple || tuple.Length != 2 ||
            tuple[0] is not { } candidate || candidate.GetType() != expectedItemType ||
            tuple[1] is not int count)
        {
            return false;
        }
        item = candidate;
        quantity = count;
        return true;
    }

    private static FieldInfo Field(Type owner, string name, Type valueType, bool isStatic)
    {
        var field = owner.GetField(name, isStatic ? Static : Instance);
        if (field is null || field.IsStatic != isStatic || field.FieldType != valueType)
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited field");
        return field;
    }

    private static MethodInfo Method(Type owner, string name, Type? result,
        params Type[] parameters) => Method(owner, name, result, false, parameters);

    private static MethodInfo Method(Type owner, string name, Type? result,
        bool isStatic, params Type[] parameters)
    {
        var method = owner.GetMethod(name, isStatic ? Static : Instance, null, parameters, null);
        if (method is null || method.IsStatic != isStatic ||
            (result is not null && method.ReturnType != result) ||
            (result is null && !typeof(IList).IsAssignableFrom(method.ReturnType)))
            throw new InvalidOperationException(owner.Name + "." + name + " did not match the audited signature");
        return method;
    }

    private static Func<object?> StaticObject(FieldInfo field) => () => field.GetValue(null);
    private static Func<object, object?> ObjectField(FieldInfo field) => target => field.GetValue(target);
    private static Func<object, IList?> ListField(FieldInfo field) =>
        target => field.GetValue(target) as IList;

    private static Func<object, TResult> Func<TResult>(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, TResult>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Func<object, object?> ObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, object?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(object)), target).Compile();
    }

    private static Func<object, IList?> ListFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Func<object, IList?>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method),
                typeof(IList)), target).Compile();
    }

    private static Func<IList?> StaticList(MethodInfo method) =>
        Expression.Lambda<Func<IList?>>(
            Expression.Convert(Expression.Call(method), typeof(IList))).Compile();

    private static Action<object> ActionVoid(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        return Expression.Lambda<Action<object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method), target).Compile();
    }

    private static Action<object, object> ActionObject(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Action<object, object>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), target, value).Compile();
    }

    private static Action<object, bool> ActionBool(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(bool), "value");
        return Expression.Lambda<Action<object, bool>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value), target, value).Compile();
    }

    private static Action<object, int> ActionInt(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(int), "value");
        return Expression.Lambda<Action<object, int>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value), target, value).Compile();
    }

    private static Action<object, string> ActionString(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(string), "value");
        return Expression.Lambda<Action<object, string>>(
            Expression.Call(Expression.Convert(target, method.DeclaringType!), method, value), target, value).Compile();
    }

    private static Func<object, object, object> ObjectObjectFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(object), "value");
        return Expression.Lambda<Func<object, object, object>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                Expression.Convert(value, method.GetParameters()[0].ParameterType)), typeof(object)),
            target, value).Compile();
    }

    private static Func<object, BigDouble, object> ObjectBigDoubleFunc(MethodInfo method)
    {
        var target = Expression.Parameter(typeof(object), "target");
        var value = Expression.Parameter(typeof(BigDouble), "value");
        return Expression.Lambda<Func<object, BigDouble, object>>(
            Expression.Convert(Expression.Call(Expression.Convert(target, method.DeclaringType!), method,
                value), typeof(object)), target, value).Compile();
    }
}

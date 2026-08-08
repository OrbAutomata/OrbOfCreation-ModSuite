using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldSpellWorkbench
{
    /// <summary>
    /// The floor the Casting screen's Output and Reserve dials admit, published beside their
    /// maximum so the pair is never a ceiling on its own. This is a suite constant matching the
    /// observed control rather than a value read from it: both dials are authored
    /// <c>UIValueSelectButton</c>s whose clamp is serialized on the prefab instead of passed in IL,
    /// so no audit can quote their own minimum without an asset dump. The contract gate pins the
    /// shape the floor lives in — <c>SetClamp(min, max)</c> stores <c>minValue</c> and
    /// <c>Decrement()</c> is <c>Math.Max(value - change, minValue)</c> — and the evidence for the
    /// value is circumstantial: every call site passing a literal passes 1, and the only computed
    /// one (<c>UIBrewingStation</c>) reads <c>CraftingStructure.GetMinSelectedLevel()</c>. Nothing
    /// here reflects on that control, so the dependency is declared as the mirrored contracts
    /// <c>ui.value-select-button.clamp</c> and <c>ui.value-select-button.minimum</c>.
    /// </summary>
    internal const int MinimumDialLevel = 1;

    internal WorldSpellWorkbench(
        int equippedCount,
        int maximumEquipped,
        bool hasEmptySlot,
        int outputLevel = 0,
        int maximumOutputLevel = 0,
        int reserveLevel = 0,
        int maximumReserveLevel = 0)
    {
        EquippedCount = equippedCount;
        MaximumEquipped = maximumEquipped;
        HasEmptySlot = hasEmptySlot;
        OutputLevel = outputLevel;
        MaximumOutputLevel = maximumOutputLevel;
        ReserveLevel = reserveLevel;
        MaximumReserveLevel = maximumReserveLevel;
    }

    internal int EquippedCount { get; }
    internal int MaximumEquipped { get; }
    internal bool HasEmptySlot { get; }
    internal int OutputLevel { get; }
    internal int MaximumOutputLevel { get; }
    internal int ReserveLevel { get; }
    internal int MaximumReserveLevel { get; }
}

internal sealed class WorldSpellWorkbenchBuffer
{
    internal int EquippedCount { get; private set; }
    internal int MaximumEquipped { get; private set; }
    internal bool HasEmptySlot { get; private set; }
    internal int OutputLevel { get; private set; }
    internal int MaximumOutputLevel { get; private set; }
    internal int ReserveLevel { get; private set; }
    internal int MaximumReserveLevel { get; private set; }

    internal void Reset()
    {
        EquippedCount = 0;
        MaximumEquipped = 0;
        HasEmptySlot = false;
        OutputLevel = 0;
        MaximumOutputLevel = 0;
        ReserveLevel = 0;
        MaximumReserveLevel = 0;
    }

    internal void SetCapacity(int equippedCount, int maximumEquipped, bool hasEmptySlot)
    {
        EquippedCount = equippedCount;
        MaximumEquipped = maximumEquipped;
        HasEmptySlot = hasEmptySlot;
    }

    internal void SetCastingDials(
        int outputLevel,
        int maximumOutputLevel,
        int reserveLevel,
        int maximumReserveLevel)
    {
        OutputLevel = outputLevel;
        MaximumOutputLevel = maximumOutputLevel;
        ReserveLevel = reserveLevel;
        MaximumReserveLevel = maximumReserveLevel;
    }

    internal WorldSpellWorkbench Build() => new(
        EquippedCount,
        MaximumEquipped,
        HasEmptySlot,
        OutputLevel,
        MaximumOutputLevel,
        ReserveLevel,
        MaximumReserveLevel);
}

/// <summary>One read-only, main-thread capture of spell loadout room and global casting dials.</summary>
internal sealed class WorldSpellWorkbenchReader : IWorldCategoryReader
{
    private const BindingFlags Static =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _active;
    private readonly Func<object, int>? _used;
    private readonly Func<object, int>? _maximum;
    private readonly Func<object, bool>? _hasEmpty;
    private readonly Func<object?>? _player;
    private readonly Func<object?>? _outputLevel;
    private readonly Func<object, object?>? _maximumOutputLevel;
    private readonly Func<object?>? _reserveLevel;
    private readonly Func<object, object?>? _maximumReserveLevel;
    private readonly Func<object, int>? _asInt;
    private readonly string _unavailable;

    internal WorldSpellWorkbenchReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        var managerType = resolveType("SpellManager");
        var spellListType = resolveType("SpellListVariable");
        _manager = BindStaticReference(managerType, "instance", managerType);
        _active = NativeAccessorBinder.Reference(managerType, "activeSpells", spellListType);
        _used = NativeAccessorBinder.Call<int>(spellListType, "GetUsedSpots");
        _maximum = NativeAccessorBinder.Call<int>(spellListType, "GetMax");
        _hasEmpty = NativeAccessorBinder.Call<bool>(spellListType, "HasEmptySpot");
        var playerType = resolveType("Player");
        var intVariableType = resolveType("IntVariable");
        _player = BindStaticReference(playerType, "_instance", playerType);
        _outputLevel = BindStaticObjectCall(playerType, "GetSpellOutputLevel", intVariableType);
        _maximumOutputLevel = NativeAccessorBinder.Reference(
            playerType, "maxSpellOutputLevel", intVariableType);
        _reserveLevel = BindStaticObjectCall(playerType, "GetReserveLevel", intVariableType);
        _maximumReserveLevel = NativeAccessorBinder.Reference(
            playerType, "maxReserveLevel", intVariableType);
        _asInt = NativeAccessorBinder.Call<int>(intVariableType, "AsInt");
        _unavailable = managerType is null || spellListType is null ||
            _manager is null || _active is null || _used is null ||
            _maximum is null || _hasEmpty is null ||
            _player is null || _outputLevel is null || _maximumOutputLevel is null ||
            _reserveLevel is null || _maximumReserveLevel is null || _asInt is null
            ? "the complete spell loadout and casting-dial binding set was unavailable"
            : string.Empty;
    }

    public string Category => "spell workbench";

    public bool IsAvailable => _unavailable.Length == 0;

    public WorldCategoryReport Collect(HashSet<Guid> claimed, GameWorldCycleFrame frame)
    {
        if (frame is null) throw new ArgumentNullException(nameof(frame));
        var buffer = frame.SpellWorkbench;
        buffer.Reset();
        if (!IsAvailable) return WorldCategoryReport.Missing(Category, _unavailable);

        try
        {
            var manager = _manager!();
            if (manager is null)
                return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);
            var active = _active!(manager);
            if (active is null)
                return WorldCategoryReport.Missing(Category, "SpellManager.activeSpells was null");
            buffer.SetCapacity(_used!(active), _maximum!(active), _hasEmpty!(active));
            var player = _player!();
            var output = _outputLevel!();
            var maximumOutput = player is null ? null : _maximumOutputLevel!(player);
            var reserve = _reserveLevel!();
            var maximumReserve = player is null ? null : _maximumReserveLevel!(player);
            if (player is null || output is null || maximumOutput is null ||
                reserve is null || maximumReserve is null)
                return WorldCategoryReport.Missing(
                    Category,
                    "Player casting-dial variables were null");
            buffer.SetCastingDials(
                _asInt!(output),
                _asInt!(maximumOutput),
                _asInt!(reserve),
                _asInt!(maximumReserve));
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);
        }
        catch (Exception ex)
        {
            return WorldCategoryReport.Missing(
                Category,
                "reading the native spell loadout and casting dials threw: " +
                ex.GetBaseException().Message);
        }
    }

    private static Func<object?>? BindStaticReference(Type? owner, string name, Type? exactType)
    {
        if (owner is null || exactType is null) return null;
        var field = owner.GetField(name, Static);
        if (field is null || field.FieldType != exactType) return null;
        try
        {
            var read = Expression.Convert(Expression.Field(null, field), typeof(object));
            return Expression.Lambda<Func<object?>>(read).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Func<object?>? BindStaticObjectCall(Type? owner, string name, Type? resultType)
    {
        if (owner is null || resultType is null) return null;
        var method = owner.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || method.ReturnType != resultType) return null;
        try
        {
            var call = Expression.Convert(Expression.Call(method), typeof(object));
            return Expression.Lambda<Func<object?>>(call).Compile();
        }
        catch (Exception)
        {
            return null;
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

internal readonly struct WorldSpellWorkbench
{
    /// <summary>
    /// The floor every level dial admits — Output Lv, Reserve Lv, Druidry Lv and Alchemy Lv —
    /// published beside their maximum so the pair is never a ceiling on its own. This is a suite
    /// constant matching the observed control rather than a value read from it: each dial is a
    /// <c>UIValueSelectButton</c> whose clamp is serialized on the prefab instead of passed in IL,
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
        int maximumReserveLevel = 0,
        PublicationTable<Guid>? usageBudgetResourceIds = null,
        int druidryLevel = 0,
        int maximumDruidryLevel = 0,
        int alchemyLevel = 0,
        int maximumAlchemyLevel = 0)
    {
        EquippedCount = equippedCount;
        MaximumEquipped = maximumEquipped;
        HasEmptySlot = hasEmptySlot;
        OutputLevel = outputLevel;
        MaximumOutputLevel = maximumOutputLevel;
        ReserveLevel = reserveLevel;
        MaximumReserveLevel = maximumReserveLevel;
        UsageBudgetResourceIds = usageBudgetResourceIds ?? PublicationTable<Guid>.Empty;
        DruidryLevel = druidryLevel;
        MaximumDruidryLevel = maximumDruidryLevel;
        AlchemyLevel = alchemyLevel;
        MaximumAlchemyLevel = maximumAlchemyLevel;
    }

    internal int EquippedCount { get; }
    internal int MaximumEquipped { get; }
    internal bool HasEmptySlot { get; }
    internal int OutputLevel { get; }
    internal int MaximumOutputLevel { get; }
    internal int ReserveLevel { get; }
    internal int MaximumReserveLevel { get; }
    internal int DruidryLevel { get; }
    internal int MaximumDruidryLevel { get; }
    internal int AlchemyLevel { get; }
    internal int MaximumAlchemyLevel { get; }

    /// <summary>
    /// The resources one more equipped spell is weighed against — <c>SpellManager</c>'s own spell
    /// weight list, in the order it holds them.
    /// </summary>
    /// <remarks>
    /// This is the whole of the capture, and it is a membership list rather than a set of numbers on
    /// purpose. The gate that refuses an add is
    /// <c>GetUsageCostOfSpell(candidate).HasEnough()</c>, which asks each of these resources whether
    /// its headroom covers the spell's weight — and the world already publishes every resource's
    /// headroom, and already mirrors that predicate in <c>WorldResourceMath.HasAmount</c>. The one
    /// fact nothing published was <em>which</em> resources the question is asked of, so a reader
    /// with the whole budget in front of it could not tell which rows were the budget. The spot
    /// counts beside this answer a different question: the loadout can have an empty slot and still
    /// refuse every spell that would fit in it.
    /// </remarks>
    internal PublicationTable<Guid> UsageBudgetResourceIds { get; }
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
    internal int DruidryLevel { get; private set; }
    internal int MaximumDruidryLevel { get; private set; }
    internal int AlchemyLevel { get; private set; }
    internal int MaximumAlchemyLevel { get; private set; }
    private readonly List<Guid> _usageBudgetResourceIds = new();

    internal void Reset()
    {
        EquippedCount = 0;
        MaximumEquipped = 0;
        HasEmptySlot = false;
        OutputLevel = 0;
        MaximumOutputLevel = 0;
        ReserveLevel = 0;
        MaximumReserveLevel = 0;
        DruidryLevel = 0;
        MaximumDruidryLevel = 0;
        AlchemyLevel = 0;
        MaximumAlchemyLevel = 0;
        _usageBudgetResourceIds.Clear();
    }

    internal void AddUsageBudgetResource(Guid resourceId) =>
        _usageBudgetResourceIds.Add(resourceId);

    internal void SetCapacity(int equippedCount, int maximumEquipped, bool hasEmptySlot)
    {
        EquippedCount = equippedCount;
        MaximumEquipped = maximumEquipped;
        HasEmptySlot = hasEmptySlot;
    }

    internal void SetLevelDials(
        int outputLevel,
        int maximumOutputLevel,
        int reserveLevel,
        int maximumReserveLevel,
        int druidryLevel,
        int maximumDruidryLevel,
        int alchemyLevel,
        int maximumAlchemyLevel)
    {
        OutputLevel = outputLevel;
        MaximumOutputLevel = maximumOutputLevel;
        ReserveLevel = reserveLevel;
        MaximumReserveLevel = maximumReserveLevel;
        DruidryLevel = druidryLevel;
        MaximumDruidryLevel = maximumDruidryLevel;
        AlchemyLevel = alchemyLevel;
        MaximumAlchemyLevel = maximumAlchemyLevel;
    }

    internal WorldSpellWorkbench Build() => new(
        EquippedCount,
        MaximumEquipped,
        HasEmptySlot,
        OutputLevel,
        MaximumOutputLevel,
        ReserveLevel,
        MaximumReserveLevel,
        PublicationTable<Guid>.Create(_usageBudgetResourceIds.ToArray()),
        DruidryLevel,
        MaximumDruidryLevel,
        AlchemyLevel,
        MaximumAlchemyLevel);
}

/// <summary>One read-only, main-thread capture of spell loadout room and the four level dials.</summary>
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
    private readonly Func<object, object?>? _druidryLevel;
    private readonly Func<object, object?>? _maximumDruidryLevel;
    private readonly Func<object, object?>? _alchemyLevel;
    private readonly Func<object, object?>? _maximumAlchemyLevel;
    private readonly Func<object, int>? _asInt;
    private readonly Func<object, object?>? _spellWeights;
    private readonly Func<object, IList?>? _spellWeightValues;
    private readonly Func<object, Guid>? _resourceGuid;
    private readonly string _unavailable;

    internal WorldSpellWorkbenchReader(Func<string, Type?> resolveType)
    {
        if (resolveType is null) throw new ArgumentNullException(nameof(resolveType));
        var managerType = resolveType("SpellManager");
        var spellListType = resolveType("SpellListVariable");
        _manager = NativeAccessorBinder.StaticReference(managerType, "instance", managerType);
        _active = NativeAccessorBinder.Reference(managerType, "activeSpells", spellListType);
        _used = NativeAccessorBinder.Call<int>(spellListType, "GetUsedSpots");
        _maximum = NativeAccessorBinder.Call<int>(spellListType, "GetMax");
        _hasEmpty = NativeAccessorBinder.Call<bool>(spellListType, "HasEmptySpot");
        var playerType = resolveType("Player");
        var intVariableType = resolveType("IntVariable");
        _player = NativeAccessorBinder.StaticReference(playerType, "_instance", playerType);
        _outputLevel = NativeAccessorBinder.StaticCallObject(playerType, "GetSpellOutputLevel", intVariableType);
        _maximumOutputLevel = NativeAccessorBinder.Reference(
            playerType, "maxSpellOutputLevel", intVariableType);
        _reserveLevel = NativeAccessorBinder.StaticCallObject(playerType, "GetReserveLevel", intVariableType);
        _maximumReserveLevel = NativeAccessorBinder.Reference(
            playerType, "maxReserveLevel", intVariableType);
        _druidryLevel = NativeAccessorBinder.Reference(
            playerType, "druidryOutputLevel", intVariableType);
        _maximumDruidryLevel = NativeAccessorBinder.Reference(
            playerType, "maxDruidryOutputLevel", intVariableType);
        _alchemyLevel = NativeAccessorBinder.Reference(
            playerType, "alchemyOutputLevel", intVariableType);
        _maximumAlchemyLevel = NativeAccessorBinder.Reference(
            playerType, "maxAlchemyOutputLevel", intVariableType);
        _asInt = NativeAccessorBinder.Call<int>(intVariableType, "AsInt");
        var resourceListType = resolveType("ResourceListVariable");
        var resourceType = resolveType("ResourceSO");
        _spellWeights = NativeAccessorBinder.Reference(
            managerType, "spellWeightVariables", resourceListType);
        _spellWeightValues = NativeAccessorBinder.CollectionField(resourceListType, "value");
        _resourceGuid = NativeAccessorBinder.StableUuid(resourceType);
        _unavailable = managerType is null || spellListType is null ||
            _manager is null || _active is null || _used is null ||
            _maximum is null || _hasEmpty is null ||
            _player is null || _outputLevel is null || _maximumOutputLevel is null ||
            _reserveLevel is null || _maximumReserveLevel is null ||
            _druidryLevel is null || _maximumDruidryLevel is null ||
            _alchemyLevel is null || _maximumAlchemyLevel is null || _asInt is null ||
            resourceListType is null || resourceType is null ||
            _spellWeights is null || _spellWeightValues is null || _resourceGuid is null
            ? "the complete spell loadout and level-dial binding set was unavailable"
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

        var report = Read(buffer);
        if (report.Outcome == WorldCategoryOutcome.Unavailable) buffer.Reset();
        return report;
    }

    private WorldCategoryReport Read(WorldSpellWorkbenchBuffer buffer)
    {
        try
        {
            var manager = _manager!();
            if (manager is null)
                return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 0, 0, string.Empty);
            var active = _active!(manager);
            if (active is null)
                return WorldCategoryReport.Missing(Category, "SpellManager.activeSpells was null");
            buffer.SetCapacity(_used!(active), _maximum!(active), _hasEmpty!(active));
            var weights = _spellWeights!(manager);
            if (weights is null)
                return WorldCategoryReport.Missing(
                    Category, "SpellManager.spellWeightVariables was null");
            var weightValues = _spellWeightValues!(weights);
            if (weightValues is null)
                return WorldCategoryReport.Missing(
                    Category, "SpellManager.spellWeightVariables held no value list");
            for (var index = 0; index < weightValues.Count; index++)
            {
                var resource = weightValues[index];
                if (resource is null) continue;
                var resourceId = _resourceGuid!(resource);
                if (resourceId != Guid.Empty) buffer.AddUsageBudgetResource(resourceId);
            }
            var player = _player!();
            var output = _outputLevel!();
            var maximumOutput = player is null ? null : _maximumOutputLevel!(player);
            var reserve = _reserveLevel!();
            var maximumReserve = player is null ? null : _maximumReserveLevel!(player);
            var druidry = player is null ? null : _druidryLevel!(player);
            var maximumDruidry = player is null ? null : _maximumDruidryLevel!(player);
            var alchemy = player is null ? null : _alchemyLevel!(player);
            var maximumAlchemy = player is null ? null : _maximumAlchemyLevel!(player);
            if (player is null || output is null || maximumOutput is null ||
                reserve is null || maximumReserve is null ||
                druidry is null || maximumDruidry is null ||
                alchemy is null || maximumAlchemy is null)
                return WorldCategoryReport.Missing(
                    Category,
                    "Player level-dial variables were null");
            buffer.SetLevelDials(
                _asInt!(output),
                _asInt!(maximumOutput),
                _asInt!(reserve),
                _asInt!(maximumReserve),
                _asInt!(druidry),
                _asInt!(maximumDruidry),
                _asInt!(alchemy),
                _asInt!(maximumAlchemy));
            return new WorldCategoryReport(Category, WorldCategoryOutcome.Collected, 1, 0, string.Empty);
        }
        catch (Exception ex)
        {
            return WorldCategoryReport.Missing(
                Category,
                "reading the native spell loadout and level dials threw: " +
                ex.GetBaseException().Message);
        }
    }

}

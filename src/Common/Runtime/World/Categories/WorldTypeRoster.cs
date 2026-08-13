using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// The nine type taxonomies whose assets the world walked but never published a row for.
/// </summary>
/// <remarks>
/// <para>
/// Their records are collected in full — <see cref="WorldTypeModifier"/> binds all fourteen
/// taxonomies and <see cref="WorldTypeModifierContribution"/> carries every modifier sitting on
/// them — and their names live in the live identity catalog like every other asset the game loads.
/// What was missing was a table keyed by the type's own identity, so nothing could ask one of these
/// assets anything: an id for a structure type resolved to no category and answered nothing.
/// </para>
/// <para>
/// A row carries what the five already-published type categories carry: the type's identity, every
/// scalar the class stores, and each <c>ValueModifierRecord</c> it holds a number of its own in. The
/// distributors are deliberately absent. Their magnitude is a total derived off-thread from the
/// contribution rows, and a per-record count beside it would republish
/// <see cref="WorldTypeModifier"/>'s own <c>ActiveCount</c> under a second name — the duplication
/// the type-modifier design named as a deletion on the older type rows, not one to spread further.
/// Two of the nine store no scalar and no value record at all, so their row is the handle alone.
/// </para>
/// </remarks>
internal readonly struct WorldStructureType : IWorldEntity
{
    internal WorldStructureType(
        Guid structureTypeId,
        int baseEffectLevel,
        double baseBuildTime,
        bool overrideRankDefault,
        int overrideRank)
    {
        StructureTypeId = structureTypeId;
        BaseEffectLevel = baseEffectLevel;
        BaseBuildTime = baseBuildTime;
        OverrideRankDefault = overrideRankDefault;
        OverrideRank = overrideRank;
    }

    internal Guid StructureTypeId { get; }

    public Guid EntityId => StructureTypeId;

    /// <summary>What every structure of this family starts at before any bonus reaches it.</summary>
    internal int BaseEffectLevel { get; }

    internal double BaseBuildTime { get; }

    /// <summary>Whether the family sorts by its own rank rather than the rank its members carry.</summary>
    internal bool OverrideRankDefault { get; }

    internal int OverrideRank { get; }
}

internal sealed class WorldStructureTypeBinder : WorldPlainBinder<WorldStructureType>
{
    private Func<object, Guid>? _id;
    private Func<object, int>? _baseEffectLevel;
    private Func<object, double>? _baseBuildTime;
    private Func<object, bool>? _overrideRankDefault;
    private Func<object, int>? _overrideRank;

    internal override string Category => "structure types";

    internal override string TypeName => "StructureTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _baseEffectLevel = bind.Field<int>("baseEffectLevel");
        _baseBuildTime = bind.Field<double>("baseBuildTime");
        _overrideRankDefault = bind.Field<bool>("overrideRankDefault");
        _overrideRank = bind.Field<int>("overrideRank");
        return bind.Failure;
    }

    internal override WorldStructureType Read(object entity) =>
        new(
            _id!(entity),
            _baseEffectLevel!(entity),
            _baseBuildTime!(entity),
            _overrideRankDefault!(entity),
            _overrideRank!(entity));
}

/// <summary>One ritual type as published: its identity and the count of rituals it holds active.</summary>
internal readonly struct WorldRitualType : IWorldEntity
{
    internal WorldRitualType(Guid ritualTypeId, bool initiated, BigDouble activeRituals)
    {
        RitualTypeId = ritualTypeId;
        Initiated = initiated;
        ActiveRituals = activeRituals;
    }

    internal Guid RitualTypeId { get; }

    public Guid EntityId => RitualTypeId;

    /// <summary>Whether the game has already wired this family up to its rituals this run.</summary>
    internal bool Initiated { get; }

    /// <summary>The one record on this class that holds a number rather than distributing one.</summary>
    internal BigDouble ActiveRituals { get; }
}

internal sealed class WorldRitualTypeBinder : WorldPlainBinder<WorldRitualType>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _initiated;
    private Func<object, BigDouble>? _activeRituals;

    internal override string Category => "ritual types";

    internal override string TypeName => "RitualTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _initiated = bind.Field<bool>("initiated");
        _activeRituals = bind.ModifierRecord("activeRituals");
        return bind.Failure;
    }

    internal override WorldRitualType Read(object entity) =>
        new(_id!(entity), _initiated!(entity), _activeRituals!(entity));
}

/// <summary>One agromancy element type as published: its identity and its own level.</summary>
internal readonly struct WorldHarvestType : IWorldEntity
{
    internal WorldHarvestType(Guid harvestTypeId, BigDouble level)
    {
        HarvestTypeId = harvestTypeId;
        Level = level;
    }

    internal Guid HarvestTypeId { get; }

    public Guid EntityId => HarvestTypeId;

    /// <summary>The type's level, which the game keeps as a modifier record rather than an integer.</summary>
    internal BigDouble Level { get; }
}

internal sealed class WorldHarvestTypeBinder : WorldPlainBinder<WorldHarvestType>
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _level;

    internal override string Category => "harvest types";

    internal override string TypeName => "HarvestTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.ModifierRecord("level");
        return bind.Failure;
    }

    internal override WorldHarvestType Read(object entity) => new(_id!(entity), _level!(entity));
}

/// <summary>One plot node type as published: its identity and the levels it carries in total.</summary>
internal readonly struct WorldPlotNodeType : IWorldEntity
{
    internal WorldPlotNodeType(Guid plotNodeTypeId, BigDouble totalLevel)
    {
        PlotNodeTypeId = plotNodeTypeId;
        TotalLevel = totalLevel;
    }

    internal Guid PlotNodeTypeId { get; }

    public Guid EntityId => PlotNodeTypeId;

    internal BigDouble TotalLevel { get; }
}

internal sealed class WorldPlotNodeTypeBinder : WorldPlainBinder<WorldPlotNodeType>
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _totalLevel;

    internal override string Category => "plot node types";

    internal override string TypeName => "PlotNodeTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _totalLevel = bind.ModifierRecord("totalLevel");
        return bind.Failure;
    }

    internal override WorldPlotNodeType Read(object entity) =>
        new(_id!(entity), _totalLevel!(entity));
}

/// <summary>
/// One research type as published: its identity and the three level pools it holds numbers for.
/// </summary>
internal readonly struct WorldResearchType : IWorldEntity
{
    internal WorldResearchType(
        Guid researchTypeId,
        bool linkedDevelopCost,
        bool linkedResourceCost,
        bool linkedResearchTime,
        bool ignoreWhenLevelDependent,
        bool persistThroughReset,
        int cachedTotalLevel,
        int cachedPeakLevel,
        int cachedQueuedLevel,
        int cachedDevelopingLevel,
        int cachedQueuedValue,
        int cachedInvestmentLevel,
        int cachedPurchasedLevel,
        BigDouble freeBonusLevels,
        BigDouble usedBonusLevels,
        BigDouble maxInvestmentLevel)
    {
        ResearchTypeId = researchTypeId;
        LinkedDevelopCost = linkedDevelopCost;
        LinkedResourceCost = linkedResourceCost;
        LinkedResearchTime = linkedResearchTime;
        IgnoreWhenLevelDependent = ignoreWhenLevelDependent;
        PersistThroughReset = persistThroughReset;
        CachedTotalLevel = cachedTotalLevel;
        CachedPeakLevel = cachedPeakLevel;
        CachedQueuedLevel = cachedQueuedLevel;
        CachedDevelopingLevel = cachedDevelopingLevel;
        CachedQueuedValue = cachedQueuedValue;
        CachedInvestmentLevel = cachedInvestmentLevel;
        CachedPurchasedLevel = cachedPurchasedLevel;
        FreeBonusLevels = freeBonusLevels;
        UsedBonusLevels = usedBonusLevels;
        MaxInvestmentLevel = maxInvestmentLevel;
    }

    internal Guid ResearchTypeId { get; }

    public Guid EntityId => ResearchTypeId;

    /// <summary>
    /// Which of the three prices the family ties together across its members, and the two rules that
    /// say whether it counts toward level-dependent costs and whether it survives a reset.
    /// </summary>
    internal bool LinkedDevelopCost { get; }

    internal bool LinkedResourceCost { get; }

    internal bool LinkedResearchTime { get; }

    internal bool IgnoreWhenLevelDependent { get; }

    internal bool PersistThroughReset { get; }

    /// <summary>
    /// The seven level readings the game keeps on the family itself. They are the game's own
    /// arithmetic over its members, recomputed as the run moves, and they are published as the game
    /// states them rather than re-derived here.
    /// </summary>
    internal int CachedTotalLevel { get; }

    internal int CachedPeakLevel { get; }

    internal int CachedQueuedLevel { get; }

    internal int CachedDevelopingLevel { get; }

    internal int CachedQueuedValue { get; }

    internal int CachedInvestmentLevel { get; }

    internal int CachedPurchasedLevel { get; }

    /// <summary>How many free levels this type grants, and how many of them are already spent.</summary>
    internal BigDouble FreeBonusLevels { get; }

    internal BigDouble UsedBonusLevels { get; }

    /// <summary>The ceiling this type puts on investment.</summary>
    internal BigDouble MaxInvestmentLevel { get; }
}

internal sealed class WorldResearchTypeBinder : WorldPlainBinder<WorldResearchType>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _linkedDevelopCost;
    private Func<object, bool>? _linkedResourceCost;
    private Func<object, bool>? _linkedResearchTime;
    private Func<object, bool>? _ignoreWhenLevelDependent;
    private Func<object, bool>? _persistThroughReset;
    private Func<object, int>? _cachedTotalLevel;
    private Func<object, int>? _cachedPeakLevel;
    private Func<object, int>? _cachedQueuedLevel;
    private Func<object, int>? _cachedDevelopingLevel;
    private Func<object, int>? _cachedQueuedValue;
    private Func<object, int>? _cachedInvestmentLevel;
    private Func<object, int>? _cachedPurchasedLevel;
    private Func<object, BigDouble>? _freeBonusLevels;
    private Func<object, BigDouble>? _usedBonusLevels;
    private Func<object, BigDouble>? _maxInvestmentLevel;

    internal override string Category => "research types";

    internal override string TypeName => "ResearchTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _linkedDevelopCost = bind.Field<bool>("linkedDevelopCost");
        _linkedResourceCost = bind.Field<bool>("linkedResourceCost");
        _linkedResearchTime = bind.Field<bool>("linkedResearchTime");
        _ignoreWhenLevelDependent = bind.Field<bool>("ignoreWhenLevelDependent");
        _persistThroughReset = bind.Field<bool>("persistThroughReset");
        _cachedTotalLevel = bind.Field<int>("cachedTotalLevel");
        _cachedPeakLevel = bind.Field<int>("cachedPeakLevel");
        _cachedQueuedLevel = bind.Field<int>("cachedQueuedLevel");
        _cachedDevelopingLevel = bind.Field<int>("cachedDevelopingLevel");
        _cachedQueuedValue = bind.Field<int>("cachedQueuedValue");
        _cachedInvestmentLevel = bind.Field<int>("cachedInvestmentLevel");
        _cachedPurchasedLevel = bind.Field<int>("cachedPurchasedLevel");
        _freeBonusLevels = bind.ModifierRecord("freeBonusLevels");
        _usedBonusLevels = bind.ModifierRecord("usedBonusLevels");
        _maxInvestmentLevel = bind.ModifierRecord("maxInvestmentLevel");
        return bind.Failure;
    }

    internal override WorldResearchType Read(object entity) =>
        new(
            _id!(entity),
            _linkedDevelopCost!(entity),
            _linkedResourceCost!(entity),
            _linkedResearchTime!(entity),
            _ignoreWhenLevelDependent!(entity),
            _persistThroughReset!(entity),
            _cachedTotalLevel!(entity),
            _cachedPeakLevel!(entity),
            _cachedQueuedLevel!(entity),
            _cachedDevelopingLevel!(entity),
            _cachedQueuedValue!(entity),
            _cachedInvestmentLevel!(entity),
            _cachedPurchasedLevel!(entity),
            _freeBonusLevels!(entity),
            _usedBonusLevels!(entity),
            _maxInvestmentLevel!(entity));
}

/// <summary>
/// One consumable family as published — the game's Food, Potion, Relic and Treasure buckets. Every
/// record on this class distributes.
/// </summary>
/// <remarks>
/// Named for the family rather than the type because <c>WorldConsumableFamily</c> is already the
/// consumable-to-family membership edge, and the game's own display names for these assets are the
/// family words. The wire category stays <c>consumable-types</c> beside its sibling taxonomies.
/// </remarks>
internal readonly struct WorldConsumableFamily : IWorldEntity
{
    internal WorldConsumableFamily(Guid consumableFamilyId, bool hidden, int sortOrder)
    {
        ConsumableFamilyId = consumableFamilyId;
        Hidden = hidden;
        SortOrder = sortOrder;
    }

    internal Guid ConsumableFamilyId { get; }

    public Guid EntityId => ConsumableFamilyId;

    /// <summary>Whether the family is kept off the screen entirely, and where it sits when it is not.</summary>
    internal bool Hidden { get; }

    internal int SortOrder { get; }
}

internal sealed class WorldConsumableFamilyBinder : WorldPlainBinder<WorldConsumableFamily>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _hidden;
    private Func<object, int>? _sortOrder;

    internal override string Category => "consumable families";

    internal override string TypeName => "ConsumableTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _hidden = bind.Field<bool>("hidden");
        _sortOrder = bind.Field<int>("sortOrder");
        return bind.Failure;
    }

    internal override WorldConsumableFamily Read(object entity) =>
        new(_id!(entity), _hidden!(entity), _sortOrder!(entity));
}

/// <summary>One agromancy action type as published. Every record on this class distributes.</summary>
internal readonly struct WorldHarvestActionType : IWorldEntity
{
    internal WorldHarvestActionType(Guid harvestActionTypeId) =>
        HarvestActionTypeId = harvestActionTypeId;

    internal Guid HarvestActionTypeId { get; }

    public Guid EntityId => HarvestActionTypeId;
}

internal sealed class WorldHarvestActionTypeBinder : WorldPlainBinder<WorldHarvestActionType>
{
    private Func<object, Guid>? _id;

    internal override string Category => "harvest action types";

    internal override string TypeName => "HarvestActionTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        return bind.Failure;
    }

    internal override WorldHarvestActionType Read(object entity) => new(_id!(entity));
}

/// <summary>One passive ability type as published. Every record on this class distributes.</summary>
internal readonly struct WorldPassiveAbilityType : IWorldEntity
{
    internal WorldPassiveAbilityType(Guid passiveAbilityTypeId) =>
        PassiveAbilityTypeId = passiveAbilityTypeId;

    internal Guid PassiveAbilityTypeId { get; }

    public Guid EntityId => PassiveAbilityTypeId;
}

internal sealed class WorldPassiveAbilityTypeBinder : WorldPlainBinder<WorldPassiveAbilityType>
{
    private Func<object, Guid>? _id;

    internal override string Category => "passive ability types";

    internal override string TypeName => "PassiveAbilityTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        return bind.Failure;
    }

    internal override WorldPassiveAbilityType Read(object entity) => new(_id!(entity));
}

/// <summary>One time rune type as published: its identity and the levels it carries in total.</summary>
internal readonly struct WorldTimeRuneType : IWorldEntity
{
    internal WorldTimeRuneType(Guid timeRuneTypeId, bool initialized, BigDouble totalLevel)
    {
        TimeRuneTypeId = timeRuneTypeId;
        Initialized = initialized;
        TotalLevel = totalLevel;
    }

    internal Guid TimeRuneTypeId { get; }

    public Guid EntityId => TimeRuneTypeId;

    /// <summary>Whether the game has already wired this family up to its runes this run.</summary>
    internal bool Initialized { get; }

    internal BigDouble TotalLevel { get; }
}

internal sealed class WorldTimeRuneTypeBinder : WorldPlainBinder<WorldTimeRuneType>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _initialized;
    private Func<object, BigDouble>? _totalLevel;

    internal override string Category => "time rune types";

    internal override string TypeName => "TimeRuneTypeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _initialized = bind.Field<bool>("initialized");
        _totalLevel = bind.ModifierRecord("totalLevel");
        return bind.Failure;
    }

    internal override WorldTimeRuneType Read(object entity) =>
        new(_id!(entity), _initialized!(entity), _totalLevel!(entity));
}

using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One plot node action as published: whether it has been used, what it costs, the five records that
/// scale it, and what its author decided it does.
/// </summary>
internal readonly struct WorldPlotNodeAction : IWorldEntity
{
    internal WorldPlotNodeAction(
        Guid plotNodeActionId,
        bool hasBeenUsed,
        bool isGrowingAction,
        bool showExitTooltip,
        int elementCost,
        bool useSizeModForCost,
        bool useAnyStateForCost,
        bool parallelAction,
        double baseTime,
        bool useSpaceUsageForTimeMult,
        bool ignoreNodeYield,
        bool useParentSize,
        bool useParentQuality,
        BigDouble power,
        BigDouble speed,
        BigDouble costMod,
        BigDouble growthSizeMod,
        BigDouble refundRating,
        int sizeModNodeCount,
        int elementCostType,
        int elementCostExitPhase,
        int prerequisiteCount,
        int resourceCostCount,
        int persistentEffectCount,
        int completionEffectCount)
    {
        PlotNodeActionId = plotNodeActionId;
        HasBeenUsed = hasBeenUsed;
        IsGrowingAction = isGrowingAction;
        ShowExitTooltip = showExitTooltip;
        ElementCost = elementCost;
        UseSizeModForCost = useSizeModForCost;
        UseAnyStateForCost = useAnyStateForCost;
        ParallelAction = parallelAction;
        BaseTime = baseTime;
        UseSpaceUsageForTimeMult = useSpaceUsageForTimeMult;
        IgnoreNodeYield = ignoreNodeYield;
        UseParentSize = useParentSize;
        UseParentQuality = useParentQuality;
        Power = power;
        Speed = speed;
        CostMod = costMod;
        GrowthSizeMod = growthSizeMod;
        RefundRating = refundRating;
        SizeModNodeCount = sizeModNodeCount;
        ElementCostType = elementCostType;
        ElementCostExitPhase = elementCostExitPhase;
        PrerequisiteCount = prerequisiteCount;
        ResourceCostCount = resourceCostCount;
        PersistentEffectCount = persistentEffectCount;
        CompletionEffectCount = completionEffectCount;
    }

    internal Guid PlotNodeActionId { get; }

    public Guid EntityId => PlotNodeActionId;

    internal bool HasBeenUsed { get; }

    internal bool IsGrowingAction { get; }

    internal bool ShowExitTooltip { get; }

    internal int ElementCost { get; }

    internal bool UseSizeModForCost { get; }

    internal bool UseAnyStateForCost { get; }

    internal bool ParallelAction { get; }

    internal double BaseTime { get; }

    internal bool UseSpaceUsageForTimeMult { get; }

    internal bool IgnoreNodeYield { get; }

    internal bool UseParentSize { get; }

    internal bool UseParentQuality { get; }

    internal BigDouble Power { get; }

    internal BigDouble Speed { get; }

    internal BigDouble CostMod { get; }

    internal BigDouble GrowthSizeMod { get; }

    internal BigDouble RefundRating { get; }

    /// <summary>
    /// How many other nodes this action takes its size multiplier from. Zero is the ordinary case and
    /// means the multiplier is the empty product; anything else puts the action's element cost behind
    /// a chain this suite has not ported.
    /// </summary>
    internal int SizeModNodeCount { get; }

    /// <summary>How the element cost is charged, as the game's enum's underlying integer.</summary>
    internal int ElementCostType { get; }

    /// <summary>Which phase the element cost is charged on leaving, as its underlying integer.</summary>
    internal int ElementCostExitPhase { get; }

    /// <summary>How many conditions gate the action.</summary>
    internal int PrerequisiteCount { get; }

    /// <summary>How many resources one run of the action drains.</summary>
    internal int ResourceCostCount { get; }

    /// <summary>How many standing effects the action applies for as long as it runs.</summary>
    internal int PersistentEffectCount { get; }

    /// <summary>
    /// How many effect blocks one completed run applies, as the action authors them.
    /// </summary>
    /// <remarks>
    /// The blocks themselves are rows of their own. This is the authored length, including any entry
    /// that could not be read — so a consumer that requires exactly one block can tell "the author
    /// wrote one" from "one of several was legible".
    /// </remarks>
    internal int CompletionEffectCount { get; }
}

internal sealed class WorldPlotNodeActionBinder : WorldPlainBinder<WorldPlotNodeAction>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _hasBeenUsed;
    private Func<object, bool>? _isGrowingAction;
    private Func<object, bool>? _showExitTooltip;
    private Func<object, int>? _elementCost;
    private Func<object, bool>? _useSizeModForCost;
    private Func<object, bool>? _useAnyStateForCost;
    private Func<object, bool>? _parallelAction;
    private Func<object, double>? _baseTime;
    private Func<object, bool>? _useSpaceUsageForTimeMult;
    private Func<object, bool>? _ignoreNodeYield;
    private Func<object, bool>? _useParentSize;
    private Func<object, bool>? _useParentQuality;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _speed;
    private Func<object, BigDouble>? _costMod;
    private Func<object, BigDouble>? _growthSizeMod;
    private Func<object, BigDouble>? _refundRating;
    private Func<object, int>? _sizeModNodeCount;
    private Func<object, int>? _elementCostType;
    private Func<object, int>? _elementCostExitPhase;
    private Func<object, int>? _prerequisiteCount;
    private Func<object, int>? _resourceCostCount;
    private Func<object, int>? _persistentEffectCount;
    private Func<object, int>? _completionEffectCount;

    internal override string Category => "plot node actions";

    internal override string TypeName => "PlotNodeActionSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _hasBeenUsed = bind.Field<bool>("hasBeenUsed");
        _isGrowingAction = bind.Field<bool>("isGrowingAction");
        _showExitTooltip = bind.Field<bool>("showExitTooltip");
        _elementCost = bind.Field<int>("elementCost");
        _useSizeModForCost = bind.Field<bool>("useSizeModForCost");
        _useAnyStateForCost = bind.Field<bool>("useAnyStateForCost");
        _parallelAction = bind.Field<bool>("parallelAction");
        _baseTime = bind.Field<double>("baseTime");
        _useSpaceUsageForTimeMult = bind.Field<bool>("useSpaceUsageForTimeMult");
        _ignoreNodeYield = bind.Field<bool>("ignoreNodeYield");
        _useParentSize = bind.Field<bool>("useParentSize");
        _useParentQuality = bind.Field<bool>("useParentQuality");
        _power = bind.ModifierRecord("power");
        _speed = bind.ModifierRecord("speed");
        _costMod = bind.ModifierRecord("costMod");
        _growthSizeMod = bind.ModifierRecord("growthSizeMod");
        _refundRating = bind.ModifierRecord("refundRating");

        // Only the count: the nodes themselves are a list, and how many there are is the whole fact
        // a cost consumer needs — zero means the size multiplier is the empty product, one.
        _sizeModNodeCount = bind.CollectionCount("sizeModNodes");

        // What the action's author decided it costs and does. These change with the game's content
        // rather than with play, and a consumer that has to know an action is safe to run asks them
        // of the snapshot instead of asking the live object at the moment it mutates.
        _elementCostType = bind.EnumField("elementCostType");
        _elementCostExitPhase = bind.EnumField("elementCostExitPhase");
        _prerequisiteCount = bind.NestedCollectionCount("prerequisites", "prerequisites");
        _resourceCostCount = bind.NestedCollectionCount("actionDrain", "costs");
        _persistentEffectCount = bind.CollectionCount("actionEffects");
        _completionEffectCount = bind.CollectionCount("completeEffects");
        return bind.Failure;
    }

    internal override WorldPlotNodeAction Read(object entity) =>
        new(
            _id!(entity),
            _hasBeenUsed!(entity),
            _isGrowingAction!(entity),
            _showExitTooltip!(entity),
            _elementCost!(entity),
            _useSizeModForCost!(entity),
            _useAnyStateForCost!(entity),
            _parallelAction!(entity),
            _baseTime!(entity),
            _useSpaceUsageForTimeMult!(entity),
            _ignoreNodeYield!(entity),
            _useParentSize!(entity),
            _useParentQuality!(entity),
            _power!(entity),
            _speed!(entity),
            _costMod!(entity),
            _growthSizeMod!(entity),
            _refundRating!(entity),
            _sizeModNodeCount!(entity),
            _elementCostType!(entity),
            _elementCostExitPhase!(entity),
            _prerequisiteCount!(entity),
            _resourceCostCount!(entity),
            _persistentEffectCount!(entity),
            _completionEffectCount!(entity));
}

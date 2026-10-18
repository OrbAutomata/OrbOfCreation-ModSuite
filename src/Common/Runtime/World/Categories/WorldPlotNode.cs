using System;
using System.Collections;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One plot node as read — the scalar half, plus the two quantities its phase instances sum to. The
/// size modifiers are a list and are still not carried; see
/// <c>docs/runtime-architecture/world-collection.md</c>.
/// </summary>
internal readonly struct RawPlotNodeSample : IWorldEntity
{
    internal RawPlotNodeSample(
        Guid plotNodeId,
        bool visible,
        BigDouble currentTime,
        BigDouble nextErraticTime,
        BigDouble sizeLevel,
        BigDouble masteryXp,
        int masteryLevel,
        bool noMastery,
        bool noSizeDisplay,
        bool useVisibilityPrereq,
        bool hasErraticGrowth,
        bool debugMode,
        int erraticQuantity,
        BigDouble actionQuantityUsageMain,
        BigDouble actionQuantityUsageAny,
        BigDouble actionXpRate,
        BigDouble yieldMod,
        BigDouble specialMod,
        BigDouble actionSpeed,
        BigDouble actionCostMod,
        BigDouble growingSpeed,
        BigDouble restingSpeed,
        BigDouble sizeMod,
        BigDouble qualityMod,
        BigDouble recoverySizeMod,
        BigDouble naturalGrowth,
        BigDouble naturalGrowthPower,
        int lastQuantity,
        int idleQuantity,
        int totalQuantity)
    {
        PlotNodeId = plotNodeId;
        Visible = visible;
        CurrentTime = currentTime;
        NextErraticTime = nextErraticTime;
        SizeLevel = sizeLevel;
        MasteryXp = masteryXp;
        MasteryLevel = masteryLevel;
        NoMastery = noMastery;
        NoSizeDisplay = noSizeDisplay;
        UseVisibilityPrereq = useVisibilityPrereq;
        HasErraticGrowth = hasErraticGrowth;
        DebugMode = debugMode;
        ErraticQuantity = erraticQuantity;
        ActionQuantityUsageMain = actionQuantityUsageMain;
        ActionQuantityUsageAny = actionQuantityUsageAny;
        ActionXpRate = actionXpRate;
        YieldMod = yieldMod;
        SpecialMod = specialMod;
        ActionSpeed = actionSpeed;
        ActionCostMod = actionCostMod;
        GrowingSpeed = growingSpeed;
        RestingSpeed = restingSpeed;
        SizeMod = sizeMod;
        QualityMod = qualityMod;
        RecoverySizeMod = recoverySizeMod;
        NaturalGrowth = naturalGrowth;
        NaturalGrowthPower = naturalGrowthPower;
        LastQuantity = lastQuantity;
        IdleQuantity = idleQuantity;
        TotalQuantity = totalQuantity;
    }

    internal Guid PlotNodeId { get; }

    public Guid EntityId => PlotNodeId;

    internal bool Visible { get; }

    /// <summary>Where the node is on its own clock, and when the next erratic event is due.</summary>
    internal BigDouble CurrentTime { get; }

    internal BigDouble NextErraticTime { get; }

    internal BigDouble SizeLevel { get; }

    internal BigDouble MasteryXp { get; }

    internal int MasteryLevel { get; }

    /// <summary>
    /// The rest of the node's runtime state: the growth flags, the quantities it is carrying, and the
    /// fourteen cached records its yield is a function of.
    /// </summary>
    internal bool NoMastery { get; }

    internal bool NoSizeDisplay { get; }

    internal bool UseVisibilityPrereq { get; }

    internal bool HasErraticGrowth { get; }

    internal bool DebugMode { get; }

    internal int ErraticQuantity { get; }

    internal BigDouble ActionQuantityUsageMain { get; }

    internal BigDouble ActionQuantityUsageAny { get; }

    internal BigDouble ActionXpRate { get; }

    internal BigDouble YieldMod { get; }

    internal BigDouble SpecialMod { get; }

    internal BigDouble ActionSpeed { get; }

    internal BigDouble ActionCostMod { get; }

    internal BigDouble GrowingSpeed { get; }

    internal BigDouble RestingSpeed { get; }

    internal BigDouble SizeMod { get; }

    internal BigDouble QualityMod { get; }

    internal BigDouble RecoverySizeMod { get; }

    internal BigDouble NaturalGrowth { get; }

    internal BigDouble NaturalGrowthPower { get; }

    internal int LastQuantity { get; }

    /// <summary>
    /// How many of this node are idle — the game's <c>GetQuantity()</c>, which is the Idle phase
    /// instance's timer count and nothing else.
    /// </summary>
    internal int IdleQuantity { get; }

    /// <summary>
    /// How many of this node exist across every authored phase — the game's <c>GetTotalQuantity()</c>.
    /// Idle plus everything growing or resting.
    /// </summary>
    internal int TotalQuantity { get; }
}

/// <summary>One plot node as published.</summary>
internal readonly struct WorldPlotNode : IWorldEntity
{
    internal WorldPlotNode(
        in RawPlotNodeSample reading,
        int remainingQuantity,
        int remainingTotalQuantity)
    {
        Reading = reading;
        RemainingQuantity = remainingQuantity;
        RemainingTotalQuantity = remainingTotalQuantity;
    }

    internal RawPlotNodeSample Reading { get; }

    public Guid EntityId => Reading.PlotNodeId;

    /// <summary>
    /// How many of this node an action may still be started on, which is not the same as how many
    /// exist. Ported from <c>PlotNodeSO.GetRemainingQuantity()</c>:
    /// <c>GetQuantity() - actionQuantityUsageMain.AsInt() - Max(actionQuantityUsageAny.AsInt() -
    /// GetOtherQuantity(Idle), 0)</c>, where <c>GetOtherQuantity(Idle)</c> is total minus idle.
    /// </summary>
    /// <remarks>
    /// The asymmetry between the two usage terms is the original's and is easy to mistake for a bug:
    /// the <em>main</em> usage comes straight off the idle count, while the <em>any</em> usage is
    /// first absorbed by whatever is busy growing or resting and only bites the idle count once that
    /// runs out. Reproduced as written.
    /// </remarks>
    internal int RemainingQuantity { get; }

    /// <summary>
    /// The same question asked of every phase rather than only the idle one, ported from
    /// <c>PlotNodeSO.GetRemainingTotalQuantity()</c>: <c>GetTotalQuantity() - usageAny - usageMain</c>.
    /// An action whose cost may be paid from any state draws on this one instead.
    /// </summary>
    /// <remarks>
    /// Note that this is not <see cref="RemainingQuantity"/> plus what is busy: both usage terms come
    /// off the total flat here, with none of the absorption the idle-only form does.
    /// </remarks>
    internal int RemainingTotalQuantity { get; }
}

internal sealed class WorldPlotNodeBinder : WorldRowBinder<RawPlotNodeSample, WorldPlotNode>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _visible;
    private Func<object, BigDouble>? _currentTime;
    private Func<object, BigDouble>? _nextErraticTime;
    private Func<object, BigDouble>? _sizeLevel;
    private Func<object, BigDouble>? _masteryXp;
    private Func<object, int>? _masteryLevel;
    private Func<object, bool>? _noMastery;
    private Func<object, bool>? _noSizeDisplay;
    private Func<object, bool>? _useVisibilityPrereq;
    private Func<object, bool>? _hasErraticGrowth;
    private Func<object, bool>? _debugMode;
    private Func<object, int>? _erraticQuantity;
    private Func<object, BigDouble>? _actionQuantityUsageMain;
    private Func<object, BigDouble>? _actionQuantityUsageAny;
    private Func<object, BigDouble>? _actionXpRate;
    private Func<object, BigDouble>? _yieldMod;
    private Func<object, BigDouble>? _specialMod;
    private Func<object, BigDouble>? _actionSpeed;
    private Func<object, BigDouble>? _actionCostMod;
    private Func<object, BigDouble>? _growingSpeed;
    private Func<object, BigDouble>? _restingSpeed;
    private Func<object, BigDouble>? _sizeMod;
    private Func<object, BigDouble>? _qualityMod;
    private Func<object, BigDouble>? _recoverySizeMod;
    private Func<object, BigDouble>? _naturalGrowth;
    private Func<object, BigDouble>? _naturalGrowthPower;
    private Func<object, int>? _lastQuantity;
    private WorldPlotPhaseQuantities _phases;

    internal override string Category => "plot nodes";

    internal override string TypeName => "PlotNodeSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.StableUuid();
        _visible = bind.Field<bool>("visible");
        _currentTime = bind.Field<BigDouble>("currentTime");
        _nextErraticTime = bind.Field<BigDouble>("nextErraticTime");
        _sizeLevel = bind.Field<BigDouble>("sizeLevel");
        _masteryXp = bind.Field<BigDouble>("masteryXp");
        _masteryLevel = bind.Field<int>("masteryLevel");
        _noMastery = bind.Field<bool>("noMastery");
        _noSizeDisplay = bind.Field<bool>("noSizeDisplay");
        _useVisibilityPrereq = bind.Field<bool>("useVisibilityPrereq");
        _hasErraticGrowth = bind.Field<bool>("hasErraticGrowth");
        _debugMode = bind.Field<bool>("debugMode");
        _erraticQuantity = bind.Field<int>("erraticQuantity");
        _actionQuantityUsageMain = bind.ModifierRecord("actionQuantityUsageMain");
        _actionQuantityUsageAny = bind.ModifierRecord("actionQuantityUsageAny");
        _actionXpRate = bind.ModifierRecord("actionXpRate");
        _yieldMod = bind.ModifierRecord("yieldMod");
        _specialMod = bind.ModifierRecord("specialMod");
        _actionSpeed = bind.ModifierRecord("actionSpeed");
        _actionCostMod = bind.ModifierRecord("actionCostMod");
        _growingSpeed = bind.ModifierRecord("growingSpeed");
        _restingSpeed = bind.ModifierRecord("restingSpeed");
        _sizeMod = bind.ModifierRecord("sizeMod");
        _qualityMod = bind.ModifierRecord("qualityMod");
        _recoverySizeMod = bind.ModifierRecord("recoverySizeMod");
        _naturalGrowth = bind.ModifierRecord("naturalGrowth");
        _naturalGrowthPower = bind.ModifierRecord("naturalGrowthPower");
        _lastQuantity = bind.Field<int>("lastQuantity");
        _phases = WorldPlotPhaseQuantities.Bind(bind);
        return bind.Failure;
    }

    internal override RawPlotNodeSample Read(object entity) =>
        new(
            _id!(entity),
            _visible!(entity),
            _currentTime!(entity),
            _nextErraticTime!(entity),
            _sizeLevel!(entity),
            _masteryXp!(entity),
            _masteryLevel!(entity),
            _noMastery!(entity),
            _noSizeDisplay!(entity),
            _useVisibilityPrereq!(entity),
            _hasErraticGrowth!(entity),
            _debugMode!(entity),
            _erraticQuantity!(entity),
            _actionQuantityUsageMain!(entity),
            _actionQuantityUsageAny!(entity),
            _actionXpRate!(entity),
            _yieldMod!(entity),
            _specialMod!(entity),
            _actionSpeed!(entity),
            _actionCostMod!(entity),
            _growingSpeed!(entity),
            _restingSpeed!(entity),
            _sizeMod!(entity),
            _qualityMod!(entity),
            _recoverySizeMod!(entity),
            _naturalGrowth!(entity),
            _naturalGrowthPower!(entity),
            _lastQuantity!(entity),
            _phases.Idle(entity),
            _phases.Total(entity));
}

/// <summary>
/// Sums a plot node's phase instances the way the game does, without asking it to.
/// </summary>
/// <remarks>
/// <para>
/// The obvious route — <c>GetQuantity()</c> and <c>GetTotalQuantity()</c> — is closed to collection.
/// Both reach the instances through <c>GetPhaseInstance(phase)</c>, which lazily builds a dictionary
/// cache and <em>creates a missing instance</em> on the way past. That is a write to game state, on
/// the Unity thread, from a pass whose whole contract is that it does not write. So the fields are
/// walked directly instead.
/// </para>
/// <para>
/// The total sums only instances whose phase the node actually authors, because the game's own total
/// iterates <c>phaseInfos</c> rather than the instances. An unauthored instance is not part of the
/// node as far as every other calculation is concerned, and counting it here would put the two
/// answers quietly out of step.
/// </para>
/// </remarks>
internal readonly struct WorldPlotPhaseQuantities
{
    private const int IdlePhase = 0;

    private readonly Func<object, IList?>? _instances;
    private readonly Func<object, IList?>? _infos;
    private readonly Func<object, int>? _instancePhase;
    private readonly Func<object, int>? _infoPhase;
    private readonly Func<object, int>? _count;

    private WorldPlotPhaseQuantities(
        Func<object, IList?>? instances,
        Func<object, IList?>? infos,
        Func<object, int>? instancePhase,
        Func<object, int>? infoPhase,
        Func<object, int>? count)
    {
        _instances = instances;
        _infos = infos;
        _instancePhase = instancePhase;
        _infoPhase = infoPhase;
        _count = count;
    }

    internal static WorldPlotPhaseQuantities Bind(WorldMemberBinding bind)
    {
        if (bind is null) throw new ArgumentNullException(nameof(bind));

        var instances = bind.CollectionField("phaseInstances");
        var infos = bind.CollectionField("phaseInfos");
        var instanceType = bind.CollectionElementType("phaseInstances");
        var infoType = bind.CollectionElementType("phaseInfos");

        return new WorldPlotPhaseQuantities(
            instances,
            infos,
            bind.Elements(instanceType, "PlotNodePhaseInstance").EnumField("phase"),
            bind.Elements(infoType, "PlotNodePhaseInfo").EnumField("phase"),
            bind.Elements(instanceType, "PlotNodePhaseInstance").NestedField<int>("timers", "q"));
    }

    /// <summary>The Idle phase instance's count, which is what the game calls <c>GetQuantity()</c>.</summary>
    internal int Idle(object plotNode) => QuantityOf(plotNode, IdlePhase);

    internal int Total(object plotNode)
    {
        if (_infos is null) return 0;

        var infos = _infos(plotNode);
        if (infos is null) return 0;

        var total = 0;
        for (var index = 0; index < infos.Count; index++)
        {
            var info = infos[index];
            if (info is null) continue;
            total += QuantityOf(plotNode, _infoPhase!(info));
        }

        return total;
    }

    private int QuantityOf(object plotNode, int phase)
    {
        if (_instances is null) return 0;

        var instances = _instances(plotNode);
        if (instances is null) return 0;

        for (var index = 0; index < instances.Count; index++)
        {
            var instance = instances[index];
            if (instance is null || _instancePhase!(instance) != phase) continue;
            return _count!(instance);
        }

        // A phase the node authors but has never instantiated holds nothing, which is the same answer
        // the game gives — it would create the instance at zero and read that.
        return 0;
    }
}

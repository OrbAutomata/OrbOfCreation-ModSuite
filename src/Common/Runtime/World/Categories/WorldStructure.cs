using System;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One structure reading — a levelled thing the player develops. Shown in game as an
/// <em>Attribute</em>; see <c>docs/reverse-engineering/naming-traps.md</c>.
/// </summary>
internal readonly struct RawStructureSample : IWorldEntity
{
    internal RawStructureSample(
        Guid structureId,
        Guid structureTypeId,
        BigDouble level,
        BigDouble queuedLevels,
        bool unlocked,
        int queuedEchos,
        int completedEchos,
        int selfBonusLevels,
        BigDouble queueTimeLeft,
        BigDouble currentBuildTime,
        bool flagged,
        int baseLevel,
        float queueTimeTotal,
        bool debugStructure,
        bool disabled,
        int observableId,
        bool insufficientReqPenaltyActive,
        int bufferDevelopedQuantity,
        Guid costPerQuantityId,
        in RawStructureModifiers modifiers)
    {
        StructureId = structureId;
        StructureTypeId = structureTypeId;
        Level = level;
        QueuedLevels = queuedLevels;
        Unlocked = unlocked;
        QueuedEchos = queuedEchos;
        CompletedEchos = completedEchos;
        SelfBonusLevels = selfBonusLevels;
        QueueTimeLeft = queueTimeLeft;
        CurrentBuildTime = currentBuildTime;
        Flagged = flagged;
        BaseLevel = baseLevel;
        QueueTimeTotal = queueTimeTotal;
        DebugStructure = debugStructure;
        Disabled = disabled;
        ObservableId = observableId;
        InsufficientReqPenaltyActive = insufficientReqPenaltyActive;
        BufferDevelopedQuantity = bufferDevelopedQuantity;
        CostPerQuantityId = costPerQuantityId;
        Modifiers = modifiers;
    }

    internal Guid StructureId { get; }

    /// <summary>The identity every category-generic lookup and traversal reads.</summary>
    public Guid EntityId => StructureId;

    /// <summary>
    /// The attribute tab that owns this structure. Scholar uses the same <c>StructureSO</c> shape as
    /// Wizardry; this edge is what lets a consumer distinguish the two without a name table.
    /// </summary>
    internal Guid StructureTypeId { get; }

    /// <summary>
    /// Levels already owned, from <c>StructureSO.GetPurchaseLevel()</c> — the accessor that
    /// forwards to <c>GetBaseLevel()</c>, which is the number the attribute's badge draws and the
    /// count the purchase-cost chain scales by. The accessor is the contract; the <c>quantity</c>
    /// field it returns is not grabbed a second time.
    /// </summary>
    internal BigDouble Level { get; }

    /// <summary>Levels bought and still developing. Paid for, not yet effective.</summary>
    internal BigDouble QueuedLevels { get; }

    internal bool Unlocked { get; }

    /// <summary>Echo levels queued, and how many of them have completed.</summary>
    internal int QueuedEchos { get; }

    internal int CompletedEchos { get; }

    /// <summary>Levels the structure granted itself, excluded from the purchase level.</summary>
    internal int SelfBonusLevels { get; }

    /// <summary>
    /// Seconds left on the level currently developing, counting down, against the
    /// <see cref="CurrentBuildTime"/> it started from. That pairing is the game's own:
    /// <c>GetQueueTimeRatio() => 1 - queueTimeLeft / GetActionTime()</c>, and
    /// <c>GetActionTime()</c> returns <c>currentBuildTime</c> — not the authored
    /// <c>queueTimeTotal</c>, which is only an input to computing it.
    /// </summary>
    internal BigDouble QueueTimeLeft { get; }

    internal BigDouble CurrentBuildTime { get; }

    /// <summary>The player's own marker on this structure. Not a game rule, but it is intent.</summary>
    internal bool Flagged { get; }

    /// <summary>The level the structure starts from, before anything is bought.</summary>
    internal int BaseLevel { get; }

    /// <summary>
    /// The authored build time. An input to <see cref="CurrentBuildTime"/> rather than the time a
    /// level actually takes, which is why progress is measured against the latter.
    /// </summary>
    internal float QueueTimeTotal { get; }

    /// <summary>The game's own debug flag for this entry.</summary>
    internal bool DebugStructure { get; }

    /// <summary>Whether the player disabled this structure's effect.</summary>
    internal bool Disabled { get; }

    /// <summary>The observable stamp the game bumps when this structure's value moves.</summary>
    internal int ObservableId { get; }

    /// <summary>Whether the penalty for unmet requirements is currently applied.</summary>
    internal bool InsufficientReqPenaltyActive { get; }

    /// <summary>Levels developed but not yet folded into the owned count.</summary>
    internal int BufferDevelopedQuantity { get; }

    /// <summary>
    /// The global modifier this structure's cost grows by, as an identity into
    /// <c>GameWorldState.ModifierVariables</c>. Empty when the structure references no modifier,
    /// which the game reads as "no scaling" and the deriver reads as "cannot price this".
    /// </summary>
    internal Guid CostPerQuantityId { get; }

    internal RawStructureModifiers Modifiers { get; }
}

/// <summary>
/// A structure's cached modifier records, grouped so the reading stays legible.
/// </summary>
/// <remarks>
/// These are the numbers the game has already calculated for its own display, and the reason the
/// shared collection is worth doing at all: the cost chain, the effect a level buys, and the time a
/// level takes are all functions of these, so owning them means owning the answer without asking the
/// game to recompute one.
/// </remarks>
internal readonly struct RawStructureModifiers
{
    internal RawStructureModifiers(
        BigDouble power,
        BigDouble powerScaling,
        BigDouble speed,
        BigDouble passiveCostMod,
        BigDouble activeCostMod,
        BigDouble costScalingMod,
        BigDouble attributeRankEffectMod,
        BigDouble drainCostMod,
        BigDouble bonusLevels,
        BigDouble effectLevels,
        BigDouble buildSpeed,
        BigDouble echoBuildRating,
        BigDouble powerBuildRating)
    {
        Power = power;
        PowerScaling = powerScaling;
        Speed = speed;
        PassiveCostMod = passiveCostMod;
        ActiveCostMod = activeCostMod;
        CostScalingMod = costScalingMod;
        AttributeRankEffectMod = attributeRankEffectMod;
        DrainCostMod = drainCostMod;
        BonusLevels = bonusLevels;
        EffectLevels = effectLevels;
        BuildSpeed = buildSpeed;
        EchoBuildRating = echoBuildRating;
        PowerBuildRating = powerBuildRating;
    }

    /// <summary>Percent scaling on what one level does, and on how that effect grows with level.</summary>
    internal BigDouble Power { get; }

    internal BigDouble PowerScaling { get; }

    /// <summary>Percent scaling on the structure's action rate.</summary>
    internal BigDouble Speed { get; }

    /// <summary>Percent scaling on passive and active costs respectively.</summary>
    internal BigDouble PassiveCostMod { get; }

    internal BigDouble ActiveCostMod { get; }

    /// <summary>
    /// Percent scaling on how fast cost grows per owned level — the term
    /// <c>GetNextCost()</c> multiplies the per-quantity modifier by.
    /// </summary>
    internal BigDouble CostScalingMod { get; }

    internal BigDouble AttributeRankEffectMod { get; }

    /// <summary>Percent scaling on the structure's ongoing upkeep.</summary>
    internal BigDouble DrainCostMod { get; }

    /// <summary>Levels granted from elsewhere, and levels that count only toward effects.</summary>
    internal BigDouble BonusLevels { get; }

    internal BigDouble EffectLevels { get; }

    /// <summary>Percent scaling on development speed, plus the two build-rating chances.</summary>
    internal BigDouble BuildSpeed { get; }

    internal BigDouble EchoBuildRating { get; }

    internal BigDouble PowerBuildRating { get; }
}

/// <summary>One structure as published — shown in game as an <em>Attribute</em>.</summary>
internal readonly struct WorldStructure : IWorldEntity
{
    internal WorldStructure(
        in RawStructureSample reading,
        BigDouble committedLevel,
        bool hasWorkInFlight,
        BigDouble effectiveLevel,
        double developmentProgress)
    {
        Reading = reading;
        CommittedLevel = committedLevel;
        HasWorkInFlight = hasWorkInFlight;
        EffectiveLevel = effectiveLevel;
        DevelopmentProgress = developmentProgress;
    }

    internal RawStructureSample Reading { get; }

    public Guid EntityId => Reading.StructureId;

    /// <summary>
    /// Levels owned plus levels already paid for and developing. This, not the owned level, is what a
    /// purchase decision must compare against: queued levels have already been bought, so ranking on
    /// the owned level alone re-buys work that is in flight.
    /// </summary>
    internal BigDouble CommittedLevel { get; }

    internal bool HasWorkInFlight { get; }

    /// <summary>
    /// Owned levels plus every kind of granted level — self bonus, bonus, and effect levels. What the
    /// structure currently <em>does</em>, as against <see cref="CommittedLevel"/>, which is what the
    /// next purchase is priced from.
    /// </summary>
    internal BigDouble EffectiveLevel { get; }

    /// <summary>
    /// How far the level in flight has come, in <c>[0, 1]</c>. Zero when nothing is developing and
    /// when the total build time is unknown, so a consumer never reads progress into an idle
    /// structure.
    /// </summary>
    internal double DevelopmentProgress { get; }
}

/// <summary>Structures — shown in game as <em>Attributes</em>: owned levels, queued work, unlock state.</summary>
internal sealed class WorldStructureBinder : WorldRowBinder<RawStructureSample, WorldStructure>
{
    private Func<object, Guid>? _id;
    private Func<object, Guid>? _structureTypeId;
    private Func<object, int>? _level;
    private Func<object, int>? _queued;
    private Func<object, bool>? _unlocked;
    private Func<object, int>? _baseLevel;
    private Func<object, float>? _queueTimeTotal;
    private Func<object, bool>? _debugStructure;
    private Func<object, bool>? _disabled;
    private Func<object, int>? _observableId;
    private Func<object, bool>? _insufficientReqPenalty;
    private Func<object, int>? _bufferDeveloped;
    private Func<object, Guid>? _costPerQuantityId;
    private Func<object, int>? _queuedEchos;
    private Func<object, int>? _completedEchos;
    private Func<object, int>? _selfBonusLevels;
    private Func<object, BigDouble>? _queueTimeLeft;
    private Func<object, BigDouble>? _currentBuildTime;
    private Func<object, bool>? _flagged;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _powerScaling;
    private Func<object, BigDouble>? _speed;
    private Func<object, BigDouble>? _passiveCostMod;
    private Func<object, BigDouble>? _activeCostMod;
    private Func<object, BigDouble>? _costScalingMod;
    private Func<object, BigDouble>? _attributeRankEffectMod;
    private Func<object, BigDouble>? _drainCostMod;
    private Func<object, BigDouble>? _bonusLevels;
    private Func<object, BigDouble>? _effectLevels;
    private Func<object, BigDouble>? _buildSpeed;
    private Func<object, BigDouble>? _echoBuildRating;
    private Func<object, BigDouble>? _powerBuildRating;

    internal override string Category => "structures";

    internal override string TypeName => "StructureSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _structureTypeId = bind.ReferenceGuid("structureType");

        // GetPurchaseLevel forwards to GetBaseLevel, which returns the persisted quantity — the same
        // number the purchase-cost chain scales by, and deliberately excluding every granted level.
        _level = bind.Call<int>("GetPurchaseLevel");
        _queued = bind.Call<int>("GetQueuedQuantity");
        _unlocked = bind.Call<bool>("IsAvailable");
        _baseLevel = bind.Field<int>("baseLevel");
        _queueTimeTotal = bind.Field<float>("queueTimeTotal");
        _debugStructure = bind.Field<bool>("debugStructure");
        _disabled = bind.Field<bool>("disabled");
        _observableId = bind.Field<int>("observableId");
        _insufficientReqPenalty = bind.Field<bool>("insufficientReqPenaltyActive");
        _bufferDeveloped = bind.Field<int>("bufferDevelopedQuantity");

        // A ValueModifierRef holds the modifier variable, not the modifier: the identity is the edge,
        // and the global registry carries the arithmetic. See D17.
        _costPerQuantityId = bind.Through("costPerQuantity").ReferenceGuid("variable");
        _queuedEchos = bind.Field<int>("queuedEchos");
        _completedEchos = bind.Field<int>("completedEchos");
        _selfBonusLevels = bind.Field<int>("selfBonusLevels");
        _queueTimeLeft = bind.Field<BigDouble>("queueTimeLeft");

        // The denominator the game itself divides by: GetActionTime() returns currentBuildTime, not
        // the authored queueTimeTotal.
        _currentBuildTime = bind.Field<BigDouble>("currentBuildTime");
        _flagged = bind.Field<bool>("flagged");

        _power = bind.ModifierRecord("power");
        _powerScaling = bind.ModifierRecord("powerScaling");
        _speed = bind.ModifierRecord("speed");
        _passiveCostMod = bind.ModifierRecord("passiveCostMod");
        _activeCostMod = bind.ModifierRecord("activeCostMod");
        _costScalingMod = bind.ModifierRecord("costScalingMod");
        _attributeRankEffectMod = bind.ModifierRecord("attributeRankEffectMod");
        _drainCostMod = bind.ModifierRecord("drainCostMod");
        _bonusLevels = bind.ModifierRecord("bonusLevels");
        _effectLevels = bind.ModifierRecord("effectLevels");
        _buildSpeed = bind.ModifierRecord("buildSpeed");
        _echoBuildRating = bind.ModifierRecord("echoBuildRating");
        _powerBuildRating = bind.ModifierRecord("powerBuildRating");
        return bind.Failure;
    }

    internal override RawStructureSample Read(object entity) =>
        new(
            _id!(entity),
            _structureTypeId!(entity),
            new BigDouble(_level!(entity)),
            new BigDouble(_queued!(entity)),
            _unlocked!(entity),
            _queuedEchos!(entity),
            _completedEchos!(entity),
            _selfBonusLevels!(entity),
            _queueTimeLeft!(entity),
            _currentBuildTime!(entity),
            _flagged!(entity),
            _baseLevel!(entity),
            _queueTimeTotal!(entity),
            _debugStructure!(entity),
            _disabled!(entity),
            _observableId!(entity),
            _insufficientReqPenalty!(entity),
            _bufferDeveloped!(entity),
            _costPerQuantityId!(entity),
            new RawStructureModifiers(
                _power!(entity),
                _powerScaling!(entity),
                _speed!(entity),
                _passiveCostMod!(entity),
                _activeCostMod!(entity),
                _costScalingMod!(entity),
                _attributeRankEffectMod!(entity),
                _drainCostMod!(entity),
                _bonusLevels!(entity),
                _effectLevels!(entity),
                _buildSpeed!(entity),
                _echoBuildRating!(entity),
                _powerBuildRating!(entity)));
}

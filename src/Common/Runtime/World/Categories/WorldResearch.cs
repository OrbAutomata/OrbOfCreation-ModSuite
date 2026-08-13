using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One research entry as published. Persisted state is read directly; base and effective
/// requirement levels are results returned by the game's own evaluators. The only suite-derived
/// field is their explicitly labeled difference.
/// </summary>
internal readonly struct WorldResearch : IWorldEntity
{
    internal WorldResearch(
        Guid researchId,
        int level,
        int queuedLevels,
        int researchStage,
        int selfBonusLevels,
        int maxLevel,
        double researchTime,
        bool isDeveloping,
        bool isActive,
        bool flagged,
        bool available,
        bool visible,
        bool complete,
        bool canDevelop,
        bool withinDevelopRange,
        bool meetsLevelRequirements,
        bool stillHasLeeway,
        bool belowArtificialMaxLevel,
        bool belowMaxInvestmentLevel,
        int purchasedLevels,
        int baseLevel,
        int bonusLevel,
        int totalLevel,
        int artificialMaxLevel,
        bool hiddenLevel,
        int levelVisibilityRange,
        int requiredStagesCached,
        BigDouble requiredTimeCached,
        int baseRequirementLevel,
        int effectiveRequirementLevel,
        PublicationTable<WorldResearchRequirementAdjustment> requirementAdjustments,
        in RawResearchModifiers modifiers)
    {
        ResearchId = researchId;
        Level = level;
        QueuedLevels = queuedLevels;
        ResearchStage = researchStage;
        SelfBonusLevels = selfBonusLevels;
        MaxLevel = maxLevel;
        ResearchTime = researchTime;
        IsDeveloping = isDeveloping;
        IsActive = isActive;
        Flagged = flagged;
        Available = available;
        Visible = visible;
        Complete = complete;
        CanDevelop = canDevelop;
        WithinDevelopRange = withinDevelopRange;
        MeetsLevelRequirements = meetsLevelRequirements;
        StillHasLeeway = stillHasLeeway;
        BelowArtificialMaxLevel = belowArtificialMaxLevel;
        BelowMaxInvestmentLevel = belowMaxInvestmentLevel;
        PurchasedLevels = purchasedLevels;
        BaseLevel = baseLevel;
        BonusLevel = bonusLevel;
        TotalLevel = totalLevel;
        ArtificialMaxLevel = artificialMaxLevel;
        HiddenLevel = hiddenLevel;
        LevelVisibilityRange = levelVisibilityRange;
        RequiredStagesCached = requiredStagesCached;
        RequiredTimeCached = requiredTimeCached;
        BaseRequirementLevel = baseRequirementLevel;
        EffectiveRequirementLevel = effectiveRequirementLevel;
        RequirementAdjustments = requirementAdjustments;
        Modifiers = modifiers;
        Decision = default;
    }

    internal WorldResearch(
        Guid researchId, int level, int queuedLevels, int researchStage, int selfBonusLevels,
        int maxLevel, double researchTime, bool isDeveloping, bool isActive, bool flagged,
        bool available, bool visible, bool complete, bool canDevelop, bool withinDevelopRange,
        bool meetsLevelRequirements, bool stillHasLeeway, bool belowArtificialMaxLevel,
        bool belowMaxInvestmentLevel, int purchasedLevels, int baseLevel, int bonusLevel,
        int totalLevel, int artificialMaxLevel, bool hiddenLevel, int levelVisibilityRange,
        int requiredStagesCached, BigDouble requiredTimeCached, int baseRequirementLevel,
        int effectiveRequirementLevel,
        PublicationTable<WorldResearchRequirementAdjustment> requirementAdjustments,
        in RawResearchModifiers modifiers,
        in WorldResearchDecision decision)
        : this(researchId, level, queuedLevels, researchStage, selfBonusLevels, maxLevel,
            researchTime, isDeveloping, isActive, flagged, available, visible, complete,
            canDevelop, withinDevelopRange, meetsLevelRequirements, stillHasLeeway,
            belowArtificialMaxLevel, belowMaxInvestmentLevel, purchasedLevels, baseLevel,
            bonusLevel, totalLevel, artificialMaxLevel, hiddenLevel, levelVisibilityRange,
            requiredStagesCached, requiredTimeCached, baseRequirementLevel,
            effectiveRequirementLevel, requirementAdjustments, in modifiers)
    {
        Decision = decision;
    }

    internal Guid ResearchId { get; }

    public Guid EntityId => ResearchId;

    internal int Level { get; }

    /// <summary>
    /// Levels queued, not counting the one in flight. The game's <c>GetQueuedLevels()</c> adds one
    /// when <see cref="IsDeveloping"/>, which is a composition a consumer can make and this row
    /// deliberately does not bake in — the two numbers answer different questions.
    /// </summary>
    internal int QueuedLevels { get; }

    /// <summary>How far through the current level's stages the entry has progressed.</summary>
    internal int ResearchStage { get; }

    internal int SelfBonusLevels { get; }

    /// <summary>The level ceiling before <c>MaxLevelCap</c> is applied.</summary>
    internal int MaxLevel { get; }

    /// <summary>Seconds one level takes at the base rate.</summary>
    internal double ResearchTime { get; }

    /// <summary>Whether a level is currently developing.</summary>
    internal bool IsDeveloping { get; }

    /// <summary>Whether the entry is running rather than paused.</summary>
    internal bool IsActive { get; }

    /// <summary>The player's own marker on this entry.</summary>
    internal bool Flagged { get; }

    /// <summary>Whether prerequisites currently permit developing.</summary>
    internal bool Available { get; }

    /// <summary>Read-only native ResearchSO evaluator results from the same collector generation.</summary>
    internal bool Visible { get; }

    internal bool Complete { get; }

    internal bool CanDevelop { get; }

    internal bool WithinDevelopRange { get; }

    internal bool MeetsLevelRequirements { get; }

    internal bool StillHasLeeway { get; }

    internal bool BelowArtificialMaxLevel { get; }

    internal bool BelowMaxInvestmentLevel { get; }

    /// <summary>The game's distinct level accessors; completion uses BaseLevel, not TotalLevel.</summary>
    internal int PurchasedLevels { get; }

    internal int BaseLevel { get; }

    internal int BonusLevel { get; }

    internal int TotalLevel { get; }

    internal int ArtificialMaxLevel { get; }

    /// <summary>Whether the game hides this research's level, and how far around it levels are shown.</summary>
    internal bool HiddenLevel { get; }

    internal int LevelVisibilityRange { get; }

    /// <summary>
    /// The stage count and time the game last computed for the current level. Cached rather than
    /// persisted, so they are absent from the save record and present here.
    /// </summary>
    internal int RequiredStagesCached { get; }

    internal BigDouble RequiredTimeCached { get; }

    /// <summary>
    /// The level supplied to the native requirement evaluator before and after its exact
    /// <c>GetRequirementLevelMod().Adjust(...)</c> fold. Challenge effects are persistent/passive,
    /// so projecting only <c>activeModifiers.Count</c> hid their adjustment while the native tooltip
    /// and evaluator both applied it.
    /// </summary>
    internal int BaseRequirementLevel { get; }

    internal int EffectiveRequirementLevel { get; }

    internal int RequirementLevelAdjustment => EffectiveRequirementLevel - BaseRequirementLevel;

    /// <summary>
    /// Every direct modifier on this research's <c>requirementsAdjust</c> record, including whether
    /// it is passive and the stable identity/type of its native tooltip source when available.
    /// Research-type adjustments are already reflected in <see cref="EffectiveRequirementLevel"/>
    /// by the native evaluator; their authored records are not direct members of this research and
    /// are deliberately not misattributed here.
    /// </summary>
    internal PublicationTable<WorldResearchRequirementAdjustment> RequirementAdjustments { get; }

    internal RawResearchModifiers Modifiers { get; }

    internal WorldResearchDecision Decision { get; }
}

internal readonly struct WorldResearchDecision
{
    private readonly PublicationTable<WorldResearchCost>? _developmentCosts;
    private readonly PublicationTable<WorldResearchInvestment>? _investment;
    private readonly PublicationTable<WorldResearchTypeDecision>? _researchTypes;

    internal WorldResearchDecision(bool queueMode, int multiBuy, int queuedLevels,
        int levelsAvailable, int currentInvestmentLevel, BigDouble currentTime, BigDouble remainingTime,
        BigDouble timeRatio, bool canApplyBonusLevel, int freeBonusLevels,
        bool developmentCostAffordable, PublicationTable<WorldResearchCost> developmentCosts,
        PublicationTable<WorldResearchInvestment> investment,
        PublicationTable<WorldResearchTypeDecision> researchTypes)
    {
        Available = true;
        UnavailableReason = string.Empty;
        QueueMode = queueMode;
        MultiBuy = Math.Max(multiBuy, 0);
        QueuedLevels = Math.Max(queuedLevels, 0);
        LevelsAvailable = Math.Max(levelsAvailable, 0);
        CurrentInvestmentLevel = Math.Max(currentInvestmentLevel, 0);
        CurrentTime = currentTime;
        RemainingTime = remainingTime;
        TimeRatio = timeRatio;
        CanApplyBonusLevel = canApplyBonusLevel;
        FreeBonusLevels = Math.Max(freeBonusLevels, 0);
        DevelopmentCostAffordable = developmentCostAffordable;
        _developmentCosts = developmentCosts ?? PublicationTable<WorldResearchCost>.Empty;
        _investment = investment ?? PublicationTable<WorldResearchInvestment>.Empty;
        _researchTypes = researchTypes ?? PublicationTable<WorldResearchTypeDecision>.Empty;
    }

    internal bool Available { get; }
    internal string UnavailableReason { get; }
    internal bool QueueMode { get; }
    internal int MultiBuy { get; }
    internal int QueuedLevels { get; }
    internal int LevelsAvailable { get; }
    internal int CurrentInvestmentLevel { get; }
    internal BigDouble CurrentTime { get; }
    internal BigDouble RemainingTime { get; }
    internal BigDouble TimeRatio { get; }
    internal bool CanApplyBonusLevel { get; }
    internal int FreeBonusLevels { get; }
    internal bool DevelopmentCostAffordable { get; }

    /// <summary>
    /// The three tables a decision carries. Each answers empty rather than null on a decision that
    /// was never collected: a research row published through the no-decision constructor leaves
    /// this whole struct at <c>default</c>, which no constructor guard can reach, and a reader
    /// walking the tables then faulted on a row the world had deliberately published without one.
    /// </summary>
    internal PublicationTable<WorldResearchCost> DevelopmentCosts =>
        _developmentCosts ?? PublicationTable<WorldResearchCost>.Empty;

    internal PublicationTable<WorldResearchInvestment> Investment =>
        _investment ?? PublicationTable<WorldResearchInvestment>.Empty;

    internal PublicationTable<WorldResearchTypeDecision> ResearchTypes =>
        _researchTypes ?? PublicationTable<WorldResearchTypeDecision>.Empty;
}

internal readonly struct WorldResearchCost
{
    internal WorldResearchCost(Guid resourceId, BigDouble cost, BigDouble amount)
    { ResourceId = resourceId; Cost = cost; Amount = amount; }
    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
    internal BigDouble Amount { get; }
}

internal readonly struct WorldResearchInvestment
{
    internal WorldResearchInvestment(Guid resourceId, BigDouble invested,
        BigDouble required, BigDouble remaining)
    { ResourceId = resourceId; Invested = invested; Required = required; Remaining = remaining; }
    internal Guid ResourceId { get; }
    internal BigDouble Invested { get; }
    internal BigDouble Required { get; }
    internal BigDouble Remaining { get; }
}

internal readonly struct WorldResearchTypeDecision
{
    internal WorldResearchTypeDecision(Guid researchTypeId, int remainingBonusLevels,
        int currentInvestmentLevel, int maximumInvestmentLevel)
    {
        ResearchTypeId = researchTypeId; RemainingBonusLevels = remainingBonusLevels;
        CurrentInvestmentLevel = currentInvestmentLevel;
        MaximumInvestmentLevel = maximumInvestmentLevel;
    }
    internal Guid ResearchTypeId { get; }
    internal int RemainingBonusLevels { get; }
    internal int CurrentInvestmentLevel { get; }
    internal int MaximumInvestmentLevel { get; }
}

/// <summary>A research entry's cached modifier records.</summary>
internal readonly struct RawResearchModifiers
{
    internal RawResearchModifiers(
        BigDouble bonusLevels,
        BigDouble baseLevels,
        BigDouble power,
        BigDouble maxLevelCap,
        BigDouble leewayPoints)
    {
        BonusLevels = bonusLevels;
        BaseLevels = baseLevels;
        Power = power;
        MaxLevelCap = maxLevelCap;
        LeewayPoints = leewayPoints;
    }

    /// <summary>Levels granted from elsewhere, and levels the entry starts with.</summary>
    internal BigDouble BonusLevels { get; }

    internal BigDouble BaseLevels { get; }

    /// <summary>Percent scaling on what one level does.</summary>
    internal BigDouble Power { get; }

    /// <summary>How far the level ceiling has been raised, and how much requirement slack is allowed.</summary>
    internal BigDouble MaxLevelCap { get; }

    internal BigDouble LeewayPoints { get; }
}

/// <summary>
/// Research entries. Read-only native evaluators supply visibility, completion, development gates,
/// distinct level values, and requirement levels from one main-thread collection generation; the
/// collector does not reimplement those verdicts.
/// </summary>
/// <remarks>
/// Mutable progress fields remain plain reads. The predicate and accessor calls are side-effect-free
/// queries over that same native state, and every reflected member is pinned by the installed-game
/// contract manifest.
/// </remarks>
internal sealed class WorldResearchBinder : WorldPlainBinder<WorldResearch>
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Func<string, Type?> _resolveType;
    private Func<object, Guid>? _id;
    private Func<object, int>? _level;
    private Func<object, int>? _queuedLevels;
    private Func<object, int>? _researchStage;
    private Func<object, int>? _selfBonusLevels;
    private Func<object, int>? _maxLevel;
    private Func<object, double>? _researchTime;
    private Func<object, bool>? _isDeveloping;
    private Func<object, bool>? _isActive;
    private Func<object, bool>? _flagged;
    private Func<object, bool>? _available;
    private Func<object, bool>? _visible;
    private Func<object, bool>? _complete;
    private Func<object, bool>? _canDevelop;
    private Func<object, bool>? _withinDevelopRange;
    private Func<object, bool>? _meetsLevelRequirements;
    private Func<object, bool>? _stillHasLeeway;
    private Func<object, bool>? _belowArtificialMaxLevel;
    private Func<object, bool>? _belowMaxInvestmentLevel;
    private Func<object, int>? _purchasedLevels;
    private Func<object, int>? _baseLevel;
    private Func<object, int>? _bonusLevel;
    private Func<object, int>? _totalLevel;
    private Func<object, int>? _artificialMaxLevel;
    private Func<object, bool>? _hiddenLevel;
    private Func<object, int>? _levelVisibilityRange;
    private Func<object, int>? _requiredStagesCached;
    private Func<object, BigDouble>? _requiredTimeCached;
    private Func<object, int>? _baseRequirementLevel;
    private Func<object, int>? _effectiveRequirementLevel;
    private NativeModifierAdjustmentAccess? _requirementAdjustments;
    private string _requirementAdjustmentFailure = string.Empty;
    private Func<object, BigDouble>? _bonusLevels;
    private Func<object, BigDouble>? _baseLevels;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _maxLevelCap;
    private Func<object, BigDouble>? _leewayPoints;
    private Func<bool>? _queueMode;
    private Func<object?>? _multiBuy;
    private Func<object, int>? _asInt;
    private Func<object, int>? _queuedIncludingActive;
    private Func<object, int>? _currentInvestmentLevel;
    private Func<object, BigDouble>? _currentTime;
    private Func<object, BigDouble>? _remainingTime;
    private Func<object, BigDouble>? _timeRatio;
    private Func<object, bool>? _canApplyBonusLevel;
    private Func<object, int>? _freeBonusLevels;
    private Func<object, object?>? _developmentCost;
    private Func<object, int, object?>? _developmentCostAtLevel;
    private Func<object, int, bool>? _withinDevelopRangeAt;
    private Func<object, bool>? _hasMaxLevel;
    private Func<object, bool>? _costAffordable;
    private Func<object, object, object?>? _addCost;
    private Func<object, IList?>? _costEntries;
    private Func<object, object?>? _costResource;
    private Func<object, BigDouble>? _costValue;
    private Func<object, Guid>? _resourceId;
    private Func<object, BigDouble>? _resourceAmount;
    private Func<object, object?>? _fillList;
    private Func<object, IList?>? _fillEntries;
    private Func<object, object?>? _fillResource;
    private Func<object, BigDouble>? _fillQuantity;
    private Func<object, BigDouble>? _fillCapacity;
    private Func<object, BigDouble>? _fillRemaining;
    private Func<object, IList?>? _researchTypes;
    private Func<object, Guid>? _researchTypeId;
    private Func<object, int>? _remainingBonusLevels;
    private Func<object, int>? _typeCurrentInvestment;
    private Func<object, int>? _typeMaximumInvestment;

    internal WorldResearchBinder(Func<string, Type?>? resolveType = null) =>
        _resolveType = resolveType ?? OrbModding.Common.ReflectionUtil.FindLoadedType;

    internal override string Category => "research";

    internal override string TypeName => "ResearchSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _level = bind.Field<int>("level");
        _queuedLevels = bind.Field<int>("queuedLevels");
        _researchStage = bind.Field<int>("researchStage");
        _selfBonusLevels = bind.Field<int>("selfBonusLevels");
        _maxLevel = bind.Field<int>("maxLevel");
        _researchTime = bind.Field<double>("researchTime");
        _isDeveloping = bind.Field<bool>("isDeveloping");
        _isActive = bind.Field<bool>("isActive");
        _flagged = bind.Field<bool>("flagged");
        _available = bind.Call<bool>("IsAvailable");
        _visible = bind.Call<bool>("IsVisible");
        _complete = bind.Call<bool>("IsComplete");
        _canDevelop = bind.Call<bool>("CanDevelop");
        _withinDevelopRange = bind.Call<bool>("IsWithinDevelopRange");
        _meetsLevelRequirements = bind.Call<bool>("MeetsLevelRequirements");
        _stillHasLeeway = bind.Call<bool>("StillHasLeeway");
        _belowArtificialMaxLevel = bind.Call<bool>("IsBelowArtificialMaxLevel");
        _belowMaxInvestmentLevel = bind.Call<bool>("IsBelowMaxInvestmentLevel");
        _purchasedLevels = bind.Call<int>("GetPurchasedLevels");
        _baseLevel = bind.Call<int>("GetBaseLevel");
        _bonusLevel = bind.Call<int>("GetBonusLevels");
        _totalLevel = bind.Call<int>("GetLevel");
        _artificialMaxLevel = bind.Call<int>("GetArtificialMaxLevel");
        _hiddenLevel = bind.Field<bool>("hiddenLevel");
        _levelVisibilityRange = bind.Field<int>("levelVisibilityRange");
        _requiredStagesCached = bind.Field<int>("requiredStagesCached");
        _requiredTimeCached = bind.Field<BigDouble>("requiredTimeCached");

        // Both are native, read-only evaluators. GetRequirementLevel folds research-type records and
        // this research's passive and active requirementsAdjust sets exactly as the tooltip does.
        _baseRequirementLevel = bind.Call<int>("GetBaseLevel");
        _effectiveRequirementLevel = bind.Call<int>("GetRequirementLevel");
        _requirementAdjustments = NativeModifierAdjustmentAccess.Bind(
            type,
            "requirementsAdjust",
            out _requirementAdjustmentFailure);
        _bonusLevels = bind.ModifierRecord("bonusLevels");
        _baseLevels = bind.ModifierRecord("baseLevels");
        _power = bind.ModifierRecord("power");
        _maxLevelCap = bind.ModifierRecord("maxLevelCap");
        _leewayPoints = bind.ModifierRecord("leewayPoints");
        var settings = _resolveType("SettingsManager");
        var globals = _resolveType("GlobalVariables");
        var integer = _resolveType("IntVariable");
        var costType = _resolveType("ResourceCostList");
        var tupleType = _resolveType("ResourceTuple");
        var resourceType = _resolveType("ResourceSO");
        var fillType = _resolveType("ResourceFillList");
        var fillEntryType = _resolveType("ResourceFillList+ResourceFillEntry");
        var researchType = _resolveType("ResearchTypeSO");
        _queueMode = StaticCall<bool>(settings, "IsResearchQueueMode");
        _multiBuy = StaticObjectCall(globals, "GetMultiBuy", integer);
        _asInt = NativeAccessorBinder.Call<int>(integer, "AsInt");
        _queuedIncludingActive = bind.Call<int>("GetQueuedLevels");
        _currentInvestmentLevel = bind.Call<int>("GetCurrentInvestmentLevel");
        _currentTime = bind.Call<BigDouble>("GetCurrentTime");
        _remainingTime = bind.Call<BigDouble>("GetRemainingTime");
        _timeRatio = bind.Call<BigDouble>("GetTimeRatio");
        _canApplyBonusLevel = bind.Call<bool>("CanApplyBonusLevels");
        _freeBonusLevels = bind.Call<int>("GetFreeBonusLevelsLeft");
        _developmentCost = bind.CallObject("GetDevelopmentCost", costType);
        _developmentCostAtLevel = InstanceIntObjectCall(type, "GetDevelopmentCostAtLevel", costType);
        _withinDevelopRangeAt = InstanceIntCall<bool>(type, "IsWithinDevelopRangeAt");
        _hasMaxLevel = bind.Call<bool>("HasMaxLevel");
        _costAffordable = NativeAccessorBinder.Call<bool>(costType, "HasEnough");
        _addCost = InstanceObjectCall(costType, "Add", costType, costType);
        _costEntries = NativeAccessorBinder.CallList(costType, "GetEntries", tupleType);
        _costResource = NativeAccessorBinder.Reference(tupleType, "resource", resourceType);
        _costValue = NativeAccessorBinder.Call<BigDouble>(tupleType, "GetValue");
        _resourceId = NativeAccessorBinder.Call<Guid>(resourceType, "GetGuid");
        _resourceAmount = NativeAccessorBinder.Call<BigDouble>(resourceType, "GetQuantity");
        _fillList = bind.Reference("resourceFillList", fillType);
        _fillEntries = NativeAccessorBinder.CollectionField(fillType, "entries");
        _fillResource = NativeAccessorBinder.CallObject(fillEntryType, "get_resource", resourceType);
        _fillQuantity = NativeAccessorBinder.Call<BigDouble>(fillEntryType, "GetQuantity");
        _fillCapacity = NativeAccessorBinder.Call<BigDouble>(fillEntryType, "GetCapacity");
        _fillRemaining = NativeAccessorBinder.Call<BigDouble>(fillEntryType, "GetRemaining");
        _researchTypes = NativeAccessorBinder.CollectionField(type, "researchTypes");
        _researchTypeId = NativeAccessorBinder.Call<Guid>(researchType, "GetGuid");
        _remainingBonusLevels = NativeAccessorBinder.Call<int>(researchType, "GetRemainingFreeBonusLevels");
        _typeCurrentInvestment = NativeAccessorBinder.Call<int>(researchType, "GetCurrentInvestmentLevel");
        _typeMaximumInvestment = NativeAccessorBinder.Call<int>(researchType, "GetMaxInvestmentLevel");
        var decisionFailure = _queueMode is null || _multiBuy is null || _asInt is null ||
            _queuedIncludingActive is null || _currentInvestmentLevel is null ||
            _currentTime is null || _remainingTime is null || _timeRatio is null ||
            _canApplyBonusLevel is null || _freeBonusLevels is null || _developmentCost is null ||
            _developmentCostAtLevel is null || _withinDevelopRangeAt is null ||
            _hasMaxLevel is null || _costAffordable is null || _addCost is null ||
            _costEntries is null || _costResource is null ||
            _costValue is null || _resourceId is null || _resourceAmount is null ||
            _fillList is null || _fillEntries is null || _fillResource is null ||
            _fillQuantity is null || _fillCapacity is null || _fillRemaining is null ||
            _researchTypes is null || _researchTypeId is null || _remainingBonusLevels is null ||
            _typeCurrentInvestment is null || _typeMaximumInvestment is null
                ? TypeName + " did not expose the complete research decision binding set"
                : string.Empty;
        var baseFailure = bind.Failure.Length == 0
            ? decisionFailure
            : decisionFailure.Length == 0 ? bind.Failure : bind.Failure + "; " + decisionFailure;
        if (_requirementAdjustmentFailure.Length == 0) return baseFailure;
        var adjustmentFailure = TypeName + " did not expose " +
            _requirementAdjustmentFailure + " on this build";
        return baseFailure.Length == 0
            ? adjustmentFailure
            : baseFailure + "; " + adjustmentFailure;
    }

    internal override WorldResearch Read(object entity)
    {
        var decision = ReadDecision(entity);
        var modifiers = new RawResearchModifiers(
            _bonusLevels!(entity), _baseLevels!(entity), _power!(entity),
            _maxLevelCap!(entity), _leewayPoints!(entity));
        return new WorldResearch(
            _id!(entity),
            _level!(entity),
            _queuedLevels!(entity),
            _researchStage!(entity),
            _selfBonusLevels!(entity),
            _maxLevel!(entity),
            _researchTime!(entity),
            _isDeveloping!(entity),
            _isActive!(entity),
            _flagged!(entity),
            _available!(entity),
            _visible!(entity),
            _complete!(entity),
            _canDevelop!(entity),
            _withinDevelopRange!(entity),
            _meetsLevelRequirements!(entity),
            _stillHasLeeway!(entity),
            _belowArtificialMaxLevel!(entity),
            _belowMaxInvestmentLevel!(entity),
            _purchasedLevels!(entity),
            _baseLevel!(entity),
            _bonusLevel!(entity),
            _totalLevel!(entity),
            _artificialMaxLevel!(entity),
            _hiddenLevel!(entity),
            _levelVisibilityRange!(entity),
            _requiredStagesCached!(entity),
            _requiredTimeCached!(entity),
            _baseRequirementLevel!(entity),
            _effectiveRequirementLevel!(entity),
            _requirementAdjustments!.Read(entity),
            in modifiers,
            in decision);
    }

    private WorldResearchDecision ReadDecision(object entity)
    {
        var multiBuy = _multiBuy!() ??
            throw new InvalidOperationException("GlobalVariables.GetMultiBuy returned null");
        var queueMode = _queueMode!();
        var multiBuyValue = _asInt!(multiBuy);
        var plan = queueMode
            ? ReadQueuePlan(entity, multiBuyValue)
            : ReadImmediatePlan(entity);
        var fill = _fillList!(entity) ??
            throw new InvalidOperationException("ResearchSO.resourceFillList was null");
        return new WorldResearchDecision(
            queueMode, multiBuyValue, _queuedIncludingActive!(entity), plan.Levels,
            _currentInvestmentLevel!(entity), _currentTime!(entity),
            _remainingTime!(entity), _timeRatio!(entity), _canApplyBonusLevel!(entity),
            _freeBonusLevels!(entity), plan.Affordable, ReadCosts(plan.Cost),
            ReadInvestment(fill), ReadResearchTypes(entity));
    }

    private (object Cost, int Levels, bool Affordable) ReadImmediatePlan(object entity)
    {
        var cost = _developmentCost!(entity) ??
            throw new InvalidOperationException("ResearchSO.GetDevelopmentCost returned null");
        var affordable = _costAffordable!(cost);
        return (cost, _canDevelop!(entity) && affordable ? 1 : 0, affordable);
    }

    private (object Cost, int Levels, bool Affordable) ReadQueuePlan(object entity, int multiBuy)
    {
        var currentQueued = _queuedIncludingActive!(entity);
        var limit = Math.Max(multiBuy, 0);
        if (_hasMaxLevel!(entity))
            limit = Math.Min(limit, Math.Max(_maxLevel!(entity) - currentQueued - _level!(entity), 0));
        object? aggregate = null;
        var levels = 0;
        for (var index = 0; index < limit; index++)
        {
            var atLevel = checked(_level!(entity) + currentQueued + index);
            var next = _developmentCostAtLevel!(entity, checked(atLevel + 1)) ??
                throw new InvalidOperationException("ResearchSO.GetDevelopmentCostAtLevel returned null");
            aggregate = aggregate is null ? next : _addCost!(aggregate, next) ??
                throw new InvalidOperationException("ResourceCostList.Add returned null");
            if (!_costAffordable!(aggregate) || !_withinDevelopRangeAt!(entity, atLevel)) break;
            levels++;
        }
        if (levels == 0)
        {
            aggregate ??= _developmentCost!(entity) ??
                throw new InvalidOperationException("ResearchSO.GetDevelopmentCost returned null");
            return (aggregate, 0, false);
        }

        // QueueDevelopment mutates its cumulative list before checking affordability, so the list
        // left after a failed candidate includes a level the native route will not queue. Rebuild
        // only the accepted prefix for the decision projection while preserving the same native
        // GetDevelopmentCostAtLevel + ResourceCostList.Add lineage.
        object? accepted = null;
        for (var index = 0; index < levels; index++)
        {
            var atLevel = checked(_level!(entity) + currentQueued + index);
            var next = _developmentCostAtLevel!(entity, checked(atLevel + 1)) ??
                throw new InvalidOperationException("ResearchSO.GetDevelopmentCostAtLevel returned null");
            accepted = accepted is null ? next : _addCost!(accepted, next) ??
                throw new InvalidOperationException("ResourceCostList.Add returned null");
        }
        return (accepted!, levels, true);
    }

    private PublicationTable<WorldResearchCost> ReadCosts(object cost)
    {
        var entries = _costEntries!(cost) ??
            throw new InvalidOperationException("Research development cost entries were null");
        var rows = new WorldResearchCost[entries.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Research development cost contained null");
            var resource = _costResource!(entry) ??
                throw new InvalidOperationException("Research development cost had no resource");
            rows[index] = new WorldResearchCost(
                _resourceId!(resource), _costValue!(entry), _resourceAmount!(resource));
        }
        return PublicationTable<WorldResearchCost>.Create(rows);
    }

    private PublicationTable<WorldResearchInvestment> ReadInvestment(object fill)
    {
        var entries = _fillEntries!(fill) ??
            throw new InvalidOperationException("Research investment entries were null");
        var rows = new WorldResearchInvestment[entries.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Research investment contained null");
            var resource = _fillResource!(entry) ??
                throw new InvalidOperationException("Research investment had no resource");
            rows[index] = new WorldResearchInvestment(_resourceId!(resource),
                _fillQuantity!(entry), _fillCapacity!(entry), _fillRemaining!(entry));
        }
        return PublicationTable<WorldResearchInvestment>.Create(rows);
    }

    private PublicationTable<WorldResearchTypeDecision> ReadResearchTypes(object entity)
    {
        var entries = _researchTypes!(entity) ??
            throw new InvalidOperationException("ResearchSO.researchTypes was null");
        var rows = new WorldResearchTypeDecision[entries.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("ResearchSO.researchTypes contained null");
            rows[index] = new WorldResearchTypeDecision(_researchTypeId!(entry),
                _remainingBonusLevels!(entry), _typeCurrentInvestment!(entry),
                _typeMaximumInvestment!(entry));
        }
        return PublicationTable<WorldResearchTypeDecision>.Create(rows);
    }

    private static Func<TResult>? StaticCall<TResult>(Type? owner, string name)
    {
        var method = owner?.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || method.ReturnType != typeof(TResult)) return null;
        try { return Expression.Lambda<Func<TResult>>(Expression.Call(method)).Compile(); }
        catch (Exception) { return null; }
    }

    private static Func<object?>? StaticObjectCall(Type? owner, string name, Type? result)
    {
        var method = owner?.GetMethod(name, Static, null, Type.EmptyTypes, null);
        if (method is null || !method.IsStatic || result is null || method.ReturnType != result) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Call(method), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, int, TResult>? InstanceIntCall<TResult>(Type? owner, string name)
    {
        var method = owner?.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
        if (method is null || method.IsStatic || method.ReturnType != typeof(TResult)) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(int), "value");
            return Expression.Lambda<Func<object, int, TResult>>(
                Expression.Call(Expression.Convert(target, owner!), method, value), target, value).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, int, object?>? InstanceIntObjectCall(
        Type? owner, string name, Type? result)
    {
        if (result is null) return null;
        var method = owner?.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic, null, new[] { typeof(int) }, null);
        if (method is null || method.IsStatic || method.ReturnType != result) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(int), "value");
            return Expression.Lambda<Func<object, int, object?>>(
                Expression.Convert(Expression.Call(Expression.Convert(target, owner!), method, value), typeof(object)),
                target, value).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, object, object?>? InstanceObjectCall(
        Type? owner, string name, Type? parameter, Type? result)
    {
        if (owner is null || parameter is null || result is null) return null;
        var method = owner.GetMethod(name, BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.NonPublic, null, new[] { parameter }, null);
        if (method is null || method.IsStatic || method.ReturnType != result) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var value = Expression.Parameter(typeof(object), "value");
            return Expression.Lambda<Func<object, object, object?>>(
                Expression.Convert(Expression.Call(Expression.Convert(target, owner), method,
                    Expression.Convert(value, parameter)), typeof(object)), target, value).Compile();
        }
        catch (Exception) { return null; }
    }
}

/// <summary>One modifier contributing directly to a research requirement-level adjustment.</summary>
internal readonly struct WorldResearchRequirementAdjustment
{
    internal WorldResearchRequirementAdjustment(
        Guid modifierId,
        Guid sourceId,
        string sourceNativeType,
        int modifierType,
        BigDouble amount,
        int order,
        bool passive)
    {
        ModifierId = modifierId;
        SourceId = sourceId;
        SourceNativeType = sourceNativeType ?? string.Empty;
        ModifierType = modifierType;
        Amount = amount;
        Order = order;
        Passive = passive;
    }

    internal Guid ModifierId { get; }

    internal Guid SourceId { get; }

    internal string SourceNativeType { get; }

    internal int ModifierType { get; }

    internal BigDouble Amount { get; }

    internal int Order { get; }

    internal bool Passive { get; }
}

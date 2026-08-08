using System;
using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// One ritual as published. The rolled effects themselves (<c>ritualInstances</c>) are variable-size
/// and stay behind; how many effects are running does not, because that is the game's own definition
/// of a ritual being active. The spoils the results screen lists are carried, because a run that
/// ended has no other record of what it won.
/// </summary>
internal readonly struct WorldRitual : IWorldEntity
{
    internal WorldRitual(
        Guid ritualId,
        bool discovered,
        bool inBattle,
        int activeInstances,
        int reachedLevel,
        int lastReachedLevel,
        int selectedLevel,
        int wavesCompleted,
        int discoveryRarityLevel,
        int critLevel,
        int echoLevel,
        int chainLevel,
        int durationRewardBlocks,
        BigDouble battleTotalWeight,
        in RawRitualModifiers modifiers,
        bool hideEndScreenResults,
        bool isDiscoverRequired,
        bool forceLevel,
        int forceLevelValue,
        int baseWaves,
        int maxWaves,
        int requiredWaves,
        double baseWeight,
        int minimumEffectLevel,
        bool failedRun,
        WorldRitualSpoil[]? spoils = null,
        WorldDiscoverableDecision discovery = default,
        WorldRitualDecision decision = default,
        WorldRitualCompletionFormula completionFormula = default)
    {
        RitualId = ritualId;
        Discovered = discovered;
        InBattle = inBattle;
        ActiveInstances = activeInstances;
        ReachedLevel = reachedLevel;
        LastReachedLevel = lastReachedLevel;
        SelectedLevel = selectedLevel;
        WavesCompleted = wavesCompleted;
        DiscoveryRarityLevel = discoveryRarityLevel;
        CritLevel = critLevel;
        EchoLevel = echoLevel;
        ChainLevel = chainLevel;
        DurationRewardBlocks = durationRewardBlocks;
        BattleTotalWeight = battleTotalWeight;
        Modifiers = modifiers;
        HideEndScreenResults = hideEndScreenResults;
        IsDiscoverRequired = isDiscoverRequired;
        ForceLevel = forceLevel;
        ForceLevelValue = forceLevelValue;
        BaseWaves = baseWaves;
        MaxWaves = maxWaves;
        RequiredWaves = requiredWaves;
        BaseWeight = baseWeight;
        MinimumEffectLevel = minimumEffectLevel;
        FailedRun = failedRun;
        Spoils = spoils is null || spoils.Length == 0
            ? PublicationTable<WorldRitualSpoil>.Empty
            : PublicationTable<WorldRitualSpoil>.Create(spoils);
        Discovery = discovery;
        Decision = decision;
        CompletionFormula = completionFormula;
    }

    internal Guid RitualId { get; }

    public Guid EntityId => RitualId;

    /// <summary>
    /// Whether the ritual is unlocked. This is the whole of it: the game's <c>IsAvailable()</c> and
    /// <c>IsVisible()</c> both forward to <c>IsDiscovered()</c>, which returns this field.
    /// </summary>
    internal bool Discovered { get; }

    /// <summary>Whether a run is in progress right now.</summary>
    internal bool InBattle { get; }

    /// <summary>
    /// How many rolled effects are currently running. The game's <c>HasActiveInstances()</c> and
    /// <c>IsDurationActive()</c> are both <c>ritualInstances.Count &gt; 0</c>, so this is what "the
    /// ritual is active" means for a completed run whose reward is still ticking — a different
    /// question from <see cref="InBattle"/>, and the one a re-run decision turns on.
    /// </summary>
    internal int ActiveInstances { get; }

    /// <summary>The best level ever reached, the level reached in the last run, and the level chosen.</summary>
    internal int ReachedLevel { get; }

    internal int LastReachedLevel { get; }

    internal int SelectedLevel { get; }

    /// <summary>
    /// Waves cleared by the run in progress, or — once <see cref="InBattle"/> is false — by the run
    /// that last finished. <c>Initiate()</c> is the only thing that clears it, so the finished run's
    /// count stands until the next activation.
    /// </summary>
    internal int WavesCompleted { get; }

    internal int DiscoveryRarityLevel { get; }

    /// <summary>The tiers the run in progress has rolled up to.</summary>
    internal int CritLevel { get; }

    internal int EchoLevel { get; }

    internal int ChainLevel { get; }

    /// <summary>
    /// How many duration rewards the ritual defines. <c>IsDurationRitual()</c> is
    /// <c>durationRewardBlocks.Count &gt; 0</c>, so this is what says whether
    /// <see cref="ActiveInstances"/> is a question worth asking about this ritual at all.
    /// </summary>
    internal int DurationRewardBlocks { get; }

    /// <summary>
    /// The total enemy weight of the run in progress — the denominator the game divides destroyed
    /// weight by to advance completion.
    /// </summary>
    internal BigDouble BattleTotalWeight { get; }

    internal RawRitualModifiers Modifiers { get; }

    /// <summary>
    /// The rest of the ritual's own numbers: the wave and level bounds a run is fought within, and
    /// whether the level is forced rather than chosen.
    /// </summary>
    internal bool HideEndScreenResults { get; }

    internal bool IsDiscoverRequired { get; }

    internal bool ForceLevel { get; }

    internal int ForceLevelValue { get; }

    internal int BaseWaves { get; }

    internal int MaxWaves { get; }

    /// <summary>
    /// How many waves a run at the currently staged level has to clear: <c>GetRequiredWaves()</c>,
    /// which adjusts <see cref="BaseWaves"/> by the authored <c>wavesPerLevel</c> modifier scaled to
    /// <c>GetCurrentLevel()</c> and clamps the result to <see cref="MaxWaves"/> when that is
    /// positive. Captured rather than derived: <c>wavesPerLevel</c> is a <c>ValueModifier</c> the
    /// suite does not reimplement.
    /// </summary>
    internal int RequiredWaves { get; }

    internal double BaseWeight { get; }

    internal int MinimumEffectLevel { get; }

    /// <summary>
    /// The game's own verdict on the run that just ended: <c>RitualSO.IsFailedRun()</c>, which
    /// <c>End()</c> consults to decide between the failure popup and the rewards, and which is
    /// <c>wavesCompleted &lt; 5</c> rather than anything derived from the ritual's own wave count.
    /// </summary>
    internal bool FailedRun { get; }

    /// <summary>What the run banked, exactly as the results screen lists it.</summary>
    internal PublicationTable<WorldRitualSpoil> Spoils { get; }

    internal WorldDiscoverableDecision Discovery { get; }

    /// <summary>The currently staged player decision; prices exist only for the selected row.</summary>
    internal WorldRitualDecision Decision { get; }

    /// <summary>Raw authored inputs for the screen's selected completion-cost formula.</summary>
    internal WorldRitualCompletionFormula CompletionFormula { get; }

    internal WorldRitual WithDecision(in WorldRitualDecision decision)
    {
        var modifiers = Modifiers;
        var spoils = new WorldRitualSpoil[Spoils.Count];
        for (var index = 0; index < spoils.Length; index++) spoils[index] = Spoils[index];
        return new(
            RitualId, Discovered, InBattle, ActiveInstances, ReachedLevel, LastReachedLevel,
            SelectedLevel, WavesCompleted, DiscoveryRarityLevel, CritLevel, EchoLevel, ChainLevel,
            DurationRewardBlocks, BattleTotalWeight, in modifiers, HideEndScreenResults,
            IsDiscoverRequired, ForceLevel, ForceLevelValue, BaseWaves, MaxWaves, RequiredWaves,
            BaseWeight, MinimumEffectLevel, FailedRun, spoils, Discovery, decision,
            CompletionFormula);
    }
}

/// <summary>One resource line of a ritual run's spoils, as the results screen lists it.</summary>
internal readonly struct WorldRitualSpoil
{
    internal WorldRitualSpoil(Guid resourceId, BigDouble quantity)
    {
        ResourceId = resourceId;
        Quantity = quantity;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Quantity { get; }
}

internal readonly struct WorldRitualCost
{
    internal WorldRitualCost(Guid resourceId, BigDouble cost)
    {
        ResourceId = resourceId;
        Cost = cost;
    }

    internal Guid ResourceId { get; }
    internal BigDouble Cost { get; }
}

internal readonly struct WorldRitualDecision
{
    /// <summary>
    /// The floor the ritual's own starting-level control clamps to.
    /// <c>UIRitual.RenderContent()</c> calls
    /// <c>UIValueSelectButton.SetClamp(1, RitualSO.GetMaxSelectedLevel())</c>, and the control's
    /// decrement is <c>Math.Max(value - change, minValue)</c>, so 1 is the lowest starting level
    /// the game offers. The data-layer setter <c>RitualSO.ChangeStartingLevel</c> clamps to
    /// <c>Math.Min(max, Math.Max(value, 0))</c> and therefore accepts 0 — which is why this bound
    /// has to come from the control the player presses, not from the setter behind it. Nothing
    /// here reflects on that control, so the dependency is declared as the mirrored contracts
    /// <c>ui.value-select-button.clamp</c> and <c>ui.value-select-button.minimum</c>.
    /// </summary>
    internal const int NativeMinimumStartingLevel = 1;

    private readonly PublicationTable<WorldRitualCost>? _activationCosts;
    private readonly PublicationTable<WorldRitualCost>? _completionCosts;

    internal WorldRitualDecision(
        bool selected,
        int maximumStartingLevel,
        bool usageRequirementsMet,
        bool activationAffordable,
        PublicationTable<WorldRitualCost> activationCosts,
        PublicationTable<WorldRitualCost> completionCosts)
    {
        Selected = selected;
        MaximumStartingLevel = Math.Max(maximumStartingLevel, NativeMinimumStartingLevel);
        UsageRequirementsMet = usageRequirementsMet;
        ActivationAffordable = activationAffordable;
        _activationCosts = activationCosts ?? throw new ArgumentNullException(nameof(activationCosts));
        _completionCosts = completionCosts ?? throw new ArgumentNullException(nameof(completionCosts));
    }

    internal bool Selected { get; }
    internal int MaximumStartingLevel { get; }
    internal bool UsageRequirementsMet { get; }
    internal bool ActivationAffordable { get; }
    internal PublicationTable<WorldRitualCost> ActivationCosts =>
        _activationCosts ?? PublicationTable<WorldRitualCost>.Empty;
    internal PublicationTable<WorldRitualCost> CompletionCosts =>
        _completionCosts ?? PublicationTable<WorldRitualCost>.Empty;

    internal WorldRitualDecision WithCompletionCosts(
        PublicationTable<WorldRitualCost> completionCosts) =>
        new(
            Selected, MaximumStartingLevel, UsageRequirementsMet, ActivationAffordable,
            ActivationCosts, completionCosts);
}

internal readonly struct WorldRitualCompletionFormula
{
    private readonly PublicationTable<WorldRitualCost>? _baseCosts;
    private readonly PublicationTable<GameValueModifier>? _modifiers;
    private readonly PublicationTable<GameValueModifier>? _exponents;

    internal WorldRitualCompletionFormula(
        PublicationTable<WorldRitualCost> baseCosts,
        PublicationTable<GameValueModifier> modifiers,
        PublicationTable<GameValueModifier> exponents)
    {
        _baseCosts = baseCosts ?? throw new ArgumentNullException(nameof(baseCosts));
        _modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
        _exponents = exponents ?? throw new ArgumentNullException(nameof(exponents));
    }

    internal PublicationTable<WorldRitualCost> BaseCosts =>
        _baseCosts ?? PublicationTable<WorldRitualCost>.Empty;
    internal PublicationTable<GameValueModifier> Modifiers =>
        _modifiers ?? PublicationTable<GameValueModifier>.Empty;
    internal PublicationTable<GameValueModifier> Exponents =>
        _exponents ?? PublicationTable<GameValueModifier>.Empty;
}

internal static class WorldRitualDeriver
{
    internal static PublicationTable<WorldRitual> Build(
        WorldSampleBuffer<WorldRitual, WorldRitual> buffer)
    {
        var rows = new WorldRitual[buffer.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            ref readonly var source = ref buffer[index];
            if (!source.Decision.Selected)
            {
                rows[index] = source;
                continue;
            }

            var completion = ComputeCompletionCosts(in source);
            var decision = source.Decision.WithCompletionCosts(completion);
            rows[index] = source.WithDecision(in decision);
        }
        return WorldTable.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldRitualCost> ComputeCompletionCosts(in WorldRitual ritual)
    {
        var formula = ritual.CompletionFormula;
        var baseCosts = formula.BaseCosts;
        if (baseCosts.Count == 0) return PublicationTable<WorldRitualCost>.Empty;

        var modifiers = new GameValueModifier[formula.Modifiers.Count];
        for (var index = 0; index < modifiers.Length; index++)
            modifiers[index] = formula.Modifiers[index].MultiplyScalar(new BigDouble(ritual.SelectedLevel));
        var exponents = new GameValueModifier[formula.Exponents.Count];
        for (var index = 0; index < exponents.Length; index++)
            exponents[index] = formula.Exponents[index].MultiplyScalar(new BigDouble(ritual.SelectedLevel));
        var scratch = new GameValueModifier[modifiers.Length];
        var completionMod = OrbGameMath.AsPercent(ritual.Modifiers.CompletionCostMod);
        var rows = new WorldRitualCost[baseCosts.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var row = baseCosts[index];
            var adjusted = GameModifierStack.AdjustWith(
                row.Cost, modifiers, exponents, scratch) * completionMod;
            rows[index] = new WorldRitualCost(row.ResourceId, adjusted);
        }
        return PublicationTable<WorldRitualCost>.Create(rows, rows.Length);
    }
}

/// <summary>A ritual's cached modifier records — what a run at the chosen level is worth.</summary>
internal readonly struct RawRitualModifiers
{
    internal RawRitualModifiers(
        BigDouble power,
        BigDouble speed,
        BigDouble special,
        BigDouble durationMod,
        BigDouble echoRating,
        BigDouble echoPower,
        BigDouble critRating,
        BigDouble critPower,
        BigDouble critDurationMod,
        BigDouble chainLengthBonus,
        BigDouble chainPower,
        BigDouble completionCostMod,
        BigDouble completionRateMod)
    {
        Power = power;
        Speed = speed;
        Special = special;
        DurationMod = durationMod;
        EchoRating = echoRating;
        EchoPower = echoPower;
        CritRating = critRating;
        CritPower = critPower;
        CritDurationMod = critDurationMod;
        ChainLengthBonus = chainLengthBonus;
        ChainPower = chainPower;
        CompletionCostMod = completionCostMod;
        CompletionRateMod = completionRateMod;
    }

    internal BigDouble Power { get; }

    internal BigDouble Speed { get; }

    internal BigDouble Special { get; }

    /// <summary>How long a rolled effect lasts, before and after a crit.</summary>
    internal BigDouble DurationMod { get; }

    /// <summary>The chance of an echo and what one is worth, then the same for crits.</summary>
    internal BigDouble EchoRating { get; }

    internal BigDouble EchoPower { get; }

    internal BigDouble CritRating { get; }

    internal BigDouble CritPower { get; }

    internal BigDouble CritDurationMod { get; }

    /// <summary>How many rituals may chain, and what chaining is worth.</summary>
    internal BigDouble ChainLengthBonus { get; }

    internal BigDouble ChainPower { get; }

    /// <summary>What finishing a run costs, and how fast the completion fills.</summary>
    internal BigDouble CompletionCostMod { get; }

    internal BigDouble CompletionRateMod { get; }
}

internal sealed class WorldRitualBinder : WorldPlainBinder<WorldRitual>
{
    private Func<object, Guid>? _id;
    private Func<object, bool>? _discovered;
    private Func<object, bool>? _inBattle;
    private Func<object, int>? _activeInstances;
    private Func<object, int>? _reachedLevel;
    private Func<object, int>? _lastReachedLevel;
    private Func<object, int>? _selectedLevel;
    private Func<object, int>? _wavesCompleted;
    private Func<object, int>? _discRarityLevel;
    private Func<object, int>? _critLevel;
    private Func<object, int>? _echoLevel;
    private Func<object, int>? _chainLevel;
    private Func<object, int>? _durationRewardBlocks;
    private Func<object, BigDouble>? _battleTotalWeight;
    private Func<object, BigDouble>? _power;
    private Func<object, BigDouble>? _speed;
    private Func<object, BigDouble>? _special;
    private Func<object, BigDouble>? _durationMod;
    private Func<object, BigDouble>? _echoRating;
    private Func<object, BigDouble>? _echoPower;
    private Func<object, BigDouble>? _critRating;
    private Func<object, BigDouble>? _critPower;
    private Func<object, BigDouble>? _critDurationMod;
    private Func<object, BigDouble>? _chainLengthBonus;
    private Func<object, BigDouble>? _chainPower;
    private Func<object, BigDouble>? _completionCostMod;
    private Func<object, BigDouble>? _completionRateMod;
    private Func<object, bool>? _hideEndScreenResults;
    private Func<object, bool>? _isDiscoverRequired;
    private Func<object, bool>? _forceLevel;
    private Func<object, int>? _forceLevelValue;
    private Func<object, int>? _baseWaves;
    private Func<object, int>? _maxWaves;
    private Func<object, int>? _requiredWaves;
    private Func<object, double>? _baseWeight;
    private Func<object, int>? _minimumEffectLevel;
    private Func<object, bool>? _failedRun;
    private Func<object, IList?>? _currentSpoils;
    private Func<object, object?>? _spoilResource;
    private Func<object, BigDouble>? _spoilQuantity;
    private Func<object, Guid>? _spoilResourceId;
    private WorldDiscoverableBinding? _discovery;
    private readonly Func<string, Type?> _resolveType;
    private WorldRitualDecisionBinding? _decision;
    private WorldRitualCompletionFormulaBinding? _completionFormula;

    internal WorldRitualBinder(Func<string, Type?> resolveType)
    {
        _resolveType = resolveType ?? throw new ArgumentNullException(nameof(resolveType));
    }

    internal override string Category => "rituals";

    internal override string TypeName => "RitualSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _id = bind.Call<Guid>("GetGuid");
        _discovered = bind.Field<bool>("discovered");
        _inBattle = bind.Field<bool>("inBattle");

        // The elements are variable-size and stay behind; the count is what HasActiveInstances()
        // returns, and is the game's own test for the ritual being active.
        _activeInstances = bind.CollectionCount("ritualInstances");
        _reachedLevel = bind.Field<int>("reachedLevel");
        _lastReachedLevel = bind.Field<int>("lastReachedLevel");
        _selectedLevel = bind.Field<int>("selectedLevel");
        _wavesCompleted = bind.Field<int>("wavesCompleted");
        _discRarityLevel = bind.Field<int>("discRarityLevel");
        _critLevel = bind.Field<int>("critLevel");
        _echoLevel = bind.Field<int>("echoLevel");
        _chainLevel = bind.Field<int>("chainLevel");
        _durationRewardBlocks = bind.CollectionCount("durationRewardBlocks");
        _battleTotalWeight = bind.Field<BigDouble>("battleTotalWeight");
        _power = bind.ModifierRecord("power");
        _speed = bind.ModifierRecord("speed");
        _special = bind.ModifierRecord("special");
        _durationMod = bind.ModifierRecord("durationMod");
        _echoRating = bind.ModifierRecord("echoRating");
        _echoPower = bind.ModifierRecord("echoPower");
        _critRating = bind.ModifierRecord("critRating");
        _critPower = bind.ModifierRecord("critPower");
        _critDurationMod = bind.ModifierRecord("critDurationMod");
        _chainLengthBonus = bind.ModifierRecord("chainLengthBonus");
        _chainPower = bind.ModifierRecord("chainPower");
        _completionCostMod = bind.ModifierRecord("completionCostMod");
        _completionRateMod = bind.ModifierRecord("completionRateMod");
        _hideEndScreenResults = bind.Field<bool>("hideEndScreenResults");
        _isDiscoverRequired = bind.Field<bool>("isDiscoverRequired");
        _forceLevel = bind.Field<bool>("forceLevel");
        _forceLevelValue = bind.Field<int>("forceLevelValue");
        _baseWaves = bind.Field<int>("baseWaves");
        _maxWaves = bind.Field<int>("maxWaves");

        // The wave count a run has to clear scales the authored wavesPerLevel modifier by the
        // staged level, so the game's own answer is captured instead of recomputed from the bounds.
        _requiredWaves = bind.Call<int>("GetRequiredWaves");
        _baseWeight = bind.Field<double>("baseWeight");
        _minimumEffectLevel = bind.Field<int>("minimumEffectLevel");

        // The game's own verdict and the exact lines its results screen lists. A run that ended has
        // no other record of either, so both are captured rather than inferred from wave counts.
        _failedRun = bind.Call<bool>("IsFailedRun");
        _currentSpoils = bind.CollectionField("currentSpoils");
        var spoilResourceType = _resolveType("ResourceSO");
        var spoil = bind.Elements(
            bind.CollectionElementType("currentSpoils"), "RitualSO.currentSpoils[]");
        _spoilResource = spoil.CallObject("get_resource", spoilResourceType);
        _spoilQuantity = spoil.Field<BigDouble>("quantity");
        var spoilResource = new WorldMemberBinding(spoilResourceType!, "ResourceSO");
        _spoilResourceId = spoilResource.Call<Guid>("GetGuid");
        _discovery = new WorldDiscoverableBinding(type, TypeName);
        _decision = new WorldRitualDecisionBinding(type, _resolveType);
        _completionFormula = new WorldRitualCompletionFormulaBinding(type, _resolveType);
        return Join(
            bind.Failure, spoilResource.Failure,
            _discovery.Failure, _decision.Failure, _completionFormula.Failure);
    }

    internal override WorldRitual Read(object entity) =>
        new(
            _id!(entity),
            _discovered!(entity),
            _inBattle!(entity),
            _activeInstances!(entity),
            _reachedLevel!(entity),
            _lastReachedLevel!(entity),
            _selectedLevel!(entity),
            _wavesCompleted!(entity),
            _discRarityLevel!(entity),
            _critLevel!(entity),
            _echoLevel!(entity),
            _chainLevel!(entity),
            _durationRewardBlocks!(entity),
            _battleTotalWeight!(entity),
            new RawRitualModifiers(
                _power!(entity),
                _speed!(entity),
                _special!(entity),
                _durationMod!(entity),
                _echoRating!(entity),
                _echoPower!(entity),
                _critRating!(entity),
                _critPower!(entity),
                _critDurationMod!(entity),
                _chainLengthBonus!(entity),
                _chainPower!(entity),
                _completionCostMod!(entity),
                _completionRateMod!(entity)),
            _hideEndScreenResults!(entity),
            _isDiscoverRequired!(entity),
            _forceLevel!(entity),
            _forceLevelValue!(entity),
            _baseWaves!(entity),
            _maxWaves!(entity),
            _requiredWaves!(entity),
            _baseWeight!(entity),
            _minimumEffectLevel!(entity),
            _failedRun!(entity),
            ReadSpoils(entity),
            _discovery!.Read(entity),
            _decision!.Read(entity),
            _completionFormula!.Read(entity));

    private WorldRitualSpoil[] ReadSpoils(object entity)
    {
        var source = _currentSpoils!(entity) ??
            throw new InvalidOperationException("RitualSO.currentSpoils was null");
        if (source.Count == 0) return Array.Empty<WorldRitualSpoil>();
        var result = new WorldRitualSpoil[source.Count];
        for (var index = 0; index < source.Count; index++)
        {
            var entry = source[index] ??
                throw new InvalidOperationException("RitualSO.currentSpoils contained null");
            var resource = _spoilResource!(entry) ??
                throw new InvalidOperationException("SpoilsRecordEntry.resource was null");
            var resourceId = _spoilResourceId!(resource);
            if (resourceId == Guid.Empty)
                throw new InvalidOperationException(
                    "SpoilsRecordEntry.resource had no stable UUID");
            result[index] = new WorldRitualSpoil(resourceId, _spoilQuantity!(entry));
        }
        return result;
    }

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}

/// <summary>Captures the authored ritual completion formula without evaluating native math.</summary>
internal sealed class WorldRitualCompletionFormulaBinding
{
    private readonly Func<object, object?>? _baseCost;
    private readonly Func<object, object?>? _perLevel;
    private readonly Func<object, object?>? _perLevelValue;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, Guid>? _resourceId;
    private readonly Func<object, IList?>? _modifiers;
    private readonly Func<object, IList?>? _exponents;
    private readonly Func<object, int>? _modifierType;
    private readonly Func<object, int>? _modifierOrder;
    private readonly Func<object, BigDouble>? _modifierAmount;

    internal WorldRitualCompletionFormulaBinding(Type ritualType, Func<string, Type?> resolveType)
    {
        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        var resourceType = resolveType("ResourceSO");
        var modifierRefType = resolveType("ModifierListRef");
        var modifierListType = resolveType("ValueModifierList");
        if (costType is null || tupleType is null || resourceType is null ||
            modifierRefType is null || modifierListType is null)
        {
            Failure = "Ritual completion formula types were unavailable";
            return;
        }

        _baseCost = NativeAccessorBinder.Reference(ritualType, "completionCost", costType);
        _perLevel = NativeAccessorBinder.Reference(ritualType, "completionCostPerLevel", modifierRefType);
        _perLevelValue = NativeAccessorBinder.CallObject(modifierRefType, "GetValue", modifierListType);
        var entriesMethod = costType.GetMethod("GetEntries", BindingFlags.Instance |
            BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        var entriesType = entriesMethod?.ReturnType;
        var exactTuple = entriesType is { IsGenericType: true }
            ? entriesType.GetGenericArguments()[0]
            : null;
        _costEntries = NativeAccessorBinder.CallList(costType, "GetEntries", exactTuple);
        _costResource = NativeAccessorBinder.Reference(tupleType, "resource", resourceType);
        _costValue = NativeAccessorBinder.Call<BigDouble>(tupleType, "GetValue");
        _resourceId = NativeAccessorBinder.Call<Guid>(resourceType, "GetGuid");
        _modifiers = NativeAccessorBinder.CollectionField(modifierListType, "modifiers");
        _exponents = NativeAccessorBinder.CollectionField(modifierListType, "exponents");
        var modifierType = NativeAccessorBinder.CollectionElementType(modifierListType, "modifiers");
        _modifierType = NativeAccessorBinder.EnumField(modifierType, "type");
        _modifierOrder = NativeAccessorBinder.Field<int>(modifierType, "order");
        _modifierAmount = NativeAccessorBinder.Field<BigDouble>(modifierType, "adjustReal");

        Failure = _baseCost is null || _perLevel is null || _perLevelValue is null ||
            _costEntries is null || _costResource is null || _costValue is null ||
            _resourceId is null || _modifiers is null || _exponents is null ||
            _modifierType is null || _modifierOrder is null || _modifierAmount is null
                ? "Ritual completion formula bindings were unavailable"
                : string.Empty;
    }

    internal string Failure { get; }

    internal WorldRitualCompletionFormula Read(object ritual)
    {
        var cost = _baseCost!(ritual) ??
            throw new InvalidOperationException("RitualSO.completionCost was unavailable");
        var perLevel = _perLevel!(ritual) ??
            throw new InvalidOperationException("RitualSO.completionCostPerLevel was unavailable");
        var list = _perLevelValue!(perLevel) ??
            throw new InvalidOperationException("ModifierListRef.GetValue returned null");
        return new WorldRitualCompletionFormula(
            ReadCosts(cost),
            ReadModifiers(_modifiers!(list)),
            ReadModifiers(_exponents!(list)));
    }

    private PublicationTable<WorldRitualCost> ReadCosts(object cost)
    {
        var entries = _costEntries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries returned null");
        var rows = new WorldRitualCost[entries.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Ritual completion cost row was null");
            var resource = _costResource!(entry) ??
                throw new InvalidOperationException("Ritual completion cost row had no resource");
            rows[index] = new WorldRitualCost(_resourceId!(resource), _costValue!(entry));
        }
        return PublicationTable<WorldRitualCost>.Create(rows, rows.Length);
    }

    private PublicationTable<GameValueModifier> ReadModifiers(IList? source)
    {
        if (source is null || source.Count == 0)
            return PublicationTable<GameValueModifier>.Empty;
        var rows = new GameValueModifier[source.Count];
        for (var index = 0; index < rows.Length; index++)
        {
            var modifier = source[index] ??
                throw new InvalidOperationException("Ritual completion modifier row was null");
            rows[index] = new GameValueModifier(
                (GameValueModifierType)_modifierType!(modifier),
                _modifierAmount!(modifier),
                _modifierOrder!(modifier));
        }
        return PublicationTable<GameValueModifier>.Create(rows, rows.Length);
    }
}

/// <summary>Reads only the selected ritual's level and activation price.</summary>
internal sealed class WorldRitualDecisionBinding
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private readonly Type? _managerType;
    private readonly Func<object?>? _manager;
    private readonly Func<object, object?>? _selectedVariable;
    private readonly Func<object, object, bool>? _isSelected;
    private readonly Func<object, int>? _maximumStartingLevel;
    private readonly Func<object, bool>? _usageRequirementsMet;
    private readonly Func<object, object?>? _activationCost;
    private readonly Func<object, bool>? _hasEnough;
    private readonly Func<object, IList?>? _costEntries;
    private readonly Func<object, object?>? _costResource;
    private readonly Func<object, BigDouble>? _costValue;
    private readonly Func<object, Guid>? _resourceId;

    internal WorldRitualDecisionBinding(Type ritualType, Func<string, Type?> resolveType)
    {
        _managerType = resolveType("RitualManager");
        var variableType = resolveType("RitualVariable");
        var costType = resolveType("ResourceCostList");
        var tupleType = resolveType("ResourceTuple");
        var resourceType = resolveType("ResourceSO");
        if (_managerType is null || variableType is null || costType is null ||
            tupleType is null || resourceType is null)
        {
            Failure = "Ritual decision types were unavailable";
            return;
        }

        _manager = StaticReference(_managerType, "instance", _managerType);
        _selectedVariable = Reference(_managerType, "selectedRitual", variableType);
        _isSelected = CallWithReference<bool>(variableType, "IsItem", ritualType);
        var ritual = new WorldMemberBinding(ritualType, "RitualSO");
        _maximumStartingLevel = ritual.Call<int>("GetMaxSelectedLevel");
        _usageRequirementsMet = ritual.Call<bool>("HasMetUsageRequirements");
        _activationCost = ritual.CallObject("GetActivationCost", costType);

        var cost = new WorldMemberBinding(costType, "ResourceCostList");
        _hasEnough = cost.Call<bool>("HasEnough");
        var entriesMethod = costType.GetMethod("GetEntries", Instance, null, Type.EmptyTypes, null);
        var entriesType = entriesMethod?.ReturnType;
        var exactTuple = entriesType is { IsGenericType: true }
            ? entriesType.GetGenericArguments()[0]
            : null;
        _costEntries = cost.CallList("GetEntries", exactTuple);
        var tuple = new WorldMemberBinding(tupleType, "ResourceTuple");
        _costResource = tuple.Reference("resource", resourceType);
        _costValue = tuple.Call<BigDouble>("GetValue");
        var resource = new WorldMemberBinding(resourceType, "ResourceSO");
        _resourceId = resource.Call<Guid>("GetGuid");

        Failure = Join(
            _manager is null ? "RitualManager.instance was unavailable" : string.Empty,
            _selectedVariable is null ? "RitualManager.selectedRitual was unavailable" : string.Empty,
            _isSelected is null ? "RitualVariable.IsItem was unavailable" : string.Empty,
            ritual.Failure,
            cost.Failure,
            tuple.Failure,
            resource.Failure);
    }

    internal string Failure { get; }

    internal WorldRitualDecision Read(object ritual)
    {
        var manager = _manager!();
        if (manager is null || manager.GetType() != _managerType)
            throw new InvalidOperationException("RitualManager.instance was unavailable");
        var selectedVariable = _selectedVariable!(manager) ??
            throw new InvalidOperationException("RitualManager.selectedRitual was unavailable");
        var selected = _isSelected!(selectedVariable, ritual);

        // GetMaxSelectedLevel() is Math.Max(reachedLevel + 1, Player.GetCeremonialLevel()) — a fact
        // of the ritual and the player, not of the selection. Reading it only for the selected
        // ritual made the starting-level ceiling something a caller had to select a ritual to
        // discover.
        var maximumStartingLevel = _maximumStartingLevel!(ritual);
        if (!selected)
            return new WorldRitualDecision(
                false, maximumStartingLevel, false, false,
                PublicationTable<WorldRitualCost>.Empty,
                PublicationTable<WorldRitualCost>.Empty);

        var activation = _activationCost!(ritual) ??
            throw new InvalidOperationException("RitualSO.GetActivationCost returned null");
        return new WorldRitualDecision(
            true,
            maximumStartingLevel,
            _usageRequirementsMet!(ritual),
            _hasEnough!(activation),
            ReadCosts(activation),
            PublicationTable<WorldRitualCost>.Empty);
    }

    private PublicationTable<WorldRitualCost> ReadCosts(object cost)
    {
        var entries = _costEntries!(cost) ??
            throw new InvalidOperationException("ResourceCostList.GetEntries returned null");
        var rows = new WorldRitualCost[entries.Count];
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index] ??
                throw new InvalidOperationException("Ritual cost row " + index + " was null");
            var resource = _costResource!(entry) ??
                throw new InvalidOperationException("Ritual cost row " + index + " had no resource");
            rows[index] = new WorldRitualCost(
                _resourceId!(resource), _costValue!(entry));
        }
        return PublicationTable<WorldRitualCost>.Create(rows);
    }

    private static Func<object?>? StaticReference(Type owner, string name, Type exactType)
    {
        var field = owner.GetField(name, Static);
        if (field is null || !field.IsStatic || field.FieldType != exactType) return null;
        try
        {
            return Expression.Lambda<Func<object?>>(
                Expression.Convert(Expression.Field(null, field), typeof(object))).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, object?>? Reference(Type owner, string name, Type exactType)
    {
        var field = owner.GetField(name, Instance);
        if (field is null || field.IsStatic || field.FieldType != exactType) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            return Expression.Lambda<Func<object, object?>>(
                Expression.Convert(
                    Expression.Field(Expression.Convert(target, owner), field),
                    typeof(object)),
                target).Compile();
        }
        catch (Exception) { return null; }
    }

    private static Func<object, object, T>? CallWithReference<T>(
        Type owner,
        string name,
        Type argumentType)
    {
        var method = owner.GetMethod(name, Instance, null, new[] { argumentType }, null);
        if (method is null || method.ReturnType != typeof(T)) return null;
        try
        {
            var target = Expression.Parameter(typeof(object), "target");
            var argument = Expression.Parameter(typeof(object), "argument");
            return Expression.Lambda<Func<object, object, T>>(
                Expression.Call(
                    Expression.Convert(target, owner),
                    method,
                    Expression.Convert(argument, argumentType)),
                target,
                argument).Compile();
        }
        catch (Exception) { return null; }
    }

    private static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}

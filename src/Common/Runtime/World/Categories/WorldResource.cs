using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// What the main thread grabs off one <c>ResourceSO</c>: values lifted straight out of the game with
/// no arithmetic, no classification, and no allocation.
/// </summary>
/// <remarks>
/// A sample type exists only where a category has something to derive; the others publish their
/// reading unchanged and declare one struct. Every modifier reading here is folded from the record's
/// <c>baseValue</c> and modifier sets rather than lifted out of its cache — see
/// <c>docs/runtime-architecture/world-collection.md</c> and
/// <see cref="NativeModifierRecordAccess"/> for why the cache was never the game's answer.
/// </remarks>
internal readonly struct RawResourceSample : IWorldEntity
{
    internal RawResourceSample(
        Guid resourceId,
        BigDouble quantity,
        BigDouble capacity,
        bool visible,
        BigDouble lifetimeQuantity,
        BigDouble discoveryTime,
        BigDouble quality,
        BigDouble gainRate,
        BigDouble drain,
        BigDouble reservation,
        BigDouble usage,
        bool inLossMode,
        bool inRestMode,
        bool inRallyMode,
        long appliedLevels,
        Guid levelVariableId,
        in RawResourceRateInputs rateInputs,
        in RawResourceTraits traits,
        in RawResourceModifiers modifiers)
    {
        ResourceId = resourceId;
        Quantity = quantity;
        Capacity = capacity;
        Visible = visible;
        LifetimeQuantity = lifetimeQuantity;
        DiscoveryTime = discoveryTime;
        Quality = quality;
        GainRate = gainRate;
        Drain = drain;
        Reservation = reservation;
        Usage = usage;
        InLossMode = inLossMode;
        InRestMode = inRestMode;
        InRallyMode = inRallyMode;
        AppliedLevels = appliedLevels;
        LevelVariableId = levelVariableId;
        RateInputs = rateInputs;
        Traits = traits;
        Modifiers = modifiers;
    }

    internal Guid ResourceId { get; }

    /// <summary>The identity every category-generic lookup and traversal reads.</summary>
    public Guid EntityId => ResourceId;

    /// <summary>Current holdings, before quality is applied.</summary>
    internal BigDouble Quantity { get; }

    /// <summary>
    /// The effective storage ceiling, or a negative value when the resource is uncapped — the game's
    /// own convention, since <c>HasMaxQuantity()</c> is <c>maxQuantity &gt;= 0</c>. Zero is therefore a
    /// real ceiling of zero rather than an absent one.
    /// </summary>
    /// <remarks>
    /// This must come from the game's live functional maximum, not from a persisted field. The
    /// serialized <c>appliedMaxQuantity</c> is zero for all but one resource in a real save, because
    /// the effective cap is recomputed from <c>maxQuantityFunctional</c>, level modifiers, and soft-cap
    /// terms rather than stored. Reading the persisted value would report almost everything as
    /// uncapped and silently disable every capacity-relative stance.
    /// </remarks>
    internal BigDouble Capacity { get; }

    /// <summary>Whether the player has discovered this resource. Undiscovered resources still sample.</summary>
    internal bool Visible { get; }

    /// <summary>Everything ever gained since the last reset. The denominator of the lifetime rate term.</summary>
    internal BigDouble LifetimeQuantity { get; }

    /// <summary>When the resource was discovered, on the game's own clock.</summary>
    internal BigDouble DiscoveryTime { get; }

    /// <summary>
    /// Quality as the game's percent representation, where 100 is parity. It scales holdings up
    /// (<c>GetTrueQuantity() => quantity * quality.AsPercent()</c>) and spending down
    /// (<c>GetTrueSpend(amount) => amount / quality.AsPercent()</c>), so a stance that compares a cost
    /// against a raw quantity is wrong in both directions at once.
    /// </summary>
    internal BigDouble Quality { get; }

    /// <summary>Percent scaling applied to non-raw gains, and to the rate and splash rate terms.</summary>
    internal BigDouble GainRate { get; }

    /// <summary>Continuous drain before quality, in units per second.</summary>
    internal BigDouble Drain { get; }

    /// <summary>
    /// Percent divisor applied to the resource's effect quantity: holdings pledged elsewhere do not
    /// count toward what this resource does.
    /// </summary>
    internal BigDouble Reservation { get; }

    /// <summary>How much of the resource is currently claimed by running effects.</summary>
    internal BigDouble Usage { get; }

    /// <summary>Whether percentage loss is currently being applied.</summary>
    internal bool InLossMode { get; }

    /// <summary>Whether the resource has settled into its resting rate.</summary>
    internal bool InRestMode { get; }

    /// <summary>Whether the rally bonus is currently engaged.</summary>
    internal bool InRallyMode { get; }

    /// <summary>
    /// How many levels this resource has granted. Non-zero only for experience-type resources, which
    /// are the ones that convert holdings into levels.
    /// </summary>
    internal long AppliedLevels { get; }

    /// <summary>
    /// The variable those levels are pushed into, or <see cref="Guid.Empty"/> for a resource that
    /// grants none. The value lives in the global registry; only the edge belongs here. See D17.
    /// </summary>
    internal Guid LevelVariableId { get; }

    /// <summary>Everything the ported rate chain reads that the rest of this reading does not carry.</summary>
    internal RawResourceRateInputs RateInputs { get; }

    /// <summary>The resource's own remaining scalars.</summary>
    internal RawResourceTraits Traits { get; }

    /// <summary>Every cached modifier record the readings above do not already carry.</summary>
    internal RawResourceModifiers Modifiers { get; }
}

/// <summary>The rest of a resource's own scalars: how the game classifies it, the shape of its soft cap, and the batched amounts it is carrying between ticks.</summary>
internal readonly struct RawResourceTraits
{
    internal RawResourceTraits(
        double rarityValue,
        double rarityValueEnd,
        double restEngageTime,
        bool pauseLossOnChange,
        bool canOverflow,
        bool noOverflowRubberBand,
        bool bandwidthResource,
        bool invertedResource,
        bool excludeFromGlobals,
        bool startVisible,
        BigDouble appliedMaxQuantity,
        int quantitySoftCapOrder,
        int quantitySoftCapMagnitude,
        double quantitySoftCapRatio,
        bool debugResource,
        double currentLossRate,
        BigDouble lastReservation,
        BigDouble debouncedReplenish,
        BigDouble debouncedReverberate,
        BigDouble debouncedDecay,
        bool firstIncrement)
    {
        RarityValue = rarityValue;
        RarityValueEnd = rarityValueEnd;
        RestEngageTime = restEngageTime;
        PauseLossOnChange = pauseLossOnChange;
        CanOverflow = canOverflow;
        NoOverflowRubberBand = noOverflowRubberBand;
        BandwidthResource = bandwidthResource;
        InvertedResource = invertedResource;
        ExcludeFromGlobals = excludeFromGlobals;
        StartVisible = startVisible;
        AppliedMaxQuantity = appliedMaxQuantity;
        QuantitySoftCapOrder = quantitySoftCapOrder;
        QuantitySoftCapMagnitude = quantitySoftCapMagnitude;
        QuantitySoftCapRatio = quantitySoftCapRatio;
        DebugResource = debugResource;
        CurrentLossRate = currentLossRate;
        LastReservation = lastReservation;
        DebouncedReplenish = debouncedReplenish;
        DebouncedReverberate = debouncedReverberate;
        DebouncedDecay = debouncedDecay;
        FirstIncrement = firstIncrement;
    }

    /// <summary>How rare the game considers this resource, and where that rarity ends. The splash rate divides by the calculated rarity these define.</summary>
    internal double RarityValue { get; }

    internal double RarityValueEnd { get; }

    /// <summary>Seconds of no change before the resource settles into its resting rate.</summary>
    internal double RestEngageTime { get; }

    /// <summary>Whether a change suspends the loss timer, and whether holdings may exceed the ceiling at all.</summary>
    internal bool PauseLossOnChange { get; }

    internal bool CanOverflow { get; }

    internal bool NoOverflowRubberBand { get; }

    /// <summary>Whether the resource is bandwidth, is inverted, is kept out of global aggregates, or starts visible.</summary>
    internal bool BandwidthResource { get; }

    internal bool InvertedResource { get; }

    internal bool ExcludeFromGlobals { get; }

    internal bool StartVisible { get; }

    /// <summary>The ceiling as last persisted. Almost always zero — the effective cap is recomputed rather than stored, which is why Capacity reads the live modifier instead.</summary>
    internal BigDouble AppliedMaxQuantity { get; }

    /// <summary>The soft cap's shape: how many orders it spans, its magnitude, and the fraction it admits past the cap.</summary>
    internal int QuantitySoftCapOrder { get; }

    internal int QuantitySoftCapMagnitude { get; }

    internal double QuantitySoftCapRatio { get; }

    /// <summary>The game's own debug flag for this entry.</summary>
    internal bool DebugResource { get; }

    /// <summary>The loss currently being applied, and the reservation the last recalculation scaled holdings by.</summary>
    internal double CurrentLossRate { get; }

    internal BigDouble LastReservation { get; }

    /// <summary>Amounts accumulated toward the next replenish, reverberate and decay tick. The game batches these rather than creating a timer per gain.</summary>
    internal BigDouble DebouncedReplenish { get; }

    internal BigDouble DebouncedReverberate { get; }

    internal BigDouble DebouncedDecay { get; }

    /// <summary>Whether the resource has yet run an increment this session; the first one applies reservation differently.</summary>
    internal bool FirstIncrement { get; }
}

/// <summary>A resource's remaining cached modifier records — every one the rate and cost chains do not already read.</summary>
internal readonly struct RawResourceModifiers
{
    internal RawResourceModifiers(
        BigDouble maxQuantityRate,
        BigDouble maxQuantityFunctional,
        BigDouble restingRateMod,
        BigDouble attributeCostMod,
        BigDouble decayRatio,
        BigDouble decayTimeMod,
        BigDouble replenishRatio,
        BigDouble replenishTimeMod,
        BigDouble reverberateMod,
        BigDouble reverberateTimeMod,
        BigDouble rallyThreshold,
        BigDouble rallyMod,
        BigDouble usageDrainPenalty)
    {
        MaxQuantityRate = maxQuantityRate;
        MaxQuantityFunctional = maxQuantityFunctional;
        RestingRateMod = restingRateMod;
        AttributeCostMod = attributeCostMod;
        DecayRatio = decayRatio;
        DecayTimeMod = decayTimeMod;
        ReplenishRatio = replenishRatio;
        ReplenishTimeMod = replenishTimeMod;
        ReverberateMod = reverberateMod;
        ReverberateTimeMod = reverberateTimeMod;
        RallyThreshold = rallyThreshold;
        RallyMod = rallyMod;
        UsageDrainPenalty = usageDrainPenalty;
    }

    /// <summary>How fast the ceiling itself grows, and the functional ceiling the effective cap is computed from.</summary>
    internal BigDouble MaxQuantityRate { get; }

    internal BigDouble MaxQuantityFunctional { get; }

    /// <summary>The rate multiplier applied once the resource is resting.</summary>
    internal BigDouble RestingRateMod { get; }

    /// <summary>What this resource costs as an attribute input — the cost chain's own multiplier.</summary>
    internal BigDouble AttributeCostMod { get; }

    /// <summary>The share that decays and how long a decay runs; then the same pair for replenish and reverberate.</summary>
    internal BigDouble DecayRatio { get; }

    internal BigDouble DecayTimeMod { get; }

    internal BigDouble ReplenishRatio { get; }

    internal BigDouble ReplenishTimeMod { get; }

    internal BigDouble ReverberateMod { get; }

    internal BigDouble ReverberateTimeMod { get; }

    /// <summary>The threshold at which rally engages and what it is worth once it has.</summary>
    internal BigDouble RallyThreshold { get; }

    internal BigDouble RallyMod { get; }

    /// <summary>The drain penalty scaled by how much of the resource running effects have claimed.</summary>
    internal BigDouble UsageDrainPenalty { get; }
}

/// <summary>
/// The remaining arguments of the ported rate chain, gathered so it can run off the snapshot instead
/// of calling the game's <c>GetTrueRate()</c>.
/// </summary>
/// <remarks>
/// <para>
/// These are grouped rather than flattened into <see cref="RawResourceSample"/> because they are one
/// function's argument list, not sixteen independently interesting facts. A consumer that wants the
/// rate wants all of them; a consumer that wants holdings wants none.
/// </para>
/// <para>
/// The active-modifier counts stand in for <c>ModifierRecord.HasActiveElements()</c>, which is
/// <c>activeModifiers.Count &gt; 0</c>. The count travels rather than the boolean because it is the
/// raw fact and the comparison is derivation — and because "how many modifiers are on this rate" is
/// worth knowing on its own.
/// </para>
/// <para>
/// The three per-tick globals the chain also needs — resource overflow, overflow loss, and elapsed
/// reset time — are not here. They are <c>DoubleVariable</c>s and so are already collected in the
/// snapshot's own global registry; duplicating them per resource would publish eighty copies of one
/// number. The fourth, Unity's fixed delta, is on <see cref="GameWorldState"/> because it is a
/// property of the tick rather than of any resource.
/// </para>
/// </remarks>
internal readonly struct RawResourceRateInputs
{
    internal RawResourceRateInputs(
        BigDouble rate,
        BigDouble rateSplash,
        BigDouble rateMaxPercent,
        BigDouble rateInterestPercent,
        BigDouble rateMissingPercent,
        BigDouble rateLifetimePercent,
        int rateModifiers,
        int rateSplashModifiers,
        int rateMaxPercentModifiers,
        int rateInterestPercentModifiers,
        int rateMissingPercentModifiers,
        int rateLifetimePercentModifiers,
        BigDouble lossPercent,
        BigDouble displayRate,
        BigDouble calcRarityValue,
        double baseLoss)
    {
        Rate = rate;
        RateSplash = rateSplash;
        RateMaxPercent = rateMaxPercent;
        RateInterestPercent = rateInterestPercent;
        RateMissingPercent = rateMissingPercent;
        RateLifetimePercent = rateLifetimePercent;
        RateModifiers = rateModifiers;
        RateSplashModifiers = rateSplashModifiers;
        RateMaxPercentModifiers = rateMaxPercentModifiers;
        RateInterestPercentModifiers = rateInterestPercentModifiers;
        RateMissingPercentModifiers = rateMissingPercentModifiers;
        RateLifetimePercentModifiers = rateLifetimePercentModifiers;
        LossPercent = lossPercent;
        DisplayRate = displayRate;
        CalcRarityValue = calcRarityValue;
        BaseLoss = baseLoss;
    }

    /// <summary>The flat rate term, before every percent term is folded in.</summary>
    internal BigDouble Rate { get; }

    /// <summary>The splash term, scaled by the inverse of the resource's calculated rarity.</summary>
    internal BigDouble RateSplash { get; }

    /// <summary>Percent of capacity, of accrued interest, of what is missing, and of lifetime total.</summary>
    internal BigDouble RateMaxPercent { get; }

    internal BigDouble RateInterestPercent { get; }

    internal BigDouble RateMissingPercent { get; }

    internal BigDouble RateLifetimePercent { get; }

    /// <summary>
    /// How many active modifiers sit on each rate record. Zero is the game's
    /// <c>!HasActiveElements()</c>, which is what makes a term drop out of the chain entirely rather
    /// than contribute zero — the two are not the same, because the chain branches on it.
    /// </summary>
    internal int RateModifiers { get; }

    internal int RateSplashModifiers { get; }

    internal int RateMaxPercentModifiers { get; }

    internal int RateInterestPercentModifiers { get; }

    internal int RateMissingPercentModifiers { get; }

    internal int RateLifetimePercentModifiers { get; }

    /// <summary>The percentage bled off per loss tick while the resource is in loss mode.</summary>
    internal BigDouble LossPercent { get; }

    /// <summary>The rate the game shows, which is not always the rate it applies.</summary>
    internal BigDouble DisplayRate { get; }

    /// <summary>
    /// The rarity the game last calculated for this resource. Private in the game and absent from the
    /// save record — it is recomputed rather than persisted — but the splash term divides by it, so
    /// the chain cannot run without it.
    /// </summary>
    internal BigDouble CalcRarityValue { get; }

    /// <summary>The definition's base loss fraction, a plain double rather than a modifier record.</summary>
    internal double BaseLoss { get; }
}

/// <summary>
/// One resource as published to every worker: the raw reading, plus the facts derived from it.
/// </summary>
/// <remarks>
/// Derived values are stored rather than recomputed on read. The whole point of the shared world
/// publication is that meaning is computed once, off the Unity thread, for the roughly ten services
/// expected to consume it — recomputing a fill fraction per consumer per cycle would put the cost
/// back exactly where it was removed from.
/// <para>
/// The reading is carried whole rather than copied field by field, so "what the game said" and "what
/// the suite worked out" stay distinguishable at the type level. A consumer reading
/// <c>row.Reading.Quantity</c> knows it is looking at the game's number; one reading
/// <c>row.TrueQuantity</c> knows it is looking at ours.
/// </para>
/// <para>
/// Only facts are derived here, never policy. Whether 60 of a 100 cap is "nearly full" is a stance
/// decision belonging to <see cref="Strategy.SuiteResourceStance"/>; that it <em>is</em> 0.6 of capacity
/// is a fact belonging here.
/// </para>
/// </remarks>
internal readonly struct WorldResource : IWorldEntity
{
    internal WorldResource(
        in RawResourceSample reading,
        bool isCapped,
        BigDouble headroom,
        double fillFraction,
        bool isAtCapacity,
        BigDouble trueQuantity,
        BigDouble trueRate)
    {
        Reading = reading;
        IsCapped = isCapped;
        Headroom = headroom;
        FillFraction = fillFraction;
        IsAtCapacity = isAtCapacity;
        TrueQuantity = trueQuantity;
        TrueRate = trueRate;
    }

    /// <summary>Exactly what the game held when the main thread looked.</summary>
    internal RawResourceSample Reading { get; }

    public Guid EntityId => Reading.ResourceId;

    /// <summary>Whether a storage ceiling applies. When false, <c>Reading.Capacity</c> means nothing.</summary>
    internal bool IsCapped { get; }

    /// <summary>
    /// Room left before the ceiling, never negative. Zero when uncapped, which callers must read
    /// together with <see cref="IsCapped"/> — an uncapped resource has unlimited room, not none.
    /// </summary>
    internal BigDouble Headroom { get; }

    /// <summary>
    /// Holdings as a share of capacity, clamped to <c>[0, 1]</c>. Zero when uncapped. A double is
    /// exact enough here because a ratio is bounded even when both operands are astronomically large.
    /// </summary>
    internal double FillFraction { get; }

    /// <summary>
    /// Whether the counter's displayed number has reached or passed the ceiling. Ordinary counters
    /// display stored quantity; inverted ones display missing capacity, so they are at capacity when
    /// nothing is stored. Reading this against <see cref="WorldResourceCoordinate.SpendableAmount"/>
    /// mixes coordinates — it belongs beside
    /// <see cref="WorldResourceCoordinate.DisplayAmount"/>.
    /// </summary>
    internal bool IsAtCapacity { get; }

    /// <summary>
    /// Holdings scaled by quality, matching the game's <c>GetTrueQuantity()</c>. This, not the raw
    /// quantity, is what the resource is worth to anything that consumes it.
    /// </summary>
    /// <remarks>
    /// Spending converts the other way — <c>GetTrueSpend(amount) => amount / quality.AsPercent()</c> —
    /// and is deliberately not precomputed here: it is a function of an amount, not of the resource,
    /// and inverting a quality of zero would put an infinity into a published row.
    /// </remarks>
    internal BigDouble TrueQuantity { get; }

    /// <summary>
    /// The net rate this resource actually accrues at, matching the game's <c>GetTrueRate()</c>:
    /// every flat and percent term folded together, less drain and loss, clamped at the ceiling.
    /// </summary>
    /// <remarks>
    /// Computed here rather than asked of the game because <c>GetTrueRate()</c> composes several
    /// terms and reaches <c>GetValue()</c> underneath, which recalculates and re-stamps observables —
    /// a write, on the suite's schedule, once per resource per consumer. Owning it means one
    /// computation per resource per cycle, off the Unity thread, and no write at all.
    /// </remarks>
    internal BigDouble TrueRate { get; }
}

/// <summary>
/// The two native coordinates a resource exposes: what its counter displays and what
/// <c>ResourceSO.HasAmount</c> can spend. The native predicates are deliberately independent.
/// </summary>
internal static class WorldResourceCoordinate
{
    /// <summary>
    /// Mirrors <c>ResourceSO.GetDisplayQuantity()</c>: inverted counters display missing capacity;
    /// every other counter displays stored quantity.
    /// </summary>
    internal static BigDouble DisplayAmount(in WorldResource resource) =>
        resource.Reading.Traits.InvertedResource
            ? resource.Headroom
            : resource.Reading.Quantity;

    /// <summary>
    /// Whether the number this resource displays is what is <em>left</em> of its ceiling: the very
    /// pool the game spends from, falling as it is spent and rising only as the total grows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both flags decide this, and reading one of them decided it wrongly. The display coordinate is
    /// <c>invertedResource</c> — <c>ResourceSO.GetDisplayQuantity()</c> returns <c>GetMissing()</c>
    /// when it is set — and the spending coordinate is <c>bandwidthResource</c>, because
    /// <c>ResourceSO.HasAmount</c> tests missing capacity for bandwidth and stored quantity for
    /// everything else. The two agree on the twelve advancement currencies, which carry both flags:
    /// there the displayed number *is* the budget, so "left" is what it is.
    /// </para>
    /// <para>
    /// Toxicity is the one resource in this build that is inverted and is not bandwidth. The game
    /// spends its stored quantity while displaying the missing half, so its number climbs toward the
    /// ceiling as potions are drunk, blocks the next one when it is full, and falls again as the
    /// stock recovers — a meter that fills, which is the opposite of what "left" tells a reader.
    /// Both flags were already captured; only the derivation read one of them.
    /// </para>
    /// </remarks>
    internal static bool DisplaysWhatIsLeft(in WorldResource resource) =>
        resource.Reading.Traits.InvertedResource && resource.Reading.Traits.BandwidthResource;

    /// <summary>
    /// The pool shown beside a player-facing cost: bandwidth spends missing capacity; ordinary
    /// resources spend stored quantity after the cost is converted through quality.
    /// </summary>
    internal static BigDouble SpendableAmount(in WorldResource resource) =>
        resource.Reading.Traits.BandwidthResource
            ? resource.Headroom
            : resource.Reading.Quantity;

    /// <summary>
    /// The amount comparable directly with an unconverted native cost. This is headroom for
    /// bandwidth and quality-scaled holdings for ordinary resources.
    /// </summary>
    internal static BigDouble NativeCostAmount(in WorldResource resource) =>
        resource.Reading.Traits.BandwidthResource
            ? resource.Headroom
            : resource.TrueQuantity;

    /// <summary>Mirrors <c>ResourceSO.GetTrueSpend</c> for ordinary resources.</summary>
    internal static BigDouble PlayerFacingCost(in WorldResource resource, BigDouble nominalCost)
    {
        if (resource.Reading.Traits.BandwidthResource) return nominalCost;
        var quality = OrbGameMath.AsPercent(resource.Reading.Quality);
        return quality == BigDouble.Zero ? nominalCost : nominalCost / quality;
    }

    /// <summary>Mirrors <c>ResourceSO.HasAmount</c>, including bandwidth integer snapping.</summary>
    internal static bool HasAmount(in WorldResource resource, BigDouble nominalCost)
    {
        if (resource.Reading.Traits.BandwidthResource)
        {
            return OrbGameMath.SnapFloorToInt(resource.Headroom) >=
                OrbGameMath.SnapFloorToInt(nominalCost);
        }

        return resource.Reading.Quantity.CompareTo(PlayerFacingCost(in resource, nominalCost)) >= 0;
    }
}

/// <summary>
/// The member list of one <c>ResourceSO</c>, bound against whatever object holds it.
/// </summary>
/// <remarks>
/// Extracted from <see cref="WorldResourceBinder"/> so the element-owned resource can be read with
/// the same list rather than a second copy of it. The two populations are read identically and must
/// stay identical; a duplicated list is a list that drifts.
/// <para>
/// Every modifier here is folded from the record's own <c>baseValue</c> and modifier sets rather
/// than read through its accessor, because that accessor recalculates and re-stamps an observable
/// when dirty — and rather than read out of its cache, because that cache is zero for any record
/// nothing has touched since the save loaded. See <see cref="NativeAccessorBinder.ModifierRecord"/>.
/// </para>
/// <para>
/// The rate answer is never asked of the game: the port in <c>GameResourceRateMath</c> owns that
/// composition from the raw arguments below. The three mode flags are private fields with no
/// accessor at all, which is why they are read as fields.
/// </para>
/// </remarks>
internal sealed class WorldResourceMembers
{
    private Func<object, Guid>? _id;
    private Func<object, BigDouble>? _quantity;
    private Func<object, BigDouble>? _capacity;
    private Func<object, bool>? _visible;
    private Func<object, BigDouble>? _lifetimeQuantity;
    private Func<object, BigDouble>? _discoveryTime;
    private Func<object, BigDouble>? _quality;
    private Func<object, BigDouble>? _gainRate;
    private Func<object, BigDouble>? _drain;
    private Func<object, BigDouble>? _reservation;
    private Func<object, BigDouble>? _usage;
    private Func<object, bool>? _inLossMode;
    private Func<object, bool>? _inRestMode;
    private Func<object, bool>? _inRallyMode;
    private Func<object, BigDouble>? _rateFlat;
    private Func<object, BigDouble>? _rateSplash;
    private Func<object, BigDouble>? _rateMaxPercent;
    private Func<object, BigDouble>? _rateInterestPercent;
    private Func<object, BigDouble>? _rateMissingPercent;
    private Func<object, BigDouble>? _rateLifetimePercent;
    private Func<object, int>? _rateModifiers;
    private Func<object, int>? _rateSplashModifiers;
    private Func<object, int>? _rateMaxPercentModifiers;
    private Func<object, int>? _rateInterestPercentModifiers;
    private Func<object, int>? _rateMissingPercentModifiers;
    private Func<object, int>? _rateLifetimePercentModifiers;
    private Func<object, BigDouble>? _lossPercent;
    private Func<object, BigDouble>? _displayRate;
    private Func<object, BigDouble>? _calcRarityValue;
    private Func<object, double>? _baseLoss;
    private Func<object, long>? _appliedLevels;
    private Func<object, Guid>? _levelVariable;
    private Func<object, double>? _rarityValue;
    private Func<object, double>? _rarityValueEnd;
    private Func<object, double>? _restEngageTime;
    private Func<object, bool>? _pauseLossOnChange;
    private Func<object, bool>? _canOverflow;
    private Func<object, bool>? _noOverflowRubberBand;
    private Func<object, bool>? _bandwidthResource;
    private Func<object, bool>? _invertedResource;
    private Func<object, bool>? _excludeFromGlobals;
    private Func<object, bool>? _startVisible;
    private Func<object, BigDouble>? _appliedMaxQuantity;
    private Func<object, int>? _quantitySoftCapOrder;
    private Func<object, int>? _quantitySoftCapMagnitude;
    private Func<object, double>? _quantitySoftCapRatio;
    private Func<object, bool>? _debugResource;
    private Func<object, double>? _currentLossRate;
    private Func<object, BigDouble>? _lastReservation;
    private Func<object, BigDouble>? _debouncedReplenish;
    private Func<object, BigDouble>? _debouncedReverberate;
    private Func<object, BigDouble>? _debouncedDecay;
    private Func<object, bool>? _firstIncrement;
    private Func<object, BigDouble>? _maxQuantityRate;
    private Func<object, BigDouble>? _maxQuantityFunctional;
    private Func<object, BigDouble>? _restingRateMod;
    private Func<object, BigDouble>? _attributeCostMod;
    private Func<object, BigDouble>? _decayRatio;
    private Func<object, BigDouble>? _decayTimeMod;
    private Func<object, BigDouble>? _replenishRatio;
    private Func<object, BigDouble>? _replenishTimeMod;
    private Func<object, BigDouble>? _reverberateMod;
    private Func<object, BigDouble>? _reverberateTimeMod;
    private Func<object, BigDouble>? _rallyThreshold;
    private Func<object, BigDouble>? _rallyMod;
    private Func<object, BigDouble>? _usageDrainPenalty;

    internal WorldResourceMembers(WorldMemberBinding bind)
    {
        _id = bind.Call<Guid>("GetGuid");
        _quantity = bind.Call<BigDouble>("GetQuantity");
        _capacity = bind.ModifierRecord("maxQuantity");
        _visible = bind.Call<bool>("IsVisible");
        _lifetimeQuantity = bind.Field<BigDouble>("lifetimeQuantity");
        _discoveryTime = bind.Field<BigDouble>("discoveryTime");
        _quality = bind.ModifierRecord("quality");
        _gainRate = bind.ModifierRecord("gainRate");
        _drain = bind.ModifierRecord("drain");
        _reservation = bind.ModifierRecord("reservationMod");
        _usage = bind.ModifierRecord("usage");
        _inLossMode = bind.Field<bool>("inLossMode");
        _inRestMode = bind.Field<bool>("inRestMode");
        _inRallyMode = bind.Field<bool>("inRallyMode");

        // The rate chain's own arguments. These are the only source of the owned answer.
        _rateFlat = bind.ModifierRecord("rate");
        _rateSplash = bind.ModifierRecord("rateSplash");
        _rateMaxPercent = bind.ModifierRecord("rateMaxPercent");
        _rateInterestPercent = bind.ModifierRecord("rateInterestPercent");
        _rateMissingPercent = bind.ModifierRecord("rateMissingPercent");
        _rateLifetimePercent = bind.ModifierRecord("rateLifetimePercent");
        _rateModifiers = bind.NestedCollectionCount("rate", "activeModifiers");
        _rateSplashModifiers = bind.NestedCollectionCount("rateSplash", "activeModifiers");
        _rateMaxPercentModifiers = bind.NestedCollectionCount("rateMaxPercent", "activeModifiers");
        _rateInterestPercentModifiers = bind.NestedCollectionCount("rateInterestPercent", "activeModifiers");
        _rateMissingPercentModifiers = bind.NestedCollectionCount("rateMissingPercent", "activeModifiers");
        _rateLifetimePercentModifiers = bind.NestedCollectionCount("rateLifetimePercent", "activeModifiers");
        _lossPercent = bind.ModifierRecord("lossPercent");
        _displayRate = bind.ModifierRecord("displayRate");
        _calcRarityValue = bind.Field<BigDouble>("calcRarityValue");
        _baseLoss = bind.Field<double>("baseLoss");
        _appliedLevels = bind.Field<long>("appliedLevels");
        _levelVariable = bind.ReferenceGuid("levelVariable");
        _rarityValue = bind.Field<double>("rarityValue");
        _rarityValueEnd = bind.Field<double>("rarityValueEnd");
        _restEngageTime = bind.Field<double>("restEngageTime");
        _pauseLossOnChange = bind.Field<bool>("pauseLossOnChange");
        _canOverflow = bind.Field<bool>("canOverflow");
        _noOverflowRubberBand = bind.Field<bool>("noOverflowRubberBand");
        _bandwidthResource = bind.Field<bool>("bandwidthResource");
        _invertedResource = bind.Field<bool>("invertedResource");
        _excludeFromGlobals = bind.Field<bool>("excludeFromGlobals");
        _startVisible = bind.Field<bool>("startVisible");
        _appliedMaxQuantity = bind.Field<BigDouble>("appliedMaxQuantity");
        _quantitySoftCapOrder = bind.Field<int>("quantitySoftCapOrder");
        _quantitySoftCapMagnitude = bind.Field<int>("quantitySoftCapMagnitude");
        _quantitySoftCapRatio = bind.Field<double>("quantitySoftCapRatio");
        _debugResource = bind.Field<bool>("debugResource");
        _currentLossRate = bind.Field<double>("currentLossRate");
        _lastReservation = bind.Field<BigDouble>("lastReservation");
        _debouncedReplenish = bind.Field<BigDouble>("debouncedReplenish");
        _debouncedReverberate = bind.Field<BigDouble>("debouncedReverberate");
        _debouncedDecay = bind.Field<BigDouble>("debouncedDecay");
        _firstIncrement = bind.Field<bool>("firstIncrement");

        // The remaining cached modifier records.
        _maxQuantityRate = bind.ModifierRecord("maxQuantityRate");
        _maxQuantityFunctional = bind.ModifierRecord("maxQuantityFunctional");
        _restingRateMod = bind.ModifierRecord("restingRateMod");
        _attributeCostMod = bind.ModifierRecord("attributeCostMod");
        _decayRatio = bind.ModifierRecord("decayRatio");
        _decayTimeMod = bind.ModifierRecord("decayTimeMod");
        _replenishRatio = bind.ModifierRecord("replenishRatio");
        _replenishTimeMod = bind.ModifierRecord("replenishTimeMod");
        _reverberateMod = bind.ModifierRecord("reverberateMod");
        _reverberateTimeMod = bind.ModifierRecord("reverberateTimeMod");
        _rallyThreshold = bind.ModifierRecord("rallyThreshold");
        _rallyMod = bind.ModifierRecord("rallyMod");
        _usageDrainPenalty = bind.ModifierRecord("usageDrainPenalty");
    }

    internal RawResourceSample Read(object entity) =>
        new(
            _id!(entity),
            _quantity!(entity),
            _capacity!(entity),
            _visible!(entity),
            _lifetimeQuantity!(entity),
            _discoveryTime!(entity),
            _quality!(entity),
            _gainRate!(entity),
            _drain!(entity),
            _reservation!(entity),
            _usage!(entity),
            _inLossMode!(entity),
            _inRestMode!(entity),
            _inRallyMode!(entity),
            _appliedLevels!(entity),
            _levelVariable!(entity),
            new RawResourceRateInputs(
                _rateFlat!(entity),
                _rateSplash!(entity),
                _rateMaxPercent!(entity),
                _rateInterestPercent!(entity),
                _rateMissingPercent!(entity),
                _rateLifetimePercent!(entity),
                _rateModifiers!(entity),
                _rateSplashModifiers!(entity),
                _rateMaxPercentModifiers!(entity),
                _rateInterestPercentModifiers!(entity),
                _rateMissingPercentModifiers!(entity),
                _rateLifetimePercentModifiers!(entity),
                _lossPercent!(entity),
                _displayRate!(entity),
                _calcRarityValue!(entity),
                _baseLoss!(entity)),
            new RawResourceTraits(
                _rarityValue!(entity),
                _rarityValueEnd!(entity),
                _restEngageTime!(entity),
                _pauseLossOnChange!(entity),
                _canOverflow!(entity),
                _noOverflowRubberBand!(entity),
                _bandwidthResource!(entity),
                _invertedResource!(entity),
                _excludeFromGlobals!(entity),
                _startVisible!(entity),
                _appliedMaxQuantity!(entity),
                _quantitySoftCapOrder!(entity),
                _quantitySoftCapMagnitude!(entity),
                _quantitySoftCapRatio!(entity),
                _debugResource!(entity),
                _currentLossRate!(entity),
                _lastReservation!(entity),
                _debouncedReplenish!(entity),
                _debouncedReverberate!(entity),
                _debouncedDecay!(entity),
                _firstIncrement!(entity)),
            new RawResourceModifiers(
                _maxQuantityRate!(entity),
                _maxQuantityFunctional!(entity),
                _restingRateMod!(entity),
                _attributeCostMod!(entity),
                _decayRatio!(entity),
                _decayTimeMod!(entity),
                _replenishRatio!(entity),
                _replenishTimeMod!(entity),
                _reverberateMod!(entity),
                _reverberateTimeMod!(entity),
                _rallyThreshold!(entity),
                _rallyMod!(entity),
                _usageDrainPenalty!(entity)));
}

/// <summary>
/// Resources: holdings, ceiling, net rate, quality, and the cached modifier records the rate and cost
/// chains are functions of.
/// </summary>
internal sealed class WorldResourceBinder : WorldRowBinder<RawResourceSample, WorldResource>
{
    private WorldResourceMembers? _members;

    internal override string Category => "resources";

    internal override string TypeName => "ResourceSO";

    internal override string Bind(Type type)
    {
        var bind = new WorldMemberBinding(type, TypeName);
        _members = new WorldResourceMembers(bind);
        return bind.Failure;
    }

    internal override RawResourceSample Read(object entity) => _members!.Read(entity);
}

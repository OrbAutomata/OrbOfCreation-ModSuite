using System;
using OrbModding.Common.Runtime.GameMath;

namespace OrbModding.Common.Runtime.World;

/// <summary>
/// Turns raw captured readings into published world rows. Pure and total: every function takes a
/// sample and returns a row, with no game access, no allocation, and no failure mode.
/// </summary>
/// <remarks>
/// This is the off-thread half of world collection. It exists as its own type, rather than as logic
/// inside a capture adapter, so that all derivation is reachable from tests without the game and so
/// the main-thread grab stays a grab. See <see cref="RawResourceSample"/> for the measurement that
/// motivated the split.
/// <para>
/// Derivation is confined to arithmetic over captured values, and where it reproduces one of the
/// game's own compositions it says which one and matches it exactly. That is sanctioned rather than
/// silent: <c>docs/runtime-architecture/goals-and-invariants.md</c> permits owned economy math that is
/// transcribed from a hash-gated build and differentially verified, and forbids inventing formulas the
/// game has not been read for. Native revalidation at the action boundary stays authoritative
/// regardless.
/// </para>
/// <para>
/// Unreadable arithmetic fails neutral, not confident: a NaN operand yields uncapped, zero-headroom,
/// zero-fraction output rather than a fabricated bound. A consumer then sees "no capacity
/// information", which capacity-relative stances already handle explicitly, instead of a number that
/// looks authoritative and is not.
/// </para>
/// </remarks>
internal static class GameWorldStateDeriver
{
    internal static WorldResource Derive(in RawResourceSample sample, in WorldFrameGlobals globals)
    {
        var quantity = sample.Quantity;
        var capacity = sample.Capacity;
        var trueRate = GameResourceRateMath.GetTrueRate(RateInputs(in sample, in globals));

        // Matches the game's GetTrueQuantity(): quantity * quality.AsPercent().
        var trueQuantity = quantity * OrbGameMath.AsPercent(sample.Quality);

        // A ceiling exists when the game says it does, and the game's own test is
        // `HasMaxQuantity() => maxQuantity >= 0` — so zero is a real ceiling of zero, not an absent
        // one. Mantissa comparison excludes NaN for free: every comparison against NaN is false, so an
        // unreadable ceiling still lands on the uncapped branch.
        var isCapped = IsNonNegative(capacity) && !BigDouble.IsNaN(quantity);
        if (!isCapped)
        {
            return new WorldResource(
                in sample,
                isCapped: false,
                headroom: BigDouble.Zero,
                fillFraction: 0d,
                isAtCapacity: false,
                trueQuantity,
                trueRate);
        }

        var remaining = capacity - quantity;
        var headroom = IsNegative(remaining) ? BigDouble.Zero : remaining;
        var fillFraction = Clamp01((quantity / capacity).ToDouble());

        // "At capacity" answers the question the counter asks, so it compares the counter's own
        // number with the counter's own ceiling. An inverted counter displays missing capacity
        // (`GetDisplayQuantity() => GetMissing()`), so it is full exactly when nothing is stored —
        // Toxicity reads full when toxicity is actually maxed, not when tolerance is untouched.
        // The game's own `IsAtMax()` compares stored quantity instead and has no callers.
        var displayed = sample.Traits.InvertedResource ? headroom : quantity;
        var isAtCapacity = displayed.CompareTo(capacity) >= 0;

        return new WorldResource(
            in sample, isCapped: true, headroom, fillFraction, isAtCapacity, trueQuantity, trueRate);
    }

    internal static WorldStructure Derive(in RawStructureSample sample)
    {
        var level = sample.Level;
        var queued = IsNegative(sample.QueuedLevels) ? BigDouble.Zero : sample.QueuedLevels;

        // Every kind of granted level, summed. The purchase level deliberately excludes all of them,
        // so a consumer asking "how strong is this" and one asking "what does the next one cost" need
        // different numbers and would otherwise both reach for Level.
        var effectiveLevel = level
            + new BigDouble(sample.SelfBonusLevels)
            + sample.Modifiers.BonusLevels
            + sample.Modifiers.EffectLevels;

        return new WorldStructure(
            in sample,
            committedLevel: level + queued,
            hasWorkInFlight: IsPositive(queued),
            effectiveLevel,
            developmentProgress: Progress(
                remaining: sample.QueueTimeLeft,
                total: sample.CurrentBuildTime));
    }

    internal static WorldPlotNode Derive(in RawPlotNodeSample sample) =>
        new(
            in sample,
            RemainingQuantity(
                sample.IdleQuantity,
                sample.TotalQuantity,
                sample.ActionQuantityUsageMain.ToInt(),
                sample.ActionQuantityUsageAny.ToInt()),
            RemainingTotalQuantity(
                sample.TotalQuantity,
                sample.ActionQuantityUsageMain.ToInt(),
                sample.ActionQuantityUsageAny.ToInt()));

    /// <summary>
    /// Ported from <c>PlotNodeSO.GetRemainingQuantity()</c> — how many of a node an action may still
    /// be started on.
    /// </summary>
    /// <remarks>
    /// The two usage terms are not symmetric, and the asymmetry is the original's: <c>main</c> comes
    /// straight off the idle count, while <c>any</c> is absorbed first by whatever is busy and only
    /// bites the idle count once that runs out. The result is allowed to go negative, also as
    /// written — the game's own callers compare it against zero rather than clamping it, and clamping
    /// here would quietly disagree with them.
    /// <para>
    /// Named apart from <see cref="Derive(in RawPlotNodeSample)"/> so the differential pass can run
    /// the published expression against <c>PlotNodeSO.GetRemainingQuantity()</c> on inputs it read
    /// itself. A verifier that had to build a whole sample to reach this would be comparing a
    /// second transcription rather than the one that ships.
    /// </para>
    /// </remarks>
    internal static int RemainingQuantity(int idle, int total, int usageMain, int usageAny) =>
        idle - usageMain - Math.Max(usageAny - (total - idle), 0);

    /// <summary>
    /// Ported from <c>PlotNodeSO.GetRemainingTotalQuantity()</c> — the same question asked of every
    /// phase, with both usage terms coming off the total flat.
    /// </summary>
    internal static int RemainingTotalQuantity(int total, int usageMain, int usageAny) =>
        total - usageAny - usageMain;

    internal static WorldUpgrade Derive(in RawUpgradeSample sample)
    {
        var isBounded = sample.MaxLevel > 0;
        var remaining = isBounded ? Math.Max(0, sample.MaxLevel - sample.Level) : 0;

        // The game's own test, verbatim: IsDeveloping() => queuedLevels > 0.
        var isDeveloping = sample.QueuedLevels > 0;

        return new WorldUpgrade(
            in sample,
            isBounded,
            isExhausted: isBounded && sample.Level >= sample.MaxLevel,
            remainingLevels: remaining,
            committedLevel: sample.Level + sample.QueuedLevels,
            isDeveloping,
            developmentProgress: Progress(
                remaining: sample.BuildTime,
                total: new BigDouble(sample.DevelopmentTime)));
    }

    /// <summary>
    /// Projects a captured reading onto the ported rate chain's inputs.
    /// </summary>
    /// <remarks>
    /// A one-to-one restatement, not a calculation: every term is a value the collector already read.
    /// The <c>*HasActive</c> flags are the game's <c>HasActiveElements()</c>, captured as a modifier
    /// count because the chain branches on whether a term participates at all rather than on whether
    /// it contributes zero — and those are different answers.
    /// </remarks>
    private static GameResourceRateInputs RateInputs(
        in RawResourceSample sample,
        in WorldFrameGlobals globals)
    {
        var rates = sample.RateInputs;
        return new GameResourceRateInputs
        {
            Rate = rates.Rate,
            RateSplash = rates.RateSplash,
            RateMaxPercent = rates.RateMaxPercent,
            RateInterestPercent = rates.RateInterestPercent,
            RateMissingPercent = rates.RateMissingPercent,
            RateLifetimePercent = rates.RateLifetimePercent,
            MaxQuantity = sample.Capacity,
            Quality = sample.Quality,
            GainRate = sample.GainRate,
            Drain = sample.Drain,
            LossPercent = rates.LossPercent,
            DisplayRate = rates.DisplayRate,
            Quantity = sample.Quantity,
            LifetimeQuantity = sample.LifetimeQuantity,
            CalcRarityValue = rates.CalcRarityValue,
            BaseLoss = rates.BaseLoss,
            Visible = sample.Visible,
            InLossMode = sample.InLossMode,
            RateHasActive = rates.RateModifiers > 0,
            RateSplashHasActive = rates.RateSplashModifiers > 0,
            RateMaxPercentHasActive = rates.RateMaxPercentModifiers > 0,
            RateInterestPercentHasActive = rates.RateInterestPercentModifiers > 0,
            RateMissingPercentHasActive = rates.RateMissingPercentModifiers > 0,
            RateLifetimePercentHasActive = rates.RateLifetimePercentModifiers > 0,
            ResourceOverflowPercent = globals.ResourceOverflowPercent,
            ResourceOverflowLossPercent = globals.ResourceOverflowLossPercent,
            ResetTimePassed = globals.ResetTimePassed,
            FixedDeltaTime = globals.FixedDeltaTime,
        };
    }

    /// <summary>
    /// How far a countdown has come, in <c>[0, 1]</c>, matching the game's own
    /// <c>1 - remaining / total</c>. Both timers the suite reads count down, and both are zero when
    /// idle, so an entity with nothing in flight falls out as zero without needing to be asked
    /// separately whether it is building.
    /// </summary>
    private static double Progress(BigDouble remaining, BigDouble total)
    {
        if (!IsPositive(total) || !IsPositive(remaining)) return 0d;
        return Clamp01(1d - (remaining / total).ToDouble());
    }

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0d : Math.Min(1d, Math.Max(0d, value));

    private static bool IsPositive(BigDouble value) => value.Mantissa > 0.0;

    private static bool IsNonNegative(BigDouble value) => value.Mantissa >= 0.0;

    private static bool IsNegative(BigDouble value) => value.Mantissa < 0.0;
}

/// <summary>
/// The five categories whose reading is not already the row, each as an object the worker can hold
/// without holding the binder that read it.
/// </summary>
/// <remarks>
/// The three that need no cycle state are shared singletons, because a derivation that allocated per
/// cycle would undo the point of deriving off-thread. The two that close over
/// <see cref="WorldFrameGlobals"/> are constructed per cycle instead — see the note on
/// <see cref="WorldResourceDeriver"/>.
/// </remarks>
internal sealed class WorldResourceDeriver : WorldRowDeriver<RawResourceSample, WorldResource>
{
    private readonly WorldFrameGlobals _globals;

    /// <summary>
    /// Constructed per cycle with that cycle's globals rather than shared, so the terms a row was
    /// derived with cannot be mutated by the next collection while a worker is still reading.
    /// </summary>
    internal WorldResourceDeriver(in WorldFrameGlobals globals) => _globals = globals;

    internal override WorldResource Derive(in RawResourceSample sample) =>
        GameWorldStateDeriver.Derive(in sample, in _globals);
}

internal sealed class WorldStructureDeriver : WorldRowDeriver<RawStructureSample, WorldStructure>
{
    internal static readonly WorldStructureDeriver Shared = new();

    private WorldStructureDeriver()
    {
    }

    internal override WorldStructure Derive(in RawStructureSample sample) =>
        GameWorldStateDeriver.Derive(in sample);
}

internal sealed class WorldPlotNodeDeriver : WorldRowDeriver<RawPlotNodeSample, WorldPlotNode>
{
    internal static readonly WorldPlotNodeDeriver Shared = new();

    private WorldPlotNodeDeriver()
    {
    }

    internal override WorldPlotNode Derive(in RawPlotNodeSample sample) =>
        GameWorldStateDeriver.Derive(in sample);
}

internal sealed class WorldUpgradeDeriver : WorldRowDeriver<RawUpgradeSample, WorldUpgrade>
{
    internal static readonly WorldUpgradeDeriver Shared = new();

    private WorldUpgradeDeriver()
    {
    }

    internal override WorldUpgrade Derive(in RawUpgradeSample sample) =>
        GameWorldStateDeriver.Derive(in sample);
}

/// <summary>
/// The element-owned resource. Derives the resource exactly as a registered one, then re-attaches the
/// element it belongs to — the reading is a resource plus an owner, and only the resource half has
/// anything to derive.
/// </summary>
internal sealed class WorldHarvestResourceDeriver :
    WorldRowDeriver<RawHarvestResourceSample, WorldHarvestResource>
{
    private readonly WorldFrameGlobals _globals;

    internal WorldHarvestResourceDeriver(in WorldFrameGlobals globals) => _globals = globals;

    internal override WorldHarvestResource Derive(in RawHarvestResourceSample sample)
    {
        var reading = sample.Resource;
        var resource = GameWorldStateDeriver.Derive(in reading, in _globals);
        return new WorldHarvestResource(sample.ElementId, in resource);
    }
}

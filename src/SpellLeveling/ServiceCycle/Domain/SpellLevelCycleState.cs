using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// The Spell Leveling worker's per-lifecycle state: the lifecycle it was minted under and the last
/// cycle's decision metrics.
/// </summary>
/// <remarks>
/// No control state, and no capability latch. A latch is for remembering what was last read from the
/// game; a worker is handed a fresh world every cycle and derives the same answer from it, so
/// remembering would only let the two disagree.
/// </remarks>
internal struct SpellLevelCycleState
{
    private SpellLevelCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        Decision = default;
    }

    public LifecycleGeneration Lifecycle { get; private set; }

    public SpellLevelDecisionMetrics Decision { get; private set; }

    public static SpellLevelCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    public void RecordDecision(SpellLevelDecisionMetrics decision) => Decision = decision;
}

/// <summary>
/// Why a discovered spell did not reach the plan, one member per term the evaluator tests, in order.
/// </summary>
/// <remarks>
/// Spell Leveling's plan is one action or none, so "nothing happened" is its ordinary output and the
/// interesting question is always which term produced it. Auto Buy learned this the expensive way —
/// four hundred cycles of "0 eligible of 409" that took a decompile to attribute — and a service whose
/// normal state is idle needs the same answer even more.
/// </remarks>
internal enum SpellLevelExclusion
{
    /// <summary>Not excluded — the spell reached the plan.</summary>
    None = 0,

    /// <summary>The snapshot does not show the spell as discovered.</summary>
    Undiscovered = 1,

    /// <summary>Discovered, but the mastery track has not banked the next level's experience.</summary>
    NotReady = 2,

    /// <summary>Ready, but the published native level cost is not currently covered.</summary>
    Unaffordable = 3,

    /// <summary>Ready, but another ready spell outranked it. Exactly one action is planned per cycle.</summary>
    Outranked = 4,

    /// <summary>Ready and affordable, but backing off on the retry gate after its last level faulted.</summary>
    BackingOff = 5,
}

/// <summary>How many spells each exclusion term accounted for in one cycle.</summary>
/// <remarks>
/// A fixed-width value rather than a dictionary, because it rides in a struct the worker hands back
/// once per cycle and the framework's validator forbids a worker definition holding collections.
/// </remarks>
internal readonly struct SpellLevelExclusionHistogram
{
    internal SpellLevelExclusionHistogram(
        int undiscovered,
        int notReady,
        int unaffordable,
        int outranked,
        int backingOff)
    {
        Undiscovered = undiscovered;
        NotReady = notReady;
        Unaffordable = unaffordable;
        Outranked = outranked;
        BackingOff = backingOff;
    }

    public int Undiscovered { get; }
    public int NotReady { get; }
    public int Unaffordable { get; }
    public int Outranked { get; }
    public int BackingOff { get; }

    public int Total => Undiscovered + NotReady + Unaffordable + Outranked + BackingOff;

    public int For(SpellLevelExclusion exclusion) => exclusion switch
    {
        SpellLevelExclusion.Undiscovered => Undiscovered,
        SpellLevelExclusion.NotReady => NotReady,
        SpellLevelExclusion.Unaffordable => Unaffordable,
        SpellLevelExclusion.Outranked => Outranked,
        SpellLevelExclusion.BackingOff => BackingOff,
        _ => 0,
    };
}

internal readonly struct SpellLevelDecisionMetrics
{
    internal SpellLevelDecisionMetrics(
        int capturedSpells,
        int readySpells,
        int plannedActions,
        AutoSpellLevelCapability capability)
        : this(capturedSpells, readySpells, plannedActions, capability, default)
    {
    }

    internal SpellLevelDecisionMetrics(
        int capturedSpells,
        int readySpells,
        int plannedActions,
        AutoSpellLevelCapability capability,
        in SpellLevelExclusionHistogram exclusions)
    {
        if (capturedSpells < 0) throw new System.ArgumentOutOfRangeException(nameof(capturedSpells));
        if (readySpells < 0) throw new System.ArgumentOutOfRangeException(nameof(readySpells));
        if (plannedActions < 0) throw new System.ArgumentOutOfRangeException(nameof(plannedActions));
        CapturedSpells = capturedSpells;
        ReadySpells = readySpells;
        PlannedActions = plannedActions;
        Capability = capability;
        Exclusions = exclusions;
    }

    public int CapturedSpells { get; }
    public int ReadySpells { get; }
    public int PlannedActions { get; }

    /// <summary>
    /// What the snapshot says this cycle could do. The worker never answers
    /// <see cref="AutoSpellLevelCapability.Locked"/>: whether leveling is unlocked at all is a
    /// prerequisite question, and prerequisites are a boundary fact (W59).
    /// </summary>
    public AutoSpellLevelCapability Capability { get; }

    /// <summary>Why the discovered spells that did not reach the plan did not reach it.</summary>
    public SpellLevelExclusionHistogram Exclusions { get; }
}

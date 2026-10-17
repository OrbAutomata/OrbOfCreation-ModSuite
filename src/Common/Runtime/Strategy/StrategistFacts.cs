using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.Strategy;

internal enum StrategistNodeKind
{
    Upgrade = 1,
    Research = 2,
    Structure = 3,
}

/// <summary>The run's clocks, in the game's own seconds.</summary>
internal readonly struct StrategistClock
{
    internal StrategistClock(
        double totalSeconds,
        bool hasThisReset,
        double thisResetSeconds,
        int activeChallenges)
    {
        TotalSeconds = totalSeconds;
        HasThisReset = hasThisReset;
        ThisResetSeconds = thisResetSeconds;
        ActiveChallenges = activeChallenges;
    }

    internal double TotalSeconds { get; }

    /// <summary>
    /// The game publishes "Time Played this Reset" only once the save has reset, so a save that never
    /// has has one clock, not a zero second one.
    /// </summary>
    internal bool HasThisReset { get; }

    internal double ThisResetSeconds { get; }
    internal int ActiveChallenges { get; }
}

/// <summary>One open screen or one level dial, as Position prints it.</summary>
internal readonly struct StrategistOpenFact
{
    internal StrategistOpenFact(string name, bool hasLevel, int current, int maximum)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        HasLevel = hasLevel;
        Current = current;
        Maximum = maximum;
    }

    internal string Name { get; }
    internal bool HasLevel { get; }
    internal int Current { get; }
    internal int Maximum { get; }
}

internal readonly struct StrategistResourceFact
{
    internal StrategistResourceFact(
        Guid id,
        string name,
        bool visible,
        BigDouble held,
        bool capped,
        BigDouble cap,
        bool bandwidth)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Visible = visible;
        Held = held;
        Capped = capped;
        Cap = cap;
        Bandwidth = bandwidth;
    }

    internal Guid Id { get; }
    internal string Name { get; }
    internal bool Visible { get; }

    /// <summary>The amount the resource bar prints, in the unit prices are printed in.</summary>
    internal BigDouble Held { get; }

    internal bool Capped { get; }
    internal BigDouble Cap { get; }

    /// <summary>
    /// A pool of points allocated rather than spent, like the advancement pools: its prices are
    /// allocations, never a cost in time.
    /// </summary>
    internal bool Bandwidth { get; }
}

/// <summary>A purchasable or a step of the Restoration chain.</summary>
internal readonly struct StrategistNodeFact
{
    internal StrategistNodeFact(
        Guid id,
        string name,
        StrategistNodeKind kind,
        bool offered,
        bool owned,
        bool gate)
    {
        Id = id;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
        Offered = offered;
        Owned = owned;
        Gate = gate;
    }

    internal Guid Id { get; }
    internal string Name { get; }
    internal StrategistNodeKind Kind { get; }

    /// <summary>The game offers its next level right now: available and on an unlocked screen.</summary>
    internal bool Offered { get; }

    /// <summary>Every level is bought or committed.</summary>
    internal bool Owned { get; }

    /// <summary>A one-shot upgrade (maximum level one).</summary>
    internal bool Gate { get; }
}

/// <summary>One cost row of a node's next level, as the purchase screen prints it.</summary>
internal readonly struct StrategistPriceFact
{
    internal StrategistPriceFact(Guid nodeId, Guid resourceId, BigDouble amount)
    {
        NodeId = nodeId;
        ResourceId = resourceId;
        Amount = amount;
    }

    internal Guid NodeId { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
}

/// <summary>A requirement edge: the owner needs the target.</summary>
internal readonly struct StrategistEdgeFact
{
    internal StrategistEdgeFact(Guid ownerId, Guid targetId, bool met)
    {
        OwnerId = ownerId;
        TargetId = targetId;
        Met = met;
    }

    internal Guid OwnerId { get; }
    internal Guid TargetId { get; }
    internal bool Met { get; }
}

/// <summary>
/// What the strategist read off one published world: every fact its rules use, and nothing else.
/// </summary>
/// <remarks>
/// Names travel with the facts so the bulletin and its fixtures render without a world. Edges are the
/// requirement graph's part that matters: the root's own requirements, every unmet requirement behind
/// them, and the edges that name an offered gate.
/// </remarks>
internal sealed class StrategistFacts
{
    internal StrategistFacts(
        ulong worldGeneration,
        StrategistClock clock,
        Guid chainRoot,
        PublicationTable<StrategistOpenFact> open,
        PublicationTable<StrategistResourceFact> resources,
        PublicationTable<StrategistNodeFact> nodes,
        PublicationTable<StrategistPriceFact> prices,
        PublicationTable<StrategistEdgeFact> edges)
    {
        WorldGeneration = worldGeneration;
        Clock = clock;
        ChainRoot = chainRoot;
        Open = open ?? throw new ArgumentNullException(nameof(open));
        Resources = resources ?? throw new ArgumentNullException(nameof(resources));
        Nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
        Prices = prices ?? throw new ArgumentNullException(nameof(prices));
        Edges = edges ?? throw new ArgumentNullException(nameof(edges));
    }

    internal ulong WorldGeneration { get; }
    internal StrategistClock Clock { get; }
    internal Guid ChainRoot { get; }
    internal PublicationTable<StrategistOpenFact> Open { get; }
    internal PublicationTable<StrategistResourceFact> Resources { get; }
    internal PublicationTable<StrategistNodeFact> Nodes { get; }
    internal PublicationTable<StrategistPriceFact> Prices { get; }
    internal PublicationTable<StrategistEdgeFact> Edges { get; }
}

/// <summary>One resource's gross gain across the sustained-income window.</summary>
internal readonly struct StrategistWindowRow
{
    internal StrategistWindowRow(
        Guid resourceId,
        BigDouble gained,
        double spanSeconds,
        bool clippedAtCap)
    {
        ResourceId = resourceId;
        Gained = gained;
        SpanSeconds = spanSeconds;
        ClippedAtCap = clippedAtCap;
    }

    internal Guid ResourceId { get; }

    /// <summary>Lifetime quantity gained across the span: gross gain, bursts included, splash not.</summary>
    internal BigDouble Gained { get; }

    internal double SpanSeconds { get; }

    /// <summary>
    /// The resource sat at its cap somewhere in the span, where the game stops counting gain, so the
    /// income is at least this much.
    /// </summary>
    internal bool ClippedAtCap { get; }
}

/// <summary>The sustained-income window as one cycle reads it.</summary>
internal sealed class StrategistWindowReading
{
    internal StrategistWindowReading(
        double requiredSeconds,
        PublicationTable<StrategistWindowRow> rows)
    {
        RequiredSeconds = requiredSeconds;
        Rows = rows ?? throw new ArgumentNullException(nameof(rows));
    }

    /// <summary>One full cycle of the slowest running producer, and never under a minute.</summary>
    internal double RequiredSeconds { get; }

    internal PublicationTable<StrategistWindowRow> Rows { get; }
}

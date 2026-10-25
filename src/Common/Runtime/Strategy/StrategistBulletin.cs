using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.Strategy;

/// <summary>
/// What one price costs, measured on sustained income: <c>trivial</c> under ten seconds,
/// <c>cheap</c> under a minute, <c>notable</c> under an hour, <c>expensive</c> beyond.
/// </summary>
internal enum StrategistCostTier
{
    Trivial = 1,
    Cheap = 2,
    Notable = 3,
    Expensive = 4,

    /// <summary>Above the cap, or short with no income: no amount of waiting buys it.</summary>
    OutOfReach = 5,

    /// <summary>
    /// No income to divide by yet: the window has not covered a full producer cycle, or it sat at the
    /// cap throughout and a price has since fallen short.
    /// </summary>
    Measuring = 6,

    /// <summary>A point pool: the price is allocated, not earned back over time.</summary>
    Allocation = 7,
}

internal enum StrategistOutOfReach
{
    None = 0,
    AboveCap = 1,
    NoIncome = 2,
}

/// <summary>Where a short-term goal stands against the long-term goals. Ordered: lower ranks first.</summary>
internal enum StrategistGoalRank
{
    /// <summary>Related by captured prerequisite leaves; alternatives and unknowns are not proof of necessity.</summary>
    Chain = 1,

    Other = 2,
}

internal readonly struct StrategistIncome
{
    internal StrategistIncome(
        Guid resourceId,
        bool measured,
        BigDouble perSecond,
        bool lowerBound,
        double coveredSeconds,
        double requiredSeconds)
    {
        ResourceId = resourceId;
        Measured = measured;
        PerSecond = perSecond;
        LowerBound = lowerBound;
        CoveredSeconds = coveredSeconds;
        RequiredSeconds = requiredSeconds;
    }

    internal Guid ResourceId { get; }
    internal bool Measured { get; }
    internal BigDouble PerSecond { get; }
    internal bool LowerBound { get; }
    internal double CoveredSeconds { get; }
    internal double RequiredSeconds { get; }
}

/// <summary>One cost row, judged.</summary>
internal readonly struct StrategistPrice
{
    internal StrategistPrice(
        Guid nodeId,
        Guid resourceId,
        BigDouble amount,
        BigDouble held,
        StrategistCostTier tier,
        StrategistOutOfReach outOfReach,
        BigDouble etaSeconds)
    {
        NodeId = nodeId;
        ResourceId = resourceId;
        Amount = amount;
        Held = held;
        Tier = tier;
        OutOfReach = outOfReach;
        EtaSeconds = etaSeconds;
    }

    internal Guid NodeId { get; }
    internal Guid ResourceId { get; }
    internal BigDouble Amount { get; }
    internal BigDouble Held { get; }
    internal StrategistCostTier Tier { get; }
    internal StrategistOutOfReach OutOfReach { get; }

    /// <summary>(price − held) ÷ sustained income, never below zero; meaningful only on a timed tier.</summary>
    internal BigDouble EtaSeconds { get; }

    internal bool IsShort => Held < Amount;
}

internal readonly struct StrategistGoal
{
    internal StrategistGoal(
        Guid gateId,
        StrategistGoalRank rank,
        int binder,
        PublicationTable<StrategistPrice> costs,
        PublicationTable<Guid> opens)
    {
        GateId = gateId;
        Rank = rank;
        Binder = binder;
        Costs = costs ?? throw new ArgumentNullException(nameof(costs));
        Opens = opens ?? throw new ArgumentNullException(nameof(opens));
    }

    internal Guid GateId { get; }
    internal StrategistGoalRank Rank { get; }

    /// <summary>The slowest cost row's index in <see cref="Costs"/>, or −1 for a gate that costs nothing.</summary>
    internal int Binder { get; }

    internal PublicationTable<StrategistPrice> Costs { get; }

    /// <summary>What names this gate as a requirement.</summary>
    internal PublicationTable<Guid> Opens { get; }
}

/// <summary>Restoration and the offered steps related by captured prerequisite leaves.</summary>
internal readonly struct StrategistRestoration
{
    internal StrategistRestoration(
        bool published,
        bool finished,
        PublicationTable<Guid> requires,
        PublicationTable<Guid> frontier)
    {
        Published = published;
        Finished = finished;
        Requires = requires ?? throw new ArgumentNullException(nameof(requires));
        Frontier = frontier ?? throw new ArgumentNullException(nameof(frontier));
    }

    internal bool Published { get; }
    internal bool Finished { get; }

    /// <summary>Restoration's own requirements.</summary>
    internal PublicationTable<Guid> Requires { get; }

    /// <summary>Offered steps related to Restoration by captured prerequisite leaves.</summary>
    internal PublicationTable<Guid> Frontier { get; }
}

/// <summary>One currency and every offered purchase priced in it, gates first.</summary>
internal readonly struct StrategistDemand
{
    internal StrategistDemand(Guid resourceId, PublicationTable<StrategistPrice> sinks)
    {
        ResourceId = resourceId;
        Sinks = sinks ?? throw new ArgumentNullException(nameof(sinks));
    }

    internal Guid ResourceId { get; }
    internal PublicationTable<StrategistPrice> Sinks { get; }
}

/// <summary>
/// The strategist's judgment of one published world, together with the exact facts it judged.
/// </summary>
/// <remarks>
/// Every judgment is data — tiers, ranks, binders — so prose is rendered from it and a fixture asserts
/// the judgment, never the wording. Short-term goals are ordered: the first is the goal the rest of
/// the bulletin is measured against.
/// </remarks>
internal sealed class StrategistBulletin
{
    internal StrategistBulletin(
        StrategistFacts facts,
        StrategistWindowReading window,
        PublicationTable<StrategistIncome> incomes,
        StrategistRestoration restoration,
        PublicationTable<StrategistGoal> goals,
        PublicationTable<StrategistDemand> demand,
        StrategistAdvice advice)
    {
        Facts = facts ?? throw new ArgumentNullException(nameof(facts));
        Window = window ?? throw new ArgumentNullException(nameof(window));
        Incomes = incomes ?? throw new ArgumentNullException(nameof(incomes));
        Restoration = restoration;
        Goals = goals ?? throw new ArgumentNullException(nameof(goals));
        Demand = demand ?? throw new ArgumentNullException(nameof(demand));
        Advice = advice ?? throw new ArgumentNullException(nameof(advice));
    }

    internal StrategistAdvice Advice { get; }
    internal StrategistFacts Facts { get; }
    internal StrategistWindowReading Window { get; }
    internal PublicationTable<StrategistIncome> Incomes { get; }
    internal StrategistRestoration Restoration { get; }
    internal PublicationTable<StrategistGoal> Goals { get; }
    internal PublicationTable<StrategistDemand> Demand { get; }
}

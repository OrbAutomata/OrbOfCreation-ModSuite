using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbModding.Common.Runtime.Strategy;

/// <summary>The next step suggested by the captured goal's costs, not permission to act.</summary>
internal enum StrategistAdviceKind
{
    NoGoal = 0,
    Ready = 1,
    SaveResources = 2,
    IncreaseCapacity = 3,
    ObtainIncome = 4,
    MeasureIncome = 5,
    AllocatePoints = 6,
}

/// <summary>One goal price and the precise reason it is ready or still short.</summary>
internal readonly struct StrategistAdviceCost
{
    internal StrategistAdviceCost(StrategistPrice price, StrategistAdviceKind kind, BigDouble capacity)
    {
        Price = price;
        Kind = kind;
        Capacity = capacity;
    }

    internal StrategistPrice Price { get; }
    internal StrategistAdviceKind Kind { get; }
    internal Guid ResourceId => Price.ResourceId;
    internal BigDouble Missing => Price.IsShort ? Price.Amount - Price.Held : BigDouble.Zero;
    /// <summary>Captured storage capacity; meaningful when Kind is IncreaseCapacity.</summary>
    internal BigDouble Capacity { get; }
}

/// <summary>
/// An explanation of the first ranked goal from one captured world. It establishes no resource
/// reservation and never authorizes a native purchase. Income-relative times assume other spending
/// stops; every automation still owns its current admission and native validation.
/// </summary>
internal sealed class StrategistAdvice
{
    internal StrategistAdvice(
        Guid goalId,
        StrategistAdviceKind kind,
        Guid bottleneckResourceId,
        PublicationTable<StrategistAdviceCost> costs)
    {
        GoalId = goalId;
        Kind = kind;
        BottleneckResourceId = bottleneckResourceId;
        Costs = costs ?? throw new ArgumentNullException(nameof(costs));
    }

    internal Guid GoalId { get; }
    internal StrategistAdviceKind Kind { get; }
    internal Guid BottleneckResourceId { get; }
    internal PublicationTable<StrategistAdviceCost> Costs { get; }
}

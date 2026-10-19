using System;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.World;
#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>
/// Which cycle this is, and which reading of each shared publication it ran against.
/// </summary>
/// <remarks>
/// The world generation is here for the same reason the configuration and strategy generations are:
/// a cycle pins one reading of each when it starts, and everything the cycle produces is only
/// interpretable against those readings. Naming the world makes a decision answerable after the fact
/// — which collection did this act on — where before, the world was the one pinned input a cycle
/// could not say it had used. Schema v7 carries it on the trace wire alongside the other three.
///
/// There is no capture sequence here. It counted captures rather than cycles, but moved in lockstep
/// with the cycle id, so it only ever restated what the cycle id already said. It survives on
/// <see cref="ServiceCaptureContext"/>, where the capture is the thing being identified.
/// </remarks>
public readonly struct ServiceCycleIdentity : IEquatable<ServiceCycleIdentity>
{
    public ServiceCycleIdentity(
        ServiceId service,
        LifecycleGeneration lifecycle,
        ConfigGeneration config,
        StrategyGeneration strategy,
        WorldGeneration world,
        CycleId cycle)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (!config.IsValid) throw new ArgumentException("A valid configuration generation is required.", nameof(config));
        if (strategy.Value == 0) throw new ArgumentException("A valid strategy generation is required.", nameof(strategy));
        if (!world.IsValid) throw new ArgumentException("A valid world generation is required.", nameof(world));
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        Service = service;
        Lifecycle = lifecycle;
        Config = config;
        Strategy = strategy;
        World = world;
        Cycle = cycle;
    }

    public ServiceId Service { get; }
    public LifecycleGeneration Lifecycle { get; }
    public ConfigGeneration Config { get; }
    public StrategyGeneration Strategy { get; }
    public WorldGeneration World { get; }
    public CycleId Cycle { get; }
    public bool IsValid =>
        Service.IsValid && Lifecycle.Value != 0 && Config.IsValid && Strategy.Value != 0 &&
        World.IsValid && Cycle.IsValid;
    public bool Equals(ServiceCycleIdentity other) =>
        Service == other.Service &&
        Lifecycle == other.Lifecycle &&
        Config == other.Config &&
        Strategy == other.Strategy &&
        World == other.World &&
        Cycle == other.Cycle;
    public override bool Equals(object? obj) => obj is ServiceCycleIdentity other && Equals(other);
    public override int GetHashCode() =>
        HashCode.Combine(Service, Lifecycle, Config, Strategy, World, Cycle);
    public static bool operator ==(ServiceCycleIdentity left, ServiceCycleIdentity right) => left.Equals(right);
    public static bool operator !=(ServiceCycleIdentity left, ServiceCycleIdentity right) => !left.Equals(right);
}

public readonly struct ServiceCycleStartContext
{
    public ServiceCycleStartContext(
        LifecycleGeneration lifecycle,
        ConfigGeneration latestConfig,
        BatchReceipt previousReceipt,
        MonotonicTimestamp now)
    {
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (!latestConfig.IsValid) throw new ArgumentException("A valid configuration generation is required.", nameof(latestConfig));
        Lifecycle = lifecycle;
        LatestConfig = latestConfig;
        PreviousReceipt = previousReceipt;
        Now = now;
    }

    public LifecycleGeneration Lifecycle { get; }
    public ConfigGeneration LatestConfig { get; }
    public BatchReceipt PreviousReceipt { get; }
    public MonotonicTimestamp Now { get; }
}

/// <summary>
/// Which cycle a capture belongs to: the whole of <see cref="ServiceCycleIdentity"/>, the world the
/// runtime pinned for it, and when the runtime opened it.
/// </summary>
/// <remarks>
/// <para>
/// The strategy generation is here rather than in the capture's return value because the runtime owns
/// it. A service reads the bulletin its own pinned inputs gave it and has no business naming which
/// publication that was; the one thing that ever needed the number inside <c>Capture</c> was the
/// replay recorder, runtime machinery in a definition's clothing, and it is gone. See W49.
/// </para>
/// <para>
/// <see cref="World"/> is here for the same reason. It is the same snapshot the worker's frame
/// projection is handed, pinned once when the cycle started, so the two halves of a cycle cannot
/// disagree about what the game looked like. A service that reached a publisher instead could read it
/// twice and still compile. See W50.
/// </para>
/// </remarks>
public readonly struct ServiceCaptureContext
{
    public ServiceCaptureContext(
        ServiceId service,
        LifecycleGeneration lifecycle,
        ConfigGeneration config,
        StrategyGeneration strategy,
        CaptureSequence capture,
        CycleId cycle,
        GameWorldState world,
        MonotonicTimestamp capturedAt)
    {
        if (!service.IsValid) throw new ArgumentException("A valid service identity is required.", nameof(service));
        if (lifecycle.Value == 0) throw new ArgumentException("A valid lifecycle generation is required.", nameof(lifecycle));
        if (!config.IsValid) throw new ArgumentException("A valid configuration generation is required.", nameof(config));
        if (strategy.Value == 0) throw new ArgumentException("A valid strategy generation is required.", nameof(strategy));
        if (!capture.IsValid) throw new ArgumentException("A valid capture sequence is required.", nameof(capture));
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        Service = service;
        Lifecycle = lifecycle;
        Config = config;
        Strategy = strategy;
        Capture = capture;
        Cycle = cycle;
        World = world ?? throw new ArgumentNullException(nameof(world));
        CapturedAt = capturedAt;
    }

    public ServiceId Service { get; }
    public LifecycleGeneration Lifecycle { get; }
    public ConfigGeneration Config { get; }
    public StrategyGeneration Strategy { get; }
    public CaptureSequence Capture { get; }
    public CycleId Cycle { get; }

    /// <summary>The world this cycle runs against. Never null; an uncollected game is the empty world.</summary>
    public GameWorldState World { get; }

    public MonotonicTimestamp CapturedAt { get; }
}

public readonly struct ServiceCycleContext
{
    private readonly ServiceRetryGateSnapshot? _retryGate;

    public ServiceCycleContext(
        ServiceCycleIdentity identity,
        BatchReceipt previousReceipt,
        MonotonicTimestamp decisionAt,
        ServiceRetryGateSnapshot? retryGate = null)
    {
        if (!identity.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(identity));
        Identity = identity;
        PreviousReceipt = previousReceipt;
        DecisionAt = decisionAt;
        _retryGate = retryGate;
    }

    public ServiceCycleIdentity Identity { get; }
    public BatchReceipt PreviousReceipt { get; }
    public MonotonicTimestamp DecisionAt { get; }

    /// <summary>The targets backing off when this cycle opened; planning skips one until it is due.</summary>
    public ServiceRetryGateSnapshot RetryGate => _retryGate ?? ServiceRetryGateSnapshot.Empty;
}

public readonly struct ServiceActionContext
{
    public ServiceActionContext(
        ServiceCycleIdentity cycle,
        BatchId batch,
        ActionId action,
        int actionIndex,
        MonotonicTimestamp attemptedAt)
    {
        if (!cycle.IsValid) throw new ArgumentException("A valid cycle identity is required.", nameof(cycle));
        if (!batch.IsValid) throw new ArgumentException("A valid batch identity is required.", nameof(batch));
        if (!action.IsValid) throw new ArgumentException("A valid action identity is required.", nameof(action));
        if (actionIndex < 0) throw new ArgumentOutOfRangeException(nameof(actionIndex));
        Cycle = cycle;
        Batch = batch;
        Action = action;
        ActionIndex = actionIndex;
        AttemptedAt = attemptedAt;
#if SERVICE_CYCLE_PROFILE
        ProfileCoordinates = default;
#endif
    }

#if SERVICE_CYCLE_PROFILE
    internal ServiceActionContext(
        ServiceCycleIdentity cycle,
        BatchId batch,
        ActionId action,
        int actionIndex,
        MonotonicTimestamp attemptedAt,
        in ServiceCycleProfileCoordinates profileCoordinates)
        : this(cycle, batch, action, actionIndex, attemptedAt)
    {
        ProfileCoordinates = profileCoordinates;
    }
#endif

    public ServiceCycleIdentity Cycle { get; }
    public BatchId Batch { get; }
    public ActionId Action { get; }
    public int ActionIndex { get; }
    public MonotonicTimestamp AttemptedAt { get; }
#if SERVICE_CYCLE_PROFILE
    internal ServiceCycleProfileCoordinates ProfileCoordinates { get; }
#endif
}

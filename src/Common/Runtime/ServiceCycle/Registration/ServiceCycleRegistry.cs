using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using RuntimeLifecycleGeneration = OrbModding.Common.Runtime.LifecycleGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Registration;

/// <summary>Owner-thread, fixed-capacity, explicit service-cycle composition registry.</summary>
public sealed partial class ServiceCycleRegistry : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly IServiceCycleSlot[] _slots;
    private readonly Dictionary<ServiceId, IServiceCycleSlot> _byServiceId;
    private readonly IMonotonicClock _clock;
    private readonly bool _measureWorkerAllocations;
    private readonly IServiceCycleWorkerStarter? _workerStarter;
    private readonly IServiceCycleWorkerExitObserver? _workerExitObserver;
    private readonly ServiceResourceClaimLedger _resourceClaims;
    private readonly ServiceWorldPublisher<GameWorldState> _world =
        new(GameWorldStateDefaults.Empty);
    private readonly ServiceConfigurationPublisher _configuration;
    private readonly ServiceStrategyPublisher _strategy =
        new(SuiteStrategyDefaults.Neutral);
    private int _activeCount;
    private int _nextOrdinal;
    private long _nextRegistrationToken;
    private long _nextLifecycleReconciliationEpoch;
    private bool _sealed;
    private bool _pumpClaimed;
    private int _pumpCallbackDepth;
    private bool _disposed;
    private bool _reconcilingLifecycle;
    private bool _constructingRunner;
    private RuntimeLifecycleGeneration _lifecycle;
    private bool _hasLifecycle;

    public ServiceCycleRegistry(int capacity, IMonotonicClock? clock = null)
        : this(capacity, clock, false) { }

    public ServiceCycleRegistry(
        int capacity,
        RuntimeLifecycleGeneration lifecycle,
        IMonotonicClock? clock = null)
        : this(capacity, clock, false)
    {
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid initial lifecycle generation is required.", nameof(lifecycle));
        _lifecycle = lifecycle;
        _hasLifecycle = true;
    }

    internal ServiceCycleRegistry(
        int capacity,
        SuiteRuntimeConfiguration initialConfiguration,
        ConfigGeneration initialConfigurationGeneration,
        RuntimeLifecycleGeneration lifecycle,
        IMonotonicClock? clock = null)
        : this(
            capacity,
            clock,
            false,
            initialConfiguration: initialConfiguration,
            initialConfigurationGeneration: initialConfigurationGeneration)
    {
        if (lifecycle.Value == 0)
            throw new ArgumentException("A valid initial lifecycle generation is required.", nameof(lifecycle));
        _lifecycle = lifecycle;
        _hasLifecycle = true;
    }

    internal ServiceCycleRegistry(
        int capacity,
        IMonotonicClock? clock,
        bool measureWorkerAllocations,
        IServiceCycleWorkerStarter? workerStarter = null,
        IServiceCycleWorkerExitObserver? workerExitObserver = null,
        SuiteRuntimeConfiguration? initialConfiguration = null,
        ConfigGeneration? initialConfigurationGeneration = null)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        _clock = clock ?? new StopwatchMonotonicClock();
        _measureWorkerAllocations = measureWorkerAllocations;
        _workerStarter = workerStarter;
        _workerExitObserver = workerExitObserver;
        _slots = new IServiceCycleSlot[capacity];
        _byServiceId = new Dictionary<ServiceId, IServiceCycleSlot>(capacity);
        _resourceClaims = new ServiceResourceClaimLedger(capacity);
        _configuration = new ServiceConfigurationPublisher(
            initialConfiguration ?? SuiteRuntimeConfigurationDefaults.Empty,
            initialConfigurationGeneration);
    }

    public int Capacity => _slots.Length;
    public int Count => _activeCount;
    public int OrdinalCount => _nextOrdinal;
    public bool IsSealed => _sealed;
    public RuntimeLifecycleGeneration CurrentLifecycle => _lifecycle;
    internal IMonotonicClock Clock => _clock;

    /// <summary>
    /// The one world publication the suite has. Written by whichever service collects the game, read
    /// by every service the registry hands a cycle to.
    /// </summary>
    /// <remarks>
    /// The registry owns it because there is one game and therefore one world, and because a service
    /// must not be able to reach a publisher of its own — a worker holding one could read it twice in
    /// a cycle and evaluate against one snapshot while acting against another. The runtime pins it
    /// once at cycle start and hands the worker the immutable snapshot, exactly as it does with
    /// configuration. It used to be constructed and owned by OrbAutomata, which made the game's world
    /// one feature's property. See W50.
    /// </remarks>
    internal ServiceWorldPublisher<GameWorldState> World => _world;

    /// <summary>
    /// The write half, for whichever service collects the game. It is the only part of the world
    /// publication a service can reach: reading is the runtime's job, once per cycle.
    /// </summary>
    public IServiceWorldPublicationSink<GameWorldState> WorldPublication => _world;

    /// <summary>
    /// Read-only generation seam for action diagnostics. It exposes no snapshot and therefore cannot
    /// become a second world-reading path.
    /// </summary>
    internal IServiceWorldGenerationSource WorldGenerations => _world;

    /// <summary>
    /// The one configuration publication the suite has. Read by the runtime, which pins it once per
    /// cycle; never handed to a service.
    /// </summary>
    /// <remarks>
    /// Constructed with the registry rather than installed into it, exactly as the world is. There
    /// is one suite and therefore one shape of settings, so a type parameter here could only ever be
    /// closed one way — and an installation step meant a registry could be asked for a configuration
    /// it did not have yet. General-purpose registries start on the all-defaults snapshot; the
    /// application composition seeds the saved snapshot and its existing generation directly.
    /// </remarks>
    internal ServiceConfigurationPublisher Configuration => _configuration;

    /// <summary>
    /// The write half, for whoever reads the settings file. It is the only part of the configuration
    /// publication anything outside the runtime can reach: reading is the runtime's job, once per
    /// cycle.
    /// </summary>
    public IServiceConfigurationPublicationSink ConfigurationPublication =>
        _configuration;

    /// <summary>What the published configuration says about the emergency stop.</summary>
    /// <remarks>
    /// Read every frame by the pump rather than pushed to it, so the flag a user sets and the state
    /// the pump is in cannot drift: there is one answer, and it is whatever the published snapshot
    /// says.
    /// </remarks>
    internal bool ConfiguredEmergencyDisable =>
        _configuration.ReadLatest().Snapshot.Safety.EmergencyDisable;

    /// <summary>
    /// The one strategy publication the suite has. Read by the runtime, which pins it once per cycle;
    /// never handed to a service.
    /// </summary>
    /// <remarks>
    /// Constructed with the registry on the same terms as the configuration, and for the same reason:
    /// there is one suite and therefore one bulletin, so a type parameter here could only ever be
    /// closed one way and an installation step meant a registry could be asked for a strategy it did
    /// not have yet. It starts on the neutral bulletin, which constrains nothing — a suite with no
    /// strategist reads that bulletin forever and behaves as it did before strategy was delivered.
    /// </remarks>
    internal ServiceStrategyPublisher Strategy => _strategy;

    /// <summary>
    /// The write half, for whatever publishes stances. It is the only part of the strategy publication
    /// anything outside the runtime can reach: reading is the runtime's job, once per cycle.
    /// </summary>
    public IServiceStrategyPublicationSink StrategyPublication => _strategy;

    /// <summary>The write half of the strategist's latest bulletin, for the strategist.</summary>
    internal IStrategistBulletinSink StrategistBulletinPublication => _strategy;

    internal long LifecyclePositionTransitionCount
    {
        get
        {
            long total = 0;
            for (var ordinal = 0; ordinal < _nextOrdinal; ordinal++)
                total = checked(total + _slots[ordinal].LifecyclePositionTransitionCount);
            return total;
        }
    }

    private void AssertOwnerThread()
    {
        if (Thread.CurrentThread.ManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Service-cycle registration must remain on its owning main thread.");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ServiceCycleRegistry));
    }

    private void ThrowIfPumpCallback()
    {
        if (_pumpCallbackDepth != 0)
            throw new InvalidOperationException(
                "Service-cycle registration and disposal cannot mutate composition from a pump callback.");
    }

    private void ThrowIfRunnerConstruction()
    {
        if (_reconcilingLifecycle || _constructingRunner)
            throw new InvalidOperationException(
                "Service-cycle composition cannot mutate from a runner construction callback.");
    }
}

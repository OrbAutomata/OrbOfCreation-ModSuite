using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbModding.Common;

[Flags]
public enum GameplayInvalidationKind
{
    None = 0,
    Lifecycle = 1 << 0,
    Progression = 1 << 1,
    ResourceQuantity = 1 << 2,
    ResourceRate = 1 << 3,
    Queue = 1 << 4,
    Inventory = 1 << 5,
    Registry = 1 << 6,
    Configuration = 1 << 7,
    All = Lifecycle | Progression | ResourceQuantity | ResourceRate |
          Queue | Inventory | Registry | Configuration,
}

public static class GameplayInvalidationDomains
{
    public const string AutomataConcepts = "automata.concepts";
    public const string MentorSpells = "mentor.spells";
    public const string MentorArtifacts = "mentor.artifacts";
    public const string MentorAlchemy = "mentor.alchemy";
    public const string MentorSpellLoadout = "mentor.spell-loadout";
    public const string ModConfig = "mod-config";
}

public readonly struct GameplayInvalidationRequest
{
    public GameplayInvalidationRequest(
        GameplayInvalidationKind kinds,
        long lifecycleGeneration,
        long burst,
        string? domain = null,
        string? entityId = null,
        string? expectedTypeName = null,
        string? source = null)
    {
        Kinds = kinds;
        LifecycleGeneration = lifecycleGeneration;
        Burst = burst;
        Domain = domain ?? string.Empty;
        EntityId = entityId ?? string.Empty;
        ExpectedTypeName = expectedTypeName ?? string.Empty;
        Source = source ?? string.Empty;
    }

    public GameplayInvalidationKind Kinds { get; }
    public long LifecycleGeneration { get; }
    public long Burst { get; }
    public string Domain { get; }
    public string EntityId { get; }
    public string ExpectedTypeName { get; }
    public string Source { get; }
}

public readonly struct GameplayInvalidation
{
    internal GameplayInvalidation(
        GameplayInvalidationKind kinds,
        long lifecycleGeneration,
        long burst,
        string domain,
        string entityId,
        string expectedTypeName,
        string source,
        long sequence,
        long lastSequence,
        int coalescedCount)
    {
        Kinds = kinds;
        LifecycleGeneration = lifecycleGeneration;
        Burst = burst;
        Domain = domain;
        EntityId = entityId;
        ExpectedTypeName = expectedTypeName;
        Source = source;
        Sequence = sequence;
        LastSequence = lastSequence;
        CoalescedCount = coalescedCount;
    }

    public GameplayInvalidationKind Kinds { get; }
    public long LifecycleGeneration { get; }
    public long Burst { get; }
    public string Domain { get; }
    public string EntityId { get; }
    public string ExpectedTypeName { get; }
    public string Source { get; }
    public long Sequence { get; }
    internal long LastSequence { get; }
    public int CoalescedCount { get; }
    public bool IsBroad => string.IsNullOrEmpty(Domain) || string.IsNullOrEmpty(EntityId);
}

public readonly struct GameplayInvalidationFilter
{
    public GameplayInvalidationFilter(
        GameplayInvalidationKind kinds,
        string? domain = null,
        string? entityId = null,
        string? expectedTypeName = null)
    {
        Kinds = kinds;
        Domain = domain ?? string.Empty;
        EntityId = entityId ?? string.Empty;
        ExpectedTypeName = expectedTypeName ?? string.Empty;
    }

    public GameplayInvalidationKind Kinds { get; }
    public string Domain { get; }
    public string EntityId { get; }
    public string ExpectedTypeName { get; }

    internal bool Matches(GameplayInvalidation change)
    {
        if ((Kinds & change.Kinds) == GameplayInvalidationKind.None) return false;
        if (!string.IsNullOrEmpty(change.Domain) &&
            !string.IsNullOrEmpty(Domain) &&
            !string.Equals(change.Domain, Domain, StringComparison.Ordinal)) return false;
        if (!string.IsNullOrEmpty(change.ExpectedTypeName) &&
            !string.IsNullOrEmpty(ExpectedTypeName) &&
            !string.Equals(change.ExpectedTypeName, ExpectedTypeName, StringComparison.Ordinal)) return false;
        if (string.IsNullOrEmpty(change.EntityId) || string.IsNullOrEmpty(EntityId)) return true;
        return string.Equals(change.EntityId, EntityId, StringComparison.Ordinal);
    }
}

public readonly struct GameplayInvalidationBusSnapshot
{
    internal GameplayInvalidationBusSnapshot(
        long lifecycleGeneration,
        int capacity,
        int pendingCount,
        int peakPendingCount,
        long published,
        long coalesced,
        long superseded,
        long dispatched,
        long deliveryOperations,
        long staleDiscarded,
        long overflowPromotions,
        long overflowDiscarded,
        long offThreadRejections)
    {
        LifecycleGeneration = lifecycleGeneration;
        Capacity = capacity;
        PendingCount = pendingCount;
        PeakPendingCount = peakPendingCount;
        Published = published;
        Coalesced = coalesced;
        Superseded = superseded;
        Dispatched = dispatched;
        DeliveryOperations = deliveryOperations;
        StaleDiscarded = staleDiscarded;
        OverflowPromotions = overflowPromotions;
        OverflowDiscarded = overflowDiscarded;
        OffThreadRejections = offThreadRejections;
    }

    public long LifecycleGeneration { get; }
    public int Capacity { get; }
    public int PendingCount { get; }
    public int PeakPendingCount { get; }
    public long Published { get; }
    public long Coalesced { get; }
    public long Superseded { get; }
    public long Dispatched { get; }
    public long DeliveryOperations { get; }
    public long StaleDiscarded { get; }
    public long OverflowPromotions { get; }
    public long OverflowDiscarded { get; }
    public long OffThreadRejections { get; }
}

public readonly struct GameplayInvalidationPumpResult
{
    internal GameplayInvalidationPumpResult(
        int operations,
        int completedEvents,
        int pendingCount,
        bool budgetExhausted)
    {
        Operations = operations;
        CompletedEvents = completedEvents;
        PendingCount = pendingCount;
        BudgetExhausted = budgetExhausted;
    }

    public int Operations { get; }
    public int CompletedEvents { get; }
    public int PendingCount { get; }
    public bool BudgetExhausted { get; }
}

/// <summary>
/// Bounded main-thread invalidation delivery. Publishers report stable change evidence;
/// subscribers only mark owned work dirty and perform gameplay reads later under their own budget.
/// </summary>
public sealed class GameplayInvalidationBus : IDisposable
{
    private const int DefaultCapacity = 128;
    public const int DefaultMaxOperationsPerFrame = 64;
    private readonly GameLifecycleMonitor _lifecycle;
    private readonly Func<int> _readThreadId;
    private readonly int _capacity;
    private readonly Queue<InvalidationKey> _order;
    private readonly Dictionary<InvalidationKey, GameplayInvalidation> _pending;
    private readonly List<InvalidationKey> _supersededScratch;
    private readonly HashSet<InvalidationKey> _supersededSet;
    private readonly List<Subscription> _subscriptions = new();
    private int? _ownerThreadId;
    private long _nextSequence;
    private bool _disposed;
    private bool _isPumping;
    private bool _abortCurrentDispatch;
    private bool _hasCurrentDispatch;
    private GameplayInvalidation _currentDispatch;
    private int _currentSubscriberIndex;
    private int _currentSubscriberLimit;
    private long _pumpFrame = -1;
    private long _pumpSequenceLimit;
    private int _frameOperations;
    private int _peakPendingCount;
    private long _published;
    private long _coalesced;
    private long _superseded;
    private long _dispatched;
    private long _deliveryOperations;
    private long _staleDiscarded;
    private long _overflowPromotions;
    private long _overflowDiscarded;
    private Action<string>? _failureLog;
    private long _offThreadRejections;

    public GameplayInvalidationBus(
        GameLifecycleMonitor lifecycle,
        int capacity = DefaultCapacity,
        Func<int>? readThreadId = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
        _readThreadId = readThreadId ?? (() => Environment.CurrentManagedThreadId);
        _order = new Queue<InvalidationKey>(capacity);
        _pending = new Dictionary<InvalidationKey, GameplayInvalidation>(capacity);
        _supersededScratch = new List<InvalidationKey>(capacity);
        _supersededSet = new HashSet<InvalidationKey>(capacity);
        _lifecycle.Transitioned += OnLifecycleTransition;
    }

    public static GameplayInvalidationBus Shared { get; } = new(GameLifecycleMonitor.Shared);

    /// <summary>
    /// Names where a throwing subscriber is reported. A host that never sets it, a test or a tool,
    /// still isolates the throw and reports it nowhere.
    /// </summary>
    public void ConfigureFailureLog(Action<string>? log) => _failureLog = log;

    public IDisposable Subscribe(
        GameplayInvalidationFilter filter,
        Action<GameplayInvalidation> handler,
        string? subscriber = null)
    {
        ThrowIfDisposed();
        EnsureOwnerThread();
        ValidateKinds(filter.Kinds, nameof(filter));
        ValidateTarget(filter.Domain, filter.EntityId, filter.ExpectedTypeName, nameof(filter));
        if (handler is null) throw new ArgumentNullException(nameof(handler));
        var registration = new Subscription(
            this,
            filter,
            handler,
            string.IsNullOrWhiteSpace(subscriber)
                ? handler.Method.DeclaringType?.FullName ?? handler.Method.Name
                : subscriber!);
        _subscriptions.Add(registration);
        return registration;
    }

    public bool Publish(
        GameplayInvalidationKind kinds,
        long burst,
        string? domain = null,
        string? entityId = null,
        string? expectedTypeName = null,
        string? source = null)
    {
        return TryPublish(
            new GameplayInvalidationRequest(
                kinds,
                _lifecycle.Current.Generation,
                burst,
                domain,
                entityId,
                expectedTypeName,
                source),
            out _);
    }

    public bool TryPublish(GameplayInvalidationRequest request, out string reason)
    {
        ThrowIfDisposed();
        EnsureOwnerThread();
        ValidateKinds(request.Kinds, nameof(request));
        ValidateTarget(request.Domain, request.EntityId, request.ExpectedTypeName, nameof(request));
        if (request.Burst < 0) throw new ArgumentOutOfRangeException(nameof(request));
        if (request.LifecycleGeneration != _lifecycle.Current.Generation)
        {
            _staleDiscarded++;
            reason = $"stale invalidation generation; event={request.LifecycleGeneration}; current={_lifecycle.Current.Generation}";
            return false;
        }

        _published++;
        Enqueue(request, checked(++_nextSequence));
        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// Delivers only completed bursts. Multiple installed plugins may call Pump for the same
    /// Unity frame; the shared operation cap and sequence cutoff are applied once process-wide.
    /// </summary>
    public GameplayInvalidationPumpResult Pump(long currentBurstExclusive, int maxOperationsPerFrame)
    {
        ThrowIfDisposed();
        EnsureOwnerThread();
        if (currentBurstExclusive < 0) throw new ArgumentOutOfRangeException(nameof(currentBurstExclusive));
        if (maxOperationsPerFrame <= 0) throw new ArgumentOutOfRangeException(nameof(maxOperationsPerFrame));
        if (_isPumping) throw new InvalidOperationException("Invalidation delivery cannot pump recursively.");
        return PumpCore(currentBurstExclusive, maxOperationsPerFrame);
    }

    private GameplayInvalidationPumpResult PumpCore(
        long currentBurstExclusive,
        int maxOperationsPerFrame)
    {
        if (_pumpFrame != currentBurstExclusive)
        {
            _pumpFrame = currentBurstExclusive;
            _pumpSequenceLimit = _nextSequence;
            _frameOperations = 0;
        }

        var available = Math.Max(0, maxOperationsPerFrame - _frameOperations);
        if (available == 0)
            return new GameplayInvalidationPumpResult(0, 0, PendingCount, PendingCount > 0);

        var operations = 0;
        var completed = 0;
        _isPumping = true;
        try
        {
            while (operations < available)
            {
                if (_abortCurrentDispatch)
                {
                    ClearCurrentDispatch();
                    _abortCurrentDispatch = false;
                }

                if (!_hasCurrentDispatch)
                {
                    if (!TryStartNextDispatch(currentBurstExclusive, _pumpSequenceLimit)) break;
                    operations++;
                    if (_currentDispatch.LifecycleGeneration != _lifecycle.Current.Generation)
                    {
                        _staleDiscarded++;
                        ClearCurrentDispatch();
                        continue;
                    }
                    if (operations >= available) break;
                }

                if (_currentSubscriberIndex >= _currentSubscriberLimit)
                {
                    _dispatched++;
                    completed++;
                    ClearCurrentDispatch();
                    continue;
                }

                var subscription = _subscriptions[_currentSubscriberIndex++];
                operations++;
                if (!subscription.Active || !subscription.Filter.Matches(_currentDispatch)) continue;
                try
                {
                    subscription.Handler(_currentDispatch);
                }
                catch (Exception ex)
                {
                    _failureLog?.Invoke(
                        $"Gameplay invalidation subscriber {subscription.Name} threw handling " +
                        $"{_currentDispatch.Kinds}: {ExceptionDetail.Describe(ex)}");
                }
            }
        }
        finally
        {
            _isPumping = false;
            _frameOperations += operations;
            _deliveryOperations += operations;
            CompactSubscriptions();
        }

        var pendingCount = PendingCount;
        return new GameplayInvalidationPumpResult(
            operations,
            completed,
            pendingCount,
            operations >= available && pendingCount > 0);
    }

    public GameplayInvalidationBusSnapshot GetSnapshot()
    {
        ThrowIfDisposed();
        EnsureOwnerThread();
        return new GameplayInvalidationBusSnapshot(
            _lifecycle.Current.Generation,
            _capacity,
            PendingCount,
            _peakPendingCount,
            _published,
            _coalesced,
            _superseded,
            _dispatched,
            _deliveryOperations,
            _staleDiscarded,
            _overflowPromotions,
            _overflowDiscarded,
            Interlocked.Read(ref _offThreadRejections));
    }

    public void Dispose()
    {
        if (_disposed) return;
        EnsureOwnerThread();
        _disposed = true;
        _lifecycle.Transitioned -= OnLifecycleTransition;
        _order.Clear();
        _pending.Clear();
        _subscriptions.Clear();
        ClearCurrentDispatch();
    }

    private int PendingCount => _pending.Count + (_hasCurrentDispatch ? 1 : 0);

    private void Enqueue(GameplayInvalidationRequest request, long publicationSequence)
    {
        var key = new InvalidationKey(
            request.Burst,
            request.Domain,
            request.EntityId,
            request.ExpectedTypeName);
        if (TryCoalesceIntoDominating(key, request, publicationSequence)) return;
        var kinds = request.Kinds;
        var source = request.Source;
        var coalescedCount = 0;
        var firstSequence = publicationSequence;
        var orderPositionRetained = MergeSuperseded(
            key,
            ref kinds,
            ref source,
            ref coalescedCount,
            ref firstSequence);
        if (PendingCount >= _capacity)
        {
            PromoteOverflow(request, publicationSequence);
            return;
        }

        var change = new GameplayInvalidation(
            kinds,
            request.LifecycleGeneration,
            request.Burst,
            request.Domain,
            request.EntityId,
            request.ExpectedTypeName,
            source,
            firstSequence,
            publicationSequence,
            coalescedCount);
        _pending.Add(key, change);
        if (!orderPositionRetained) _order.Enqueue(key);
        _peakPendingCount = Math.Max(_peakPendingCount, PendingCount);
    }

    private bool TryCoalesceIntoDominating(
        InvalidationKey key,
        GameplayInvalidationRequest request,
        long publicationSequence)
    {
        if (TryCoalesce(key, request, publicationSequence)) return true;
        var global = new InvalidationKey(key.Burst, string.Empty, string.Empty, string.Empty);
        if (TryCoalesce(global, request, publicationSequence)) return true;
        if (!string.IsNullOrEmpty(key.Domain) && key.HasEntity)
        {
            var domain = new InvalidationKey(key.Burst, key.Domain, string.Empty, string.Empty);
            if (TryCoalesce(domain, request, publicationSequence)) return true;
        }
        return false;
    }

    private bool TryCoalesce(
        InvalidationKey key,
        GameplayInvalidationRequest request,
        long publicationSequence)
    {
        if (!_pending.TryGetValue(key, out var existing)) return false;
        _pending[key] = new GameplayInvalidation(
            existing.Kinds | request.Kinds,
            existing.LifecycleGeneration,
            existing.Burst,
            existing.Domain,
            existing.EntityId,
            existing.ExpectedTypeName,
            string.IsNullOrEmpty(request.Source) ? existing.Source : request.Source,
            existing.Sequence,
            publicationSequence,
            checked(existing.CoalescedCount + 1));
        _coalesced++;
        return true;
    }

    private bool MergeSuperseded(
        InvalidationKey key,
        ref GameplayInvalidationKind kinds,
        ref string source,
        ref int coalescedCount,
        ref long firstSequence)
    {
        if (key.HasEntity) return false;
        _supersededScratch.Clear();
        _supersededSet.Clear();
        foreach (var pendingKey in _pending.Keys)
        {
            if (key.Dominates(pendingKey) && !key.Equals(pendingKey))
            {
                _supersededScratch.Add(pendingKey);
                _supersededSet.Add(pendingKey);
            }
        }
        foreach (var superseded in _supersededScratch)
        {
            if (!_pending.TryGetValue(superseded, out var previous) ||
                !_pending.Remove(superseded)) continue;
            kinds |= previous.Kinds;
            if (string.IsNullOrEmpty(source)) source = previous.Source;
            coalescedCount = checked(coalescedCount + previous.CoalescedCount + 1);
            firstSequence = Math.Min(firstSequence, previous.Sequence);
            _superseded++;
        }
        if (_supersededScratch.Count == 0) return false;

        var retained = false;
        var count = _order.Count;
        for (var index = 0; index < count; index++)
        {
            var orderedKey = _order.Dequeue();
            if (_supersededSet.Contains(orderedKey))
            {
                if (retained) continue;
                _order.Enqueue(key);
                retained = true;
                continue;
            }
            if (_pending.ContainsKey(orderedKey)) _order.Enqueue(orderedKey);
        }
        return retained;
    }

    private void PromoteOverflow(GameplayInvalidationRequest request, long publicationSequence)
    {
        var discarded = PendingCount;
        var firstSequence = publicationSequence;
        var coalescedCount = 0;
        foreach (var pending in _pending.Values)
        {
            firstSequence = Math.Min(firstSequence, pending.Sequence);
            coalescedCount = checked(coalescedCount + pending.CoalescedCount + 1);
        }
        _overflowPromotions++;
        _overflowDiscarded += discarded;
        _pending.Clear();
        _order.Clear();
        if (_isPumping) _abortCurrentDispatch = true;
        else ClearCurrentDispatch();
        var key = new InvalidationKey(request.Burst, string.Empty, string.Empty, string.Empty);
        _pending[key] = new GameplayInvalidation(
            GameplayInvalidationKind.All,
            request.LifecycleGeneration,
            request.Burst,
            string.Empty,
            string.Empty,
            string.Empty,
            string.IsNullOrEmpty(request.Source) ? "overflow-conservative-promotion" : request.Source,
            firstSequence,
            publicationSequence,
            coalescedCount);
        _order.Enqueue(key);
        _peakPendingCount = Math.Max(_peakPendingCount, PendingCount);
    }

    private bool TryStartNextDispatch(long currentBurstExclusive, long sequenceLimit)
    {
        while (_order.Count > 0)
        {
            var key = _order.Peek();
            if (!_pending.TryGetValue(key, out var change))
            {
                _order.Dequeue();
                continue;
            }
            if (change.Burst >= currentBurstExclusive || change.LastSequence > sequenceLimit) return false;
            _order.Dequeue();
            _pending.Remove(key);
            _currentDispatch = change;
            _hasCurrentDispatch = true;
            _currentSubscriberIndex = 0;
            _currentSubscriberLimit = _subscriptions.Count;
            return true;
        }
        return false;
    }

    private void OnLifecycleTransition(GameLifecycleTransition transition)
    {
        if (_disposed) return;
        EnsureOwnerThread();
        var discarded = PendingCount;
        _staleDiscarded += discarded;
        _pending.Clear();
        _order.Clear();
        if (_isPumping) _abortCurrentDispatch = true;
        else ClearCurrentDispatch();
        Enqueue(
            new GameplayInvalidationRequest(
                GameplayInvalidationKind.Lifecycle,
                transition.Current.Generation,
                Math.Max(0, transition.Current.LastFrame),
                source: transition.Source),
            checked(++_nextSequence));
    }

    private void Deactivate(Subscription subscription)
    {
        if (_disposed) return;
        EnsureOwnerThread();
        subscription.Active = false;
        if (!_isPumping && !_hasCurrentDispatch) CompactSubscriptions();
    }

    private void CompactSubscriptions()
    {
        if (_hasCurrentDispatch) return;
        for (var index = _subscriptions.Count - 1; index >= 0; index--)
        {
            if (!_subscriptions[index].Active) _subscriptions.RemoveAt(index);
        }
    }

    private void ClearCurrentDispatch()
    {
        _hasCurrentDispatch = false;
        _currentDispatch = default;
        _currentSubscriberIndex = 0;
        _currentSubscriberLimit = 0;
    }

    private void EnsureOwnerThread()
    {
        var threadId = _readThreadId();
        _ownerThreadId ??= threadId;
        if (_ownerThreadId.Value == threadId) return;
        Interlocked.Increment(ref _offThreadRejections);
        throw new InvalidOperationException(
            $"Gameplay invalidation bus is main-thread-only; expected={_ownerThreadId.Value}; actual={threadId}");
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(GameplayInvalidationBus));
    }

    private static void ValidateKinds(GameplayInvalidationKind kinds, string parameterName)
    {
        if (kinds == GameplayInvalidationKind.None || (kinds & ~GameplayInvalidationKind.All) != 0)
            throw new ArgumentOutOfRangeException(parameterName);
    }

    private static void ValidateTarget(
        string domain,
        string entityId,
        string expectedTypeName,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(domain) && !string.IsNullOrEmpty(entityId))
            throw new ArgumentException("An entity invalidation requires a stable domain.", parameterName);
        if (string.IsNullOrWhiteSpace(entityId) && !string.IsNullOrEmpty(expectedTypeName))
            throw new ArgumentException("Expected type metadata requires a stable entity identity.", parameterName);
    }

    private readonly struct InvalidationKey : IEquatable<InvalidationKey>
    {
        public InvalidationKey(long burst, string domain, string entityId, string expectedTypeName)
        {
            Burst = burst;
            Domain = domain ?? string.Empty;
            EntityId = entityId ?? string.Empty;
            ExpectedTypeName = string.IsNullOrEmpty(EntityId) ? string.Empty : expectedTypeName ?? string.Empty;
        }

        public long Burst { get; }
        public string Domain { get; }
        public string EntityId { get; }
        public string ExpectedTypeName { get; }
        public bool HasEntity => !string.IsNullOrEmpty(EntityId);

        public bool Dominates(InvalidationKey other)
        {
            if (Burst != other.Burst) return false;
            if (!string.IsNullOrEmpty(Domain) &&
                !string.Equals(Domain, other.Domain, StringComparison.Ordinal)) return false;
            if (!HasEntity) return true;
            return Equals(other);
        }

        public bool Equals(InvalidationKey other) =>
            Burst == other.Burst &&
            string.Equals(Domain, other.Domain, StringComparison.Ordinal) &&
            string.Equals(EntityId, other.EntityId, StringComparison.Ordinal) &&
            string.Equals(ExpectedTypeName, other.ExpectedTypeName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is InvalidationKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = Burst.GetHashCode();
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Domain);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(EntityId);
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(ExpectedTypeName);
                return hash;
            }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private GameplayInvalidationBus? _owner;

        public Subscription(
            GameplayInvalidationBus owner,
            GameplayInvalidationFilter filter,
            Action<GameplayInvalidation> handler,
            string name)
        {
            _owner = owner;
            Filter = filter;
            Handler = handler;
            Name = name;
            Active = true;
        }

        public GameplayInvalidationFilter Filter { get; }
        public Action<GameplayInvalidation> Handler { get; }
        public string Name { get; }
        public bool Active { get; set; }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.Deactivate(this);
        }
    }
}

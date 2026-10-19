using System;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

internal sealed class AutoConceptTrainingSession
{
    internal AutoConceptTrainingSession(ConceptProgress target) => Target = target;
    internal ConceptProgress Target { get; }
    internal long? StartedAtTicks { get; set; }
}

internal sealed class AutoConceptOwnershipStore
{
    private string[] _keys = new string[16];
    private ConceptOwnership[] _values = new ConceptOwnership[16];
    private int _count;

    internal bool TryGet(string key, out ConceptOwnership ownership)
    {
        var index = Find(key);
        if (index >= 0)
        {
            ownership = _values[index];
            return true;
        }
        ownership = default;
        return false;
    }

    internal void ObserveBaseline(string key, int quantity) =>
        Set(key, new ConceptOwnership(Math.Max(0, quantity), 0));

    internal void RecordAutomatedDelta(string key, int currentQuantity, int delta)
    {
        if (!TryGet(key, out var ownership))
            ownership = new ConceptOwnership(currentQuantity - Math.Max(0, delta), 0);
        Set(key, new ConceptOwnership(
            ownership.ManualBaseline,
            Math.Max(0, ownership.AutomatedDelta + delta)));
    }

    internal bool RebaselineIfUnexpected(string key, int actualQuantity)
    {
        if (!TryGet(key, out var ownership))
        {
            ObserveBaseline(key, actualQuantity);
            return false;
        }
        if (actualQuantity == ownership.ExpectedQuantity) return false;
        ObserveBaseline(key, actualQuantity);
        return true;
    }

    private void Set(string key, ConceptOwnership value)
    {
        var index = Find(key);
        if (index >= 0)
        {
            _values[index] = value;
            return;
        }
        EnsureCapacity();
        _keys[_count] = key;
        _values[_count] = value;
        _count++;
    }

    private int Find(string key)
    {
        for (var index = 0; index < _count; index++)
            if (string.Equals(_keys[index], key, StringComparison.Ordinal)) return index;
        return -1;
    }

    private void EnsureCapacity()
    {
        if (_count < _keys.Length) return;
        Array.Resize(ref _keys, _keys.Length * 2);
        Array.Resize(ref _values, _values.Length * 2);
    }
}

internal sealed class AutoConceptTrainingStore
{
    private string[] _keys = new string[16];
    private AutoConceptTrainingSession?[] _values = new AutoConceptTrainingSession?[16];
    private int _count;

    internal int Count => _count;
    internal string KeyAt(int index) => _keys[index];
    internal AutoConceptTrainingSession SessionAt(int index) => _values[index]!;
    internal bool Contains(string key) => Find(key) >= 0;

    internal void Set(string key, AutoConceptTrainingSession session)
    {
        var index = Find(key);
        if (index >= 0)
        {
            _values[index] = session;
            return;
        }
        EnsureCapacity();
        _keys[_count] = key;
        _values[_count] = session;
        _count++;
    }

    internal void Remove(string key)
    {
        var index = Find(key);
        if (index < 0) return;
        _count--;
        if (index != _count)
        {
            _keys[index] = _keys[_count];
            _values[index] = _values[_count];
        }
        _keys[_count] = string.Empty;
        _values[_count] = null;
    }

    internal void Clear()
    {
        Array.Clear(_keys, 0, _count);
        Array.Clear(_values, 0, _count);
        _count = 0;
    }

    private int Find(string key)
    {
        for (var index = 0; index < _count; index++)
            if (string.Equals(_keys[index], key, StringComparison.Ordinal)) return index;
        return -1;
    }

    private void EnsureCapacity()
    {
        if (_count < _keys.Length) return;
        Array.Resize(ref _keys, _keys.Length * 2);
        Array.Resize(ref _values, _values.Length * 2);
    }
}

internal sealed class AutoConceptAssignmentHistory
{
    private string[] _keys = new string[16];
    private long[] _values = new long[16];
    private int _count;

    internal bool TryGet(string key, out long value)
    {
        for (var index = 0; index < _count; index++)
        {
            if (!string.Equals(_keys[index], key, StringComparison.Ordinal)) continue;
            value = _values[index];
            return true;
        }
        value = 0;
        return false;
    }

    internal void Set(string key, long value)
    {
        for (var index = 0; index < _count; index++)
        {
            if (!string.Equals(_keys[index], key, StringComparison.Ordinal)) continue;
            _values[index] = value;
            return;
        }
        if (_count == _keys.Length)
        {
            Array.Resize(ref _keys, _keys.Length * 2);
            Array.Resize(ref _values, _values.Length * 2);
        }
        _keys[_count] = key;
        _values[_count] = value;
        _count++;
    }

    internal void Clear()
    {
        Array.Clear(_keys, 0, _count);
        Array.Clear(_values, 0, _count);
        _count = 0;
    }
}

internal struct AutoConceptCycleState
{
    private AutoConceptCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        Ownership = new AutoConceptOwnershipStore();
        TrainingSessions = new AutoConceptTrainingStore();
        LastTimedAssignment = new AutoConceptAssignmentHistory();
        BaselineCaptured = false;
        TimedSessionsInitialized = false;
        TimedAssignmentSequence = 0;
        PreferredReplacement = Guid.Empty;
        PreferredReplacementExpiresAtTicks = 0;
        CandidateCursor = 0;
        LastSlotMode = null;
        LastTrainingPeriod = null;
        HasPendingReceipt = false;
        PendingReceiptCommitted = false;
        PendingReceiptAction = default;
        TurnHandedOn = default;
        Decision = default;
    }

    public LifecycleGeneration Lifecycle { get; private set; }
    internal AutoConceptOwnershipStore Ownership;
    internal AutoConceptTrainingStore TrainingSessions;
    internal AutoConceptAssignmentHistory LastTimedAssignment;
    internal bool BaselineCaptured;
    internal bool TimedSessionsInitialized;
    internal long TimedAssignmentSequence;
    internal Guid PreferredReplacement;
    internal long PreferredReplacementExpiresAtTicks;
    internal int CandidateCursor;
    internal AutoConceptSlotManagementMode? LastSlotMode;
    internal int? LastTrainingPeriod;
    internal bool HasPendingReceipt;
    internal bool PendingReceiptCommitted;
    internal AutoConceptCycleAction PendingReceiptAction;

    /// <summary>The action whose batch just faulted. The evaluation that reads that receipt plans past it.</summary>
    internal AutoConceptCycleAction TurnHandedOn;
    public AutoConceptDecisionMetrics Decision { get; private set; }

    public static AutoConceptCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    internal void RecordPlanned(in AutoConceptCycleAction action)
    {
        PendingReceiptAction = action;
        HasPendingReceipt = true;
        PendingReceiptCommitted = false;
        CandidateCursor = checked(CandidateCursor + 1);
    }

    internal void ClearPendingReceipt()
    {
        PendingReceiptAction = default;
        HasPendingReceipt = false;
        PendingReceiptCommitted = false;
    }

    internal void RecordDecision(in AutoConceptDecisionMetrics decision) => Decision = decision;
}

internal enum AutoConceptDecisionKind
{
    Idle = 0,
    UnsafeRollback = 1,
    PreferredReplacement = 2,
    Breadth = 3,
    Rebalance = 4,
    Depth = 5,
}

internal enum AutoConceptIdleReason
{
    None = 0,
    WaitingForTraining = 1,
    NoUnlockedAssignableReplacement = 2,
    ResourceAtZero = 3,
    RateReserve = 4,
    DrainUnknown = 5,
}

internal enum AutoConceptIdleMove
{
    None = 0,
    Slot = 1,
    Rotate = 2,
    Deepen = 3,
}

internal readonly struct AutoConceptIdle : IEquatable<AutoConceptIdle>
{
    internal AutoConceptIdle(AutoConceptIdleReason reason)
        : this(reason, AutoConceptIdleMove.None, Guid.Empty, Guid.Empty, Guid.Empty)
    {
    }

    internal AutoConceptIdle(
        AutoConceptIdleReason reason,
        AutoConceptIdleMove move,
        Guid recipe,
        Guid displaced,
        Guid resource)
    {
        Reason = reason;
        Move = move;
        Recipe = recipe;
        Displaced = displaced;
        Resource = resource;
    }

    public AutoConceptIdleReason Reason { get; }
    public AutoConceptIdleMove Move { get; }
    public Guid Recipe { get; }
    public Guid Displaced { get; }
    public Guid Resource { get; }

    public bool Equals(AutoConceptIdle other) =>
        Reason == other.Reason && Move == other.Move && Recipe == other.Recipe &&
        Displaced == other.Displaced && Resource == other.Resource;

    public override bool Equals(object? obj) => obj is AutoConceptIdle other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Reason, Move, Recipe, Displaced, Resource);
}

internal readonly struct AutoConceptDecisionMetrics
{
    internal AutoConceptDecisionMetrics(
        int capturedRecipes,
        int eligibleRecipes,
        int activeRecipes,
        int ownedRecipes,
        int plannedActions,
        AutoConceptDecisionKind kind,
        AutoConceptIdle idle = default)
    {
        CapturedRecipes = capturedRecipes;
        EligibleRecipes = eligibleRecipes;
        ActiveRecipes = activeRecipes;
        OwnedRecipes = ownedRecipes;
        PlannedActions = plannedActions;
        Kind = kind;
        Idle = idle;
    }

    public int CapturedRecipes { get; }
    public int EligibleRecipes { get; }
    public int ActiveRecipes { get; }
    public int OwnedRecipes { get; }
    public int PlannedActions { get; }
    public AutoConceptDecisionKind Kind { get; }
    public AutoConceptIdle Idle { get; }
    public AutoConceptIdleReason IdleReason => Idle.Reason;
}

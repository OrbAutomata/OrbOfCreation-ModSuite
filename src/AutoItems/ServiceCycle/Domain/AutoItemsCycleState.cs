using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum AutoItemsDecisionKind
{
    Disabled = 0,
    Idle = 1,
    Relic = 2,
    Scroll = 3,
    TemporaryItem = 4,
    AwaitingTemporaryActivation = 5,
    TemporaryEffectActive = 6,
}

internal readonly struct AutoItemsDecisionMetrics
{
    internal AutoItemsDecisionMetrics(
        int captured,
        int rejectedProfiles,
        int temporaryItems,
        int eligibleTemporaryItems,
        int eligibleRelics,
        int eligibleScrolls,
        int plannedActions,
        AutoItemsDecisionKind kind)
    {
        Captured = captured;
        RejectedProfiles = rejectedProfiles;
        TemporaryItems = temporaryItems;
        EligibleTemporaryItems = eligibleTemporaryItems;
        EligibleRelics = eligibleRelics;
        EligibleScrolls = eligibleScrolls;
        PlannedActions = plannedActions;
        Kind = kind;
    }

    internal int Captured { get; }
    internal int RejectedProfiles { get; }
    internal int TemporaryItems { get; }
    internal int EligibleTemporaryItems { get; }
    internal int EligibleRelics { get; }
    internal int EligibleScrolls { get; }
    internal int PlannedActions { get; }
    internal AutoItemsDecisionKind Kind { get; }
}

internal struct AutoItemsCycleState
{
    private AutoItemsCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        Decision = default;
        _allowlistConfiguration = default;
        _temporaryAllowlist = null;
        _targetlessScrolls = PublicationTable<Guid>.Empty;
        PendingReceiptAction = default;
        HasPendingReceipt = false;
        PendingTemporaryItem = Guid.Empty;
        TemporarySubmittedFromFrame = 0;
        TemporaryActivationSeen = false;
        PausedTemporaryItem = Guid.Empty;
        TemporaryPauseCause = AutoItemsTemporaryPauseCause.None;
    }

    private ConfigGeneration _allowlistConfiguration;
    private PublicationTable<Guid>? _temporaryAllowlist;
    private PublicationTable<Guid> _targetlessScrolls;

    internal LifecycleGeneration Lifecycle { get; }
    internal AutoItemsDecisionMetrics Decision { get; private set; }
    internal PublicationTable<Guid>? TemporaryAllowlist => _temporaryAllowlist;
    internal PublicationTable<Guid> TargetlessScrolls => _targetlessScrolls;
    internal AutoItemsCycleAction PendingReceiptAction { get; private set; }
    internal bool HasPendingReceipt { get; private set; }
    internal Guid PendingTemporaryItem { get; private set; }
    internal long TemporarySubmittedFromFrame { get; private set; }
    internal bool TemporaryActivationSeen { get; private set; }
    internal Guid PausedTemporaryItem { get; private set; }
    internal AutoItemsTemporaryPauseCause TemporaryPauseCause { get; private set; }

    internal static AutoItemsCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);
    internal void RecordDecision(in AutoItemsDecisionMetrics decision) => Decision = decision;

    internal void ObserveConfiguration(
        ConfigGeneration generation,
        AutoItemsConfiguration configuration)
    {
        if (_allowlistConfiguration == generation) return;
        _temporaryAllowlist = AutoItemsTemporaryItemAllowlist.ParsePublication(
            configuration.TemporaryItemAllowlist);
        _allowlistConfiguration = generation;
    }

    internal void RecordPlanned(in AutoItemsCycleAction action)
    {
        PendingReceiptAction = action;
        HasPendingReceipt = true;
    }

    internal void ClearPendingReceipt()
    {
        PendingReceiptAction = default;
        HasPendingReceipt = false;
    }

    internal void RecordSubmittedTemporary(in AutoItemsCycleAction action)
    {
        PendingTemporaryItem = action.ItemId;
        TemporarySubmittedFromFrame = action.CollectedAtFrame;
        TemporaryActivationSeen = false;
    }

    internal void RememberTargetlessScroll(Guid itemId) =>
        _targetlessScrolls = With(_targetlessScrolls, itemId);

    internal void ForgetTargetlessScrolls() =>
        _targetlessScrolls = PublicationTable<Guid>.Empty;

    internal void MarkTemporaryActivationSeen() => TemporaryActivationSeen = true;

    internal void ClearPendingTemporary()
    {
        PendingTemporaryItem = Guid.Empty;
        TemporarySubmittedFromFrame = 0;
        TemporaryActivationSeen = false;
    }

    internal void PausePendingTemporary(AutoItemsTemporaryPauseCause cause)
    {
        if (cause == AutoItemsTemporaryPauseCause.None)
            throw new ArgumentOutOfRangeException(nameof(cause));
        var itemId = PendingTemporaryItem;
        if (itemId == Guid.Empty)
            throw new InvalidOperationException(
                "A temporary-item pause requires a pending exact item.");

        PausedTemporaryItem = itemId;
        TemporaryPauseCause = cause;
        ClearPendingTemporary();
    }

    internal void ResumePausedTemporary()
    {
        PausedTemporaryItem = Guid.Empty;
        TemporaryPauseCause = AutoItemsTemporaryPauseCause.None;
    }

    private static PublicationTable<Guid> With(PublicationTable<Guid> values, Guid itemId)
    {
        if (AutoItemsTemporaryItemAllowlist.Contains(values, itemId)) return values;
        var previous = values.AsSpan();
        var rows = new Guid[previous.Length + 1];
        previous.CopyTo(rows);
        rows[previous.Length] = itemId;
        Array.Sort(rows);
        return PublicationTable<Guid>.Create(rows, rows.Length);
    }
}

using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbAutomata;

internal sealed class AutoScribeServiceCycleDiagnosticsBridge
{
    private readonly AutoScribeFeatureDependencies _dependencies;
    private readonly AutoScribeOneShotCraftGameAction _gameAction;
    private readonly AutoScribeActionHealth _health;
    private ServiceCycleServiceDiagnosticsSnapshot[] _services =
        new ServiceCycleServiceDiagnosticsSnapshot[8];
    private long _lifecycle;
    private ConfigGeneration _configuration;
    private bool _emergencyDisabled;
    private bool _cycleObserved;
    private AutoScribeDecisionKind _decisionKind;
    private int _blockedRole = -1;
    private AutoScribeEvidenceReason _blockedReason;
    private long _healthRevision = -1;

    internal AutoScribeServiceCycleDiagnosticsBridge(
        AutoScribeFeatureDependencies dependencies,
        AutoScribeOneShotCraftGameAction gameAction,
        AutoScribeActionHealth health,
        long lifecycle,
        ConfigGeneration configuration)
    {
        _dependencies = dependencies;
        _gameAction = gameAction;
        _health = health;
        _lifecycle = lifecycle;
        _configuration = configuration;
        Publish();
    }

    internal void Observe(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
        var changed = _emergencyDisabled != pump.IsEmergencyStopEngaged;
        _emergencyDisabled = pump.IsEmergencyStopEngaged;
        if (report.ResponsesAcquired != 0 &&
            TryReadProjection(
                pump,
                out var kind,
                out var blockedRole,
                out var blockedReason))
        {
            changed = changed ||
                !_cycleObserved ||
                kind != _decisionKind ||
                blockedRole != _blockedRole ||
                blockedReason != _blockedReason;
            _cycleObserved = true;
            _decisionKind = kind;
            _blockedRole = blockedRole;
            _blockedReason = blockedReason;
        }
        if (_healthRevision != _health.Revision)
        {
            _healthRevision = _health.Revision;
            changed = true;
        }
        if (changed) Publish();
    }

    internal void ObserveConfiguration(ConfigGeneration generation)
    {
        if (generation.Value < _configuration.Value) return;
        _configuration = generation;
        _cycleObserved = false;
        _decisionKind = AutoScribeDecisionKind.Disabled;
        _blockedRole = -1;
        _blockedReason = AutoScribeEvidenceReason.None;
    }

    internal void ObserveLifecycle(long lifecycle, ConfigGeneration generation)
    {
        if (generation.Value < _configuration.Value) return;
        _lifecycle = lifecycle;
        _configuration = generation;
        _cycleObserved = false;
        _decisionKind = AutoScribeDecisionKind.Disabled;
        _blockedRole = -1;
        _blockedReason = AutoScribeEvidenceReason.None;
        _health.InvalidateLifecycle();
        _healthRevision = _health.Revision;
        Publish();
    }

    private void Publish()
    {
        var status = Project();
        _dependencies.FeatureStatus.ObserveRuntimeLifecycle(
            status.State,
            status.Reason,
            status.Summary,
            _lifecycle,
            _configuration);
    }

    private AutoScribeFeatureStatus Project()
    {
        var ownsActionFamily = AutoScribeActionFamilyAccess.Owns(
            _dependencies.OwnsActionFamily);
        return ProjectStatus(
            _dependencies.Profile,
            _emergencyDisabled,
            ownsActionFamily,
            ownsActionFamily ? string.Empty : OwnershipReason(),
            _gameAction.BindingsAvailable,
            _gameAction.BindingFailure,
            _health,
            _cycleObserved,
            _decisionKind,
            _blockedRole,
            _blockedReason);
    }

    internal static AutoScribeFeatureStatus ProjectStatus(
        AutoScribeIdentityProfile profile,
        bool emergencyDisabled,
        bool ownsActionFamily,
        string ownershipReason,
        bool bindingsAvailable,
        string bindingFailure,
        AutoScribeActionHealth health,
        bool cycleObserved,
        AutoScribeDecisionKind decisionKind,
        int blockedRole,
        AutoScribeEvidenceReason blockedReason)
    {
        if (profile is null) throw new ArgumentNullException(nameof(profile));
        if (health is null) throw new ArgumentNullException(nameof(health));
        // Player emergency control deliberately shadows retained health: its current stop command
        // is the status the player must act on first.
        if (emergencyDisabled)
            return Blocked(
                FeatureStatusReasonCode.EmergencyDisabled,
                "Automata Emergency Disable is active.");
        // A feature that does not own the action family is inert. Report that conflict before a
        // retained failure from an earlier ownership period.
        if (!ownsActionFamily)
            return Blocked(
                FeatureStatusReasonCode.ActionFamilyConflict,
                ownershipReason);
        if (!bindingsAvailable)
            return new AutoScribeFeatureStatus(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.ContractUnavailable,
                string.IsNullOrWhiteSpace(bindingFailure)
                    ? "The lifecycle-scoped Auto Scribe binding set is unavailable."
                    : bindingFailure);

        // A genuine unresolved action failure remains operator-visible until a verified action or
        // lifecycle invalidation clears it. Publication-level evidence backpressure must not hide
        // that defect; quiet backpressure never enters action health in the first place.
        if (health.HasFailure)
            return FromActionHealth(health);
        if (!cycleObserved)
            return new AutoScribeFeatureStatus(
                FeatureStatusState.NotReady,
                FeatureStatusReasonCode.GameplayNotReady,
                "Auto Scribe is waiting for its first world publication.");
        if (decisionKind == AutoScribeDecisionKind.EvidenceBlocked)
        {
            if (profile.TryFindOrdinal(blockedRole, out var role))
                return Blocked(
                    FeatureStatusReasonCode.EvidenceUnavailable,
                    ScrollCoveragePlanner.DescribeEvidence(in role, blockedReason));
            return Blocked(
                FeatureStatusReasonCode.EvidenceUnavailable,
                $"Auto Scribe blocked an unknown role ordinal {blockedRole} for " +
                $"evidence reason {blockedReason}.");
        }
        if (decisionKind == AutoScribeDecisionKind.QueueBusy)
            return Blocked(
                FeatureStatusReasonCode.QueueFull,
                "Active Scribe work already fills the native queue; Auto Scribe is waiting for the next world publication.");
        return new AutoScribeFeatureStatus(
            FeatureStatusState.Operational,
            FeatureStatusReasonCode.None,
            decisionKind switch
            {
                AutoScribeDecisionKind.Planned =>
                    "Auto Scribe planned one audited Scroll from the latest world.",
                AutoScribeDecisionKind.ExternallyProducing =>
                    "Auto Scribe is yielding to player-owned automatic Scribe production.",
                _ => "Auto Scribe is active and coverage is currently satisfied.",
            });
    }

    private static AutoScribeFeatureStatus FromActionHealth(AutoScribeActionHealth health)
    {
        var summary = string.IsNullOrWhiteSpace(health.Reason)
            ? $"Auto Scribe failed at {health.Stage}/{health.Preflight}."
            : health.Reason;
        switch (health.Preflight)
        {
            case AutoScribePreflight.ContractUnavailable:
                return new AutoScribeFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.ContractUnavailable,
                    summary);
            case AutoScribePreflight.IdentityUnavailable:
                return new AutoScribeFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.IdentityMismatch,
                    summary);
            case AutoScribePreflight.PostPaymentFault:
            case AutoScribePreflight.VerificationFailed:
            case AutoScribePreflight.Quarantined:
                return new AutoScribeFeatureStatus(
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.MutationQuarantined,
                    summary);
            case AutoScribePreflight.RelationshipMismatch:
                return new AutoScribeFeatureStatus(
                    FeatureStatusState.ContractUnavailable,
                    FeatureStatusReasonCode.IdentityMismatch,
                    summary);
            case AutoScribePreflight.WrongThread:
                return new AutoScribeFeatureStatus(
                    FeatureStatusState.Faulted,
                    FeatureStatusReasonCode.ContractUnavailable,
                    summary);
        }
        throw new InvalidOperationException(
            $"Auto Scribe action health retained non-failure {health.Preflight}.");
    }

    private string OwnershipReason()
    {
        try
        {
            var reason = _dependencies.ReadOwnershipFailure();
            return string.IsNullOrWhiteSpace(reason)
                ? "Auto Scribe does not own CraftingQueueSubmission."
                : reason;
        }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return "Auto Scribe ownership evidence failed: " + ex.GetBaseException().Message;
        }
    }

    private bool TryReadProjection(
        SuiteFramePump pump,
        out AutoScribeDecisionKind kind,
        out int blockedRole,
        out AutoScribeEvidenceReason blockedReason)
    {
        var copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        if (copy.RequiredCount > _services.Length)
        {
            _services = new ServiceCycleServiceDiagnosticsSnapshot[copy.RequiredCount];
            copy = ServiceCycleDiagnostics.CopyServices(pump, _services);
        }
        for (var index = 0; index < copy.WrittenCount; index++)
        {
            var service = _services[index];
            if (!service.ServiceId.Equals(AutoScribeServicePolicies.ServiceId) ||
                !service.LatestProjection.IsPresent)
                continue;
            var projection = service.LatestProjection.Snapshot;
            return AutoScribeServiceProjection.TryRead(
                in projection,
                out kind,
                out blockedRole,
                out blockedReason);
        }
        kind = AutoScribeDecisionKind.Disabled;
        blockedRole = -1;
        blockedReason = AutoScribeEvidenceReason.None;
        return false;
    }

    private static AutoScribeFeatureStatus Blocked(
        FeatureStatusReasonCode reason,
        string summary) =>
        new(FeatureStatusState.TemporarilyBlocked, reason, summary);

    internal readonly struct AutoScribeFeatureStatus
    {
        internal AutoScribeFeatureStatus(
            FeatureStatusState state,
            FeatureStatusReasonCode reason,
            string summary)
        {
            State = state;
            Reason = reason;
            Summary = summary;
        }

        internal FeatureStatusState State { get; }
        internal FeatureStatusReasonCode Reason { get; }
        internal string Summary { get; }
    }
}

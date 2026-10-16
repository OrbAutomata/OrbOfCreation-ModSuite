using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal enum AutoScribeDecisionKind
{
    Disabled = 0,
    Idle = 1,
    Planned = 2,
    EvidenceBlocked = 3,
    ExternallyProducing = 4,
    QueueBusy = 5,
}

internal readonly struct AutoScribeDecisionMetrics
{
    internal AutoScribeDecisionMetrics(
        int enabledRoles,
        int deficientRoles,
        int externalRoles,
        int plannedActions,
        int selectedCraftCostOrder,
        AutoScribeDecisionKind kind,
        int blockedRoleOrdinal,
        AutoScribeEvidenceReason blockedReason)
    {
        EnabledRoles = enabledRoles;
        DeficientRoles = deficientRoles;
        ExternalRoles = externalRoles;
        PlannedActions = plannedActions;
        SelectedCraftCostOrder = selectedCraftCostOrder;
        Kind = kind;
        BlockedRoleOrdinal = blockedRoleOrdinal;
        BlockedReason = blockedReason;
    }

    internal int EnabledRoles { get; }
    internal int DeficientRoles { get; }
    internal int ExternalRoles { get; }
    internal int PlannedActions { get; }
    internal int SelectedCraftCostOrder { get; }
    internal AutoScribeDecisionKind Kind { get; }
    internal int BlockedRoleOrdinal { get; }
    internal AutoScribeEvidenceReason BlockedReason { get; }
}

internal struct AutoScribeCycleState
{
    private AutoScribeCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        RoleConfiguration = default;
        EnabledRoles = null;
        LastSelectedCraftCostOrder = -1;
        Decision = default;
    }

    internal LifecycleGeneration Lifecycle { get; }
    internal ConfigGeneration RoleConfiguration { get; private set; }
    internal PublicationTable<ScrollRoleKey>? EnabledRoles { get; private set; }
    internal int LastSelectedCraftCostOrder { get; private set; }
    internal AutoScribeDecisionMetrics Decision { get; private set; }

    internal static AutoScribeCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    internal void PinRoles(
        ConfigGeneration generation,
        string serialized,
        AutoScribeIdentityProfile profile)
    {
        if (RoleConfiguration == generation) return;
        EnabledRoles = AutoScribeRoleSelection.ParsePublication(serialized, profile.Roles);
        LastSelectedCraftCostOrder = -1;
        RoleConfiguration = generation;
    }

    internal void ObserveSelection(int craftCostOrder) =>
        LastSelectedCraftCostOrder = craftCostOrder;

    internal void RecordDecision(in AutoScribeDecisionMetrics decision) => Decision = decision;
}

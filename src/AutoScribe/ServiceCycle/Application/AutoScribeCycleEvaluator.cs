using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoScribeCycleEvaluator
{
    internal static WakePolicy Evaluate(
        GameWorldState world,
        in SuiteRuntimeConfiguration configuration,
        AutoScribeIdentityProfile profile,
        PublicationTable<ScrollRoleKey>? enabledRoles,
        int afterCraftCostOrder,
        ServiceActionWriter<AutoScribeCycleAction> actions,
        out AutoScribeDecisionMetrics metrics)
    {
        if (!AutoScribeConfigurationPolicy.IsOperational(configuration))
        {
            metrics = new AutoScribeDecisionMetrics(
                0, 0, 0, 0, -1, AutoScribeDecisionKind.Disabled, -1,
                AutoScribeEvidenceReason.None);
            return WakePolicy.OnPublication;
        }

        var plan = ScrollCoveragePlanner.Build(world, profile);
        var enabled = 0;
        var deficient = 0;
        var external = 0;
        for (var index = 0; index < plan.Roles.Length; index++)
        {
            var row = plan.Roles[index];
            if (!row.RecipeId.Equals(System.Guid.Empty) &&
                AutoScribeRoleSelection.Contains(enabledRoles, row.Role))
                enabled++;
            if (row.ShouldProduce &&
                AutoScribeRoleSelection.Contains(enabledRoles, row.Role))
                deficient++;
            if (row.State == ScrollCoverageState.ExternallyProducing &&
                AutoScribeRoleSelection.Contains(enabledRoles, row.Role))
                external++;
        }

        var selection = plan.ChooseCraft(enabledRoles, afterCraftCostOrder);
        if (selection.Kind == ScrollCraftSelectionKind.Selected)
        {
            var selected = selection.SelectedScroll;
            actions.Add(new AutoScribeCycleAction(
                selected.RecipeId,
                selected.ScrollId,
                selected.RequestedCraftLevel,
                world.CollectedAtEpoch));
            metrics = new AutoScribeDecisionMetrics(
                enabled,
                deficient,
                external,
                1,
                selected.CraftCostOrder,
                AutoScribeDecisionKind.Planned,
                -1,
                AutoScribeEvidenceReason.None);
            return WakePolicy.OnPublication;
        }
        if (selection.Kind == ScrollCraftSelectionKind.EvidenceBlocked)
        {
            metrics = new AutoScribeDecisionMetrics(
                enabled,
                deficient,
                external,
                0,
                -1,
                AutoScribeDecisionKind.EvidenceBlocked,
                selection.BlockedRoleOrdinal,
                selection.BlockedReason);
            return WakePolicy.OnPublication;
        }
        if (selection.Kind == ScrollCraftSelectionKind.QueueBusy)
        {
            metrics = new AutoScribeDecisionMetrics(
                enabled,
                deficient,
                external,
                0,
                -1,
                AutoScribeDecisionKind.QueueBusy,
                -1,
                AutoScribeEvidenceReason.None);
            return WakePolicy.OnPublication;
        }

        if (selection.Kind != ScrollCraftSelectionKind.Idle)
            throw new System.InvalidOperationException(
                $"Scribe coverage returned invalid selection kind {selection.Kind}.");

        metrics = new AutoScribeDecisionMetrics(
            enabled,
            deficient,
            external,
            0,
            -1,
            external > 0
                ? AutoScribeDecisionKind.ExternallyProducing
                : AutoScribeDecisionKind.Idle,
            -1,
            AutoScribeEvidenceReason.None);
        return WakePolicy.OnPublication;
    }
}

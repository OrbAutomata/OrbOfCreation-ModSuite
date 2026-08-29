using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoScribeServiceProjection
{
    internal const int EnabledRolesKey = 10;
    internal const int DeficientRolesKey = 11;
    internal const int ExternalRolesKey = 12;
    internal const int PlannedActionsKey = 13;
    internal const int DecisionKindKey = 14;
    internal const int BlockedRoleKey = 15;
    internal const int BlockedReasonKey = 16;

    internal static void Write(
        in AutoScribeCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(EnabledRolesKey), Integer(decision.EnabledRoles));
        output.Add(new ServiceProjectionKey(DeficientRolesKey), Integer(decision.DeficientRoles));
        output.Add(new ServiceProjectionKey(ExternalRolesKey), Integer(decision.ExternalRoles));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), Integer((int)decision.Kind));
        output.Add(new ServiceProjectionKey(BlockedRoleKey), Integer(decision.BlockedRoleOrdinal));
        output.Add(new ServiceProjectionKey(BlockedReasonKey), Integer((int)decision.BlockedReason));
    }

    internal static bool TryRead(
        in ServiceStateProjectionSnapshot projection,
        out AutoScribeDecisionKind kind,
        out int blockedRole,
        out AutoScribeEvidenceReason blockedReason)
    {
        kind = AutoScribeDecisionKind.Disabled;
        blockedRole = -1;
        blockedReason = AutoScribeEvidenceReason.None;
        var foundKind = false;
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Value.Kind != ServiceProjectionValueKind.Integer) continue;
            switch (entry.Key.Value)
            {
                case DecisionKindKey
                    when entry.Value.Integer is >= (int)AutoScribeDecisionKind.Disabled
                        and <= (int)AutoScribeDecisionKind.QueueBusy:
                    kind = (AutoScribeDecisionKind)entry.Value.Integer;
                    foundKind = true;
                    break;
                case BlockedRoleKey:
                    blockedRole = checked((int)entry.Value.Integer);
                    break;
                case BlockedReasonKey:
                    blockedReason = entry.Value.Integer is >= int.MinValue and <= int.MaxValue &&
                        System.Enum.IsDefined(
                            typeof(AutoScribeEvidenceReason),
                            (int)entry.Value.Integer)
                        ? (AutoScribeEvidenceReason)entry.Value.Integer
                        : AutoScribeEvidenceReason.Unknown;
                    break;
            }
        }
        return foundKind;
    }

    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}

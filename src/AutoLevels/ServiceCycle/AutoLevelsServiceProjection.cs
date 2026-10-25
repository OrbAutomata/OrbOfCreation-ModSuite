using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

internal static class AutoLevelsServiceProjection
{
    internal static void Write(in AutoLevelsCycleState state, ServiceStateProjectionBuilder output)
    {
        output.Add(new ServiceProjectionKey(1), ServiceProjectionValue.FromBoolean(state.Observed));
        if (!state.Observed) return;
        for (var index = 0; index < 4; index++)
        {
            var decision = state.Decision(index);
            var key = 100 + index * 16;
            Integer(output, key, decision.CurrentLevel);
            Integer(output, key + 1, decision.TargetLevel);
            Integer(output, key + 2, (int)decision.Kind);
            Integer(output, key + 3, (int)decision.Reason);
            ServiceProjectionGuid.Write(output, key + 4, decision.ResourceId);
            ServiceProjectionBigDouble.Write(output, key + 6, decision.Required);
            ServiceProjectionBigDouble.Write(output, key + 8, decision.Budget);
        }
    }

    internal static string Describe(ServiceStateProjectionSnapshot projection)
    {
        if (!Observed(in projection)) return string.Empty;
        var text = new StringBuilder("Latest evaluation:");
        for (var index = 0; index < 4; index++)
        {
            var decision = Read(in projection, index);
            text.Append('\n').Append(AutoLevelsDecisionText.Describe(in decision, EntityIdentityCatalogPublication.Current));
        }
        return text.ToString();
    }

    internal static (FeatureStatusState State, FeatureStatusReasonCode Reason, string Summary) ProjectHealth(
        ServiceStateProjectionSnapshot projection)
    {
        if (!Observed(in projection))
            return (FeatureStatusState.NotReady, FeatureStatusReasonCode.GameplayNotReady, "Waiting for a current world evaluation.");
        // Per-dial missing evidence does not stop the other dials. The detail identifies each hold.
        return (FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty);
    }

    private static bool Observed(in ServiceStateProjectionSnapshot projection)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == 1 && entry.Value.Kind == ServiceProjectionValueKind.Boolean) return entry.Value.Boolean;
        }
        return false;
    }

    internal static AutoLevelsDecision Read(in ServiceStateProjectionSnapshot projection, int index)
    {
        var key = 100 + index * 16;
        var dial = index switch { 0 => LevelDial.Output, 1 => LevelDial.Reserve, 2 => LevelDial.Druidry, _ => LevelDial.Alchemy };
        return new AutoLevelsDecision(dial, (int)Integer(in projection, key), (int)Integer(in projection, key + 1),
            (AutoLevelsDecisionKind)Integer(in projection, key + 2), (AutoLevelsDecisionReason)Integer(in projection, key + 3),
            ServiceProjectionGuid.Read(in projection, key + 4), ServiceProjectionBigDouble.Read(in projection, key + 6),
            ServiceProjectionBigDouble.Read(in projection, key + 8));
    }

    private static void Integer(ServiceStateProjectionBuilder output, int key, long value) =>
        output.Add(new ServiceProjectionKey(key), ServiceProjectionValue.FromInteger(value));
    private static long Integer(in ServiceStateProjectionSnapshot projection, int key)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == key && entry.Value.Kind == ServiceProjectionValueKind.Integer) return entry.Value.Integer;
        }
        return 0;
    }
}

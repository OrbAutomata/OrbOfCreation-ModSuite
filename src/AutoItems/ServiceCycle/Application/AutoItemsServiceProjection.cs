using System;
using System.Buffers.Binary;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal static class AutoItemsServiceProjection
{
    internal const int CapturedKey = 10;
    internal const int RejectedProfilesKey = 11;
    internal const int EligibleRelicsKey = 12;
    internal const int EligibleScrollsKey = 13;
    internal const int PlannedActionsKey = 14;
    internal const int DecisionKindKey = 15;
    internal const int TemporaryItemsKey = 16;
    internal const int EligibleTemporaryItemsKey = 17;
    internal const int TemporaryPauseCauseKey = 18;
    internal const int TemporaryPauseGuidLowKey = 19;
    internal const int TemporaryPauseGuidHighKey = 20;

    internal static void Write(
        in AutoItemsCycleState state,
        ServiceStateProjectionBuilder output)
    {
        var decision = state.Decision;
        output.Add(new ServiceProjectionKey(CapturedKey), Integer(decision.Captured));
        output.Add(new ServiceProjectionKey(RejectedProfilesKey), Integer(decision.RejectedProfiles));
        output.Add(new ServiceProjectionKey(EligibleRelicsKey), Integer(decision.EligibleRelics));
        output.Add(new ServiceProjectionKey(EligibleScrollsKey), Integer(decision.EligibleScrolls));
        output.Add(new ServiceProjectionKey(PlannedActionsKey), Integer(decision.PlannedActions));
        output.Add(new ServiceProjectionKey(DecisionKindKey), Integer((int)decision.Kind));
        output.Add(new ServiceProjectionKey(TemporaryItemsKey), Integer(decision.TemporaryItems));
        output.Add(
            new ServiceProjectionKey(EligibleTemporaryItemsKey),
            Integer(decision.EligibleTemporaryItems));
        output.Add(
            new ServiceProjectionKey(TemporaryPauseCauseKey),
            Integer((int)state.TemporaryPauseCause));
        Span<byte> itemBytes = stackalloc byte[16];
        if (!state.PausedTemporaryItem.TryWriteBytes(itemBytes))
            throw new InvalidOperationException(
                "Auto Items could not project its exact paused temporary-item UUID.");
        output.Add(
            new ServiceProjectionKey(TemporaryPauseGuidLowKey),
            Integer(BinaryPrimitives.ReadInt64LittleEndian(itemBytes.Slice(0, 8))));
        output.Add(
            new ServiceProjectionKey(TemporaryPauseGuidHighKey),
            Integer(BinaryPrimitives.ReadInt64LittleEndian(itemBytes.Slice(8, 8))));
    }

    internal static bool TryReadDecision(
        in ServiceStateProjectionSnapshot projection,
        out AutoItemsDecisionKind kind,
        out Guid pausedItem,
        out AutoItemsTemporaryPauseCause pauseCause)
    {
        var foundKind = false;
        var pausedLow = 0L;
        var pausedHigh = 0L;
        kind = AutoItemsDecisionKind.Disabled;
        pauseCause = AutoItemsTemporaryPauseCause.None;
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Value.Kind != ServiceProjectionValueKind.Integer) continue;
            switch (entry.Key.Value)
            {
                case DecisionKindKey when entry.Value.Integer is
                    >= (int)AutoItemsDecisionKind.Disabled and
                    <= (int)AutoItemsDecisionKind.TemporaryEffectActive:
                    kind = (AutoItemsDecisionKind)entry.Value.Integer;
                    foundKind = true;
                    break;
                case TemporaryPauseCauseKey when entry.Value.Integer is
                    >= (int)AutoItemsTemporaryPauseCause.None and
                    <= (int)AutoItemsTemporaryPauseCause.MissingEngagementEvidence:
                    pauseCause = (AutoItemsTemporaryPauseCause)entry.Value.Integer;
                    break;
                case TemporaryPauseGuidLowKey:
                    pausedLow = entry.Value.Integer;
                    break;
                case TemporaryPauseGuidHighKey:
                    pausedHigh = entry.Value.Integer;
                    break;
            }
        }
        Span<byte> itemBytes = stackalloc byte[16];
        BinaryPrimitives.WriteInt64LittleEndian(itemBytes.Slice(0, 8), pausedLow);
        BinaryPrimitives.WriteInt64LittleEndian(itemBytes.Slice(8, 8), pausedHigh);
        pausedItem = new Guid(itemBytes);
        return foundKind;
    }

    private static ServiceProjectionValue Integer(long value) =>
        ServiceProjectionValue.FromInteger(value);
}

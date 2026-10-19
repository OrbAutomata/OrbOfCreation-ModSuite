using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbMentor;

internal static class MentorServiceProjection
{
    internal const int LastInputSequenceKey = 10;
    internal const int TotalMissedInputsKey = 11;
    internal const int PlannedActionsKey = 12;
    internal const int RecipientsKey = 13;

    internal static void Write(in MentorCycleState state, ServiceStateProjectionBuilder output)
    {
        output.Add(
            new ServiceProjectionKey(LastInputSequenceKey),
            ServiceProjectionValue.FromInteger(state.LastInputSequence));
        output.Add(
            new ServiceProjectionKey(TotalMissedInputsKey),
            ServiceProjectionValue.FromInteger(state.TotalMissedInputs));
        output.Add(
            new ServiceProjectionKey(PlannedActionsKey),
            ServiceProjectionValue.FromInteger(state.PlannedActions));
        output.Add(
            new ServiceProjectionKey(RecipientsKey),
            ServiceProjectionValue.FromInteger(state.Recipients));
    }
}

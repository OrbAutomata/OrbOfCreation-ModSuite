using OrbModding.Common.Runtime;

namespace OrbMentor;

internal struct MentorCycleState
{
    private MentorCycleState(LifecycleGeneration lifecycle)
    {
        Lifecycle = lifecycle;
        LastInputSequence = 0;
        TotalMissedInputs = 0;
        PlannedActions = 0;
        Recipients = 0;
    }

    public LifecycleGeneration Lifecycle { get; private set; }
    public long LastInputSequence { get; private set; }
    public long TotalMissedInputs { get; private set; }
    public int PlannedActions { get; private set; }
    public int Recipients { get; private set; }

    internal static MentorCycleState Create(LifecycleGeneration lifecycle) => new(lifecycle);

    internal void DiscardThrough(long sequence)
    {
        if (sequence > LastInputSequence) LastInputSequence = sequence;
    }

    internal void Observe(long sequence, long missedInputs, int plannedActions, int recipients)
    {
        LastInputSequence = sequence;
        TotalMissedInputs += missedInputs;
        PlannedActions = plannedActions;
        Recipients = recipients;
    }
}

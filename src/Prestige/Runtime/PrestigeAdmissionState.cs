namespace OrbAutomata;

internal readonly struct PrestigeAdmissionState
{
    internal PrestigeAdmissionState(long lifecycleEpoch,
        bool worldCycleComplete, bool challengesFetched)
    {
        LifecycleEpoch = lifecycleEpoch;
        WorldCycleComplete = worldCycleComplete;
        ChallengesFetched = challengesFetched;
    }

    internal long LifecycleEpoch { get; }
    internal bool WorldCycleComplete { get; }
    internal bool ChallengesFetched { get; }
}

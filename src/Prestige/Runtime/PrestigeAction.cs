namespace OrbAutomata;

internal readonly struct PrestigeAction
{
    internal PrestigeAction(long lifecycleEpoch) => LifecycleEpoch = lifecycleEpoch;
    internal long LifecycleEpoch { get; }
}

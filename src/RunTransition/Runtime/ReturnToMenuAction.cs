namespace OrbAutomata;

internal readonly struct ReturnToMenuAction
{
    internal ReturnToMenuAction(long lifecycleEpoch) => LifecycleEpoch = lifecycleEpoch;

    internal long LifecycleEpoch { get; }
}

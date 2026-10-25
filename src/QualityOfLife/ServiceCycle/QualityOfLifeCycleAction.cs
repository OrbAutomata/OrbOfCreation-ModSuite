using System;

namespace OrbAutomata;

internal readonly struct QualityOfLifeCycleAction
{
    internal QualityOfLifeCycleAction(Guid passiveId, long lifecycleEpoch)
    {
        PassiveId = passiveId;
        LifecycleEpoch = lifecycleEpoch;
    }
    internal Guid PassiveId { get; }
    internal long LifecycleEpoch { get; }
}

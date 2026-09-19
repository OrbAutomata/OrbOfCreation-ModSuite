namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

public sealed partial class ServiceCycleSemanticRecorder
{
    /// <summary>
    /// Records what one world-collection pass spent on one category. The numbers are the pass's own —
    /// nothing here measures anything.
    /// </summary>
    internal void WorldCategoryCollected(
        int category,
        int sampled,
        int passCategories,
        ulong lifecycle,
        long frameIdentity,
        MonotonicTimestamp observedAt,
        MonotonicDuration elapsed) =>
        _world.WorldCategoryCollected(
            category,
            sampled,
            passCategories,
            lifecycle,
            frameIdentity,
            observedAt,
            elapsed);
}

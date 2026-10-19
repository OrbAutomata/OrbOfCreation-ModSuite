#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>The runtime's own heap readings, as the host process exposes them.</summary>
/// <remarks>
/// Every read is an allocation-free call, made on the pump's thread a few times per pump, except the
/// object census, which walks every live host object and runs once per census interval.
/// </remarks>
internal interface ISuiteFramePumpHeapCounters
{
    int ReadCollectionCount();

    long ReadUsedHeapBytes();

    /// <summary>The heap the runtime has reserved from the OS, used or not.</summary>
    long ReadReservedHeapBytes();

    /// <summary>The previous frame's allocation total, where the host provides one.</summary>
    bool TryReadPreviousFrameAllocated(out long bytes);

    NativeObjectCounts CountNativeObjects();
}

/// <summary>
/// How many host objects are alive: the two kinds that carry managed fields, and the scene objects
/// an instantiation creates.
/// </summary>
internal readonly record struct NativeObjectCounts(
    int ScriptableObjects,
    int GameObjects,
    int MonoBehaviours);
#endif

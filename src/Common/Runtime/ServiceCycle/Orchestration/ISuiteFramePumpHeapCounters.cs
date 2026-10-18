#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

/// <summary>The runtime's own heap readings, as the host process exposes them.</summary>
/// <remarks>
/// Every read is an allocation-free call, made on the pump's thread a few times per pump.
/// </remarks>
internal interface ISuiteFramePumpHeapCounters
{
    int ReadCollectionCount();

    long ReadUsedHeapBytes();

    /// <summary>The previous frame's allocation total, where the host provides one.</summary>
    bool TryReadPreviousFrameAllocated(out long bytes);
}
#endif

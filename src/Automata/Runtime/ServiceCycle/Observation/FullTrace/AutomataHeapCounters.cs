#if SERVICE_CYCLE_PROFILE
using System;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Profiling;

namespace OrbAutomata;

/// <summary>The Mono runtime's heap readings, for the profiling trace's heap samples.</summary>
/// <remarks>
/// Unity's "GC Allocated In Frame" counter is the direct answer where a player provides it, which a
/// non-development player may not; it is used only when Unity hands back a valid recorder.
/// </remarks>
internal sealed class AutomataHeapCounters : ISuiteFramePumpHeapCounters, IDisposable
{
    private const string FrameAllocatedCounter = "GC Allocated In Frame";

    private ProfilerRecorder _frameAllocated;
    private bool _hasFrameAllocated;

    private AutomataHeapCounters(ProfilerRecorder frameAllocated, bool hasFrameAllocated)
    {
        _frameAllocated = frameAllocated;
        _hasFrameAllocated = hasFrameAllocated;
    }

    internal bool HasFrameAllocated => _hasFrameAllocated;

    internal static AutomataHeapCounters Create()
    {
        var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, FrameAllocatedCounter);
        if (recorder.Valid) return new AutomataHeapCounters(recorder, hasFrameAllocated: true);
        recorder.Dispose();
        return new AutomataHeapCounters(default, hasFrameAllocated: false);
    }

    public int ReadCollectionCount() => GC.CollectionCount(0);

    public long ReadUsedHeapBytes() => Profiler.GetMonoUsedSizeLong();

    public long ReadReservedHeapBytes() => Profiler.GetMonoHeapSizeLong();

    public NativeObjectCounts CountNativeObjects() =>
        new(
            Resources.FindObjectsOfTypeAll(typeof(ScriptableObject)).Length,
            Resources.FindObjectsOfTypeAll(typeof(GameObject)).Length,
            Resources.FindObjectsOfTypeAll(typeof(MonoBehaviour)).Length);

    public bool TryReadPreviousFrameAllocated(out long bytes)
    {
        bytes = _hasFrameAllocated ? _frameAllocated.LastValue : 0;
        return _hasFrameAllocated;
    }

    public void Dispose()
    {
        if (!_hasFrameAllocated) return;
        _hasFrameAllocated = false;
        _frameAllocated.Dispose();
    }
}
#endif

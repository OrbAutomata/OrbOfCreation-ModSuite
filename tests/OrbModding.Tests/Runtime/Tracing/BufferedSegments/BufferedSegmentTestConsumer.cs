using OrbModding.TestSupport;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.Tracing.BufferedSegments;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing.BufferedSegments;

internal sealed class BufferedSegmentTestConsumer : IBufferedSegmentConsumer<int>, IDisposable
{
    private readonly object _gate = new();
    private readonly List<WrittenTestSegment> _segments = new();
    private readonly bool _failInitialization;
    private readonly bool _failWrite;
    private readonly bool _failCompletion;
    private BufferedSegmentCompletion _completion;

    internal BufferedSegmentTestConsumer(
        bool blockInitialization = false,
        bool blockWrites = false,
        bool failInitialization = false,
        bool failWrite = false,
        bool failCompletion = false)
    {
        _failInitialization = failInitialization;
        _failWrite = failWrite;
        _failCompletion = failCompletion;
        if (!blockInitialization) InitializationRelease.Set();
        if (!blockWrites) WriteRelease.Set();
    }

    internal ManualResetEventSlim InitializationEntered { get; } = new(false);
    internal ManualResetEventSlim InitializationRelease { get; } = new(false);
    internal ManualResetEventSlim WriteEntered { get; } = new(false);
    internal ManualResetEventSlim WriteRelease { get; } = new(false);
    internal ManualResetEventSlim CompletionObserved { get; } = new(false);

    internal IReadOnlyList<WrittenTestSegment> Segments
    {
        get { lock (_gate) return _segments.ToArray(); }
    }

    internal BufferedSegmentCompletion Completion => _completion;

    public void Initialize()
    {
        InitializationEntered.Set();
        InitializationRelease.Wait();
        if (_failInitialization) throw new InvalidOperationException("scripted initialization failure");
    }

    public int Write(long blockOrdinal, long firstRecordSequence, ReadOnlySpan<int> records)
    {
        WriteEntered.Set();
        WriteRelease.Wait();
        if (_failWrite) throw new InvalidOperationException("scripted write failure");
        lock (_gate)
            _segments.Add(new WrittenTestSegment(
                blockOrdinal,
                firstRecordSequence,
                records.ToArray(),
                Environment.CurrentManagedThreadId));
        return checked(records.Length * sizeof(int));
    }

    public void Complete(in BufferedSegmentCompletion completion)
    {
        _completion = completion;
        CompletionObserved.Set();
        if (_failCompletion) throw new InvalidOperationException("scripted completion failure");
    }

    public void Dispose()
    {
        InitializationRelease.Set();
        WriteRelease.Set();
    }
}

internal readonly record struct WrittenTestSegment(
    long Ordinal,
    long FirstRecordSequence,
    int[] Records,
    int ThreadId);

internal static class BufferedSegmentTestWait
{

    internal static void ForStatus<TRecord>(
        BufferedSegmentSink<TRecord> sink,
        BufferedSegmentStatus expected)
        where TRecord : struct
    {
        Assert.True(
            ServiceCycleTestDeadline.PollUntil(() => sink.Metrics().Status == expected),
            $"Expected {expected}; observed {sink.Metrics().Status}.");
    }

    internal static void ForSignal(ManualResetEventSlim signal, string description) =>
        Assert.True(signal.Wait(ServiceCycleTestDeadline.Value), $"Timed out waiting for {description}.");

}

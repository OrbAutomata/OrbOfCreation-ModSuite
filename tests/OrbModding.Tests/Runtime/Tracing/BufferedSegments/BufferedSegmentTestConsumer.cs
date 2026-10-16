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
    /// <summary>
    /// How long a test waits on the sink's writer thread before calling it hung.
    /// </summary>
    /// <remarks>
    /// A hang detector, not a latency budget. Every wait built on this waits for work a deliberately
    /// <see cref="ThreadPriority.Lowest"/> writer thread performs, so the deadline is not part of
    /// what the test asserts, and a healthy wait returns the moment its condition holds — the gate
    /// costs the same either way. Two seconds asserted something no test meant to assert: that this
    /// machine was not busy at that moment. Generous, but far inside the gate's own per-attempt
    /// deadline, so a genuinely hung writer reports the named expectation it never reached rather
    /// than an anonymous gate timeout.
    /// </remarks>
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(15);

    internal static void ForStatus<TRecord>(
        BufferedSegmentSink<TRecord> sink,
        BufferedSegmentStatus expected)
        where TRecord : struct
    {
        Assert.True(
            PollUntil(() => sink.Metrics().Status == expected),
            $"Expected {expected}; observed {sink.Metrics().Status}.");
    }

    internal static void ForSignal(ManualResetEventSlim signal, string description) =>
        Assert.True(signal.Wait(Deadline), $"Timed out waiting for {description}.");

    /// <summary>
    /// Polls a sink condition to the shared <see cref="Deadline"/>, asleep rather than spinning,
    /// because the awaited work runs on the sink's lowest-priority writer thread and a hot spinner
    /// competes with it for the core it needs.
    /// </summary>
    internal static bool PollUntil(Func<bool> condition)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < Deadline)
        {
            if (condition()) return true;
            Thread.Sleep(1);
        }

        return condition();
    }
}

using System;
using System.Threading;
using System.Collections.Generic;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing;

public sealed class ServiceCycleEventRingTests
{
    [Fact]
    public void OverwriteReportsExactDropRangeAndResidentWindow()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 3);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        for (var i = 0; i < 5; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);

        var output = new ServiceCycleSemanticEvent[3];
        var drain = ring.DrainSince(default, output);

        Assert.Equal(3, drain.Copied);
        Assert.Equal((ulong)3, output[0].Id.Sequence);
        Assert.Equal((ulong)5, output[2].Id.Sequence);
        Assert.Equal((ulong)1, drain.Dropped.FirstSequence);
        Assert.Equal((ulong)2, drain.Dropped.LastSequence);
        Assert.Equal((ulong)2, drain.Dropped.Count);
        Assert.Equal(2UL, drain.OverwrittenTotal);
        Assert.Equal((ulong)1, ring.OverwrittenRange.FirstSequence);
        Assert.Equal((ulong)2, ring.OverwrittenRange.LastSequence);
        Assert.False(drain.IsComplete);
        Assert.False(drain.HasMore);
        Assert.Equal((ulong)5, drain.Cursor.Sequence);
    }

    [Fact]
    public void PartialDrainsAdvanceOnlyPastCopiedEvents()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 4);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        for (var i = 0; i < 3; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        var one = new ServiceCycleSemanticEvent[1];

        var first = ring.DrainSince(default, one);
        var second = ring.DrainSince(first.Cursor, one);
        var third = ring.DrainSince(second.Cursor, one);

        Assert.True(first.HasMore);
        Assert.Equal((ulong)1, oneSequence(first, 1));
        Assert.Equal((ulong)2, oneSequence(second, 2));
        Assert.Equal((ulong)3, oneSequence(third, 3));
        Assert.False(third.HasMore);

        static ulong oneSequence(ServiceCycleEventDrain drain, ulong expected)
        {
            Assert.Equal(expected, drain.Cursor.Sequence);
            return drain.Cursor.Sequence;
        }
    }

    [Fact]
    public void ForeignAndFutureCursorsFailClosed()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        Assert.Throws<ArgumentException>(() => ring.DrainSince(
            new ServiceCycleTraceCursor(new ServiceCycleTraceSessionId(999), 0),
            Span<ServiceCycleSemanticEvent>.Empty));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.DrainSince(
            new ServiceCycleTraceCursor(ServiceCycleTraceFixtures.Session, 1),
            Span<ServiceCycleSemanticEvent>.Empty));
    }

    [Fact]
    public void AccessFromAnotherThreadIsRejected()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try { ring.DrainSince(default, Span<ServiceCycleSemanticEvent>.Empty); }
            catch (Exception exception) { observed = exception; }
        });
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread drain probe did not complete.");
        Assert.IsType<InvalidOperationException>(observed);
    }

    [Fact]
    public void EveryMutableViewGetterIsOwnerGuarded()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        var observed = new List<Exception>();
        var thread = new Thread(() =>
        {
            Try(() => _ = ring.Session); Try(() => _ = ring.Capacity); Try(() => _ = ring.Count);
            Try(() => _ = ring.OverwrittenTotal); Try(() => _ = ring.OverwrittenRange); Try(() => _ = ring.Cursor);
            void Try(Action action) { try { action(); } catch (Exception ex) { observed.Add(ex); } }
        });
        thread.Start();
        Assert.True(
            thread.Join(TimeSpan.FromSeconds(2)),
            "The foreign-thread view probe did not complete.");
        Assert.Equal(6, observed.Count);
        Assert.All(observed, exception => Assert.IsType<InvalidOperationException>(exception));
    }

    [Fact]
    public void ExhaustedSequenceAppendIsFailureAtomic()
    {
        var ring = ServiceCycleEventRing.AtExhaustedSequenceForTests(ServiceCycleTraceFixtures.Session, 2);
        var before = ring.Cursor;
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        Assert.Throws<InvalidOperationException>(() => ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload));
        Assert.Equal(before, ring.Cursor);
        Assert.Equal(0, ring.Count);
        Assert.Equal(0UL, ring.OverwrittenTotal);
    }

    [Fact]
    public void MaximumSequenceBoundaryMatchesRingDomainAndFutureCursorFailsSafely()
    {
        var maximum = ServiceCycleTraceEventId.MaximumSequence;
        var id = new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, maximum);
        var drop = new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, maximum);
        Assert.True(id.IsValid);
        Assert.Equal(maximum, drop.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, ulong.MaxValue));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, ulong.MaxValue));

        var ring = ServiceCycleEventRing.AtExhaustedSequenceForTests(ServiceCycleTraceFixtures.Session, 2);
        Assert.Equal(maximum, ring.Cursor.Sequence);
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.DrainSince(
            new ServiceCycleTraceCursor(ServiceCycleTraceFixtures.Session, ulong.MaxValue),
            Span<ServiceCycleSemanticEvent>.Empty));
    }

    [Fact]
    public void CaptureLatchesLossAcrossPartialDrains()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        for (var i = 0; i < 3; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        var capture = new ServiceCycleTraceCapture(ServiceCycleTraceFixtures.Session, 2, 7);
        capture.Pull(ring, 1);
        capture.Pull(ring, 1);
        Assert.False(capture.IsComplete);
        Assert.Equal(1UL, capture.Dropped.FirstSequence);
        var bytes = new byte[capture.GetEncodedLength()];
        capture.Encode(bytes);
        var decoded = ServiceCycleTraceCodec.Decode(bytes);
        Assert.False(decoded.IsComplete);
        Assert.Equal(2, decoded.Count);
        Assert.Equal(2UL, decoded[0].Id.Sequence);
    }

    [Fact]
    public void CaptureResetsToNewestContiguousSuffixAcrossRepeatedMiddleLoss()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        for (var i = 0; i < 3; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        var capture = new ServiceCycleTraceCapture(ServiceCycleTraceFixtures.Session, 4, 7);
        capture.Pull(ring, 1); // dropped 1, retained 2

        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        capture.Pull(ring, 1); // 3 was lost: discard 2, retain 4, report 1..3
        Assert.Equal(1, capture.Count);
        Assert.Equal(1UL, capture.Dropped.FirstSequence);
        Assert.Equal(3UL, capture.Dropped.LastSequence);

        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        capture.Pull(ring, 1); // 5 was lost: discard 4, retain 6, report 1..5
        Assert.Equal(5UL, capture.Dropped.LastSequence);
        var bytes = new byte[capture.GetEncodedLength()];
        capture.Encode(bytes);
        var decoded = ServiceCycleTraceCodec.Decode(bytes);
        Assert.False(decoded.IsComplete);
        Assert.Equal(1, decoded.Count);
        Assert.Equal(6UL, decoded[0].Id.Sequence);
    }

    [Fact]
    public void CaptureDiscardsCompleteRetainedPrefixWhenLaterMiddleIsLost()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 4);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        for (var i = 0; i < 3; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        var capture = new ServiceCycleTraceCapture(ServiceCycleTraceFixtures.Session, 6, 7);
        capture.Pull(ring, 3);
        Assert.True(capture.IsComplete);

        for (var i = 0; i < 5; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        capture.Pull(ring, 1); // sequence 4 was lost; retain newest resident sequence 5 only

        Assert.False(capture.IsComplete);
        Assert.Equal(1UL, capture.Dropped.FirstSequence);
        Assert.Equal(4UL, capture.Dropped.LastSequence);
        var bytes = new byte[capture.GetEncodedLength()];
        capture.Encode(bytes);
        var decoded = ServiceCycleTraceCodec.Decode(bytes);
        Assert.Equal(1, decoded.Count);
        Assert.Equal(5UL, decoded[0].Id.Sequence);
        Assert.Equal(4UL, decoded.Dropped.LastSequence);
    }

    [Fact]
    public void FullCaptureBufferStillResetsSafelyWhenRingOverwritesLaterEvents()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        var capture = new ServiceCycleTraceCapture(ServiceCycleTraceFixtures.Session, 2, 7);
        capture.Pull(ring, 2);
        Assert.Equal(2, capture.Count);

        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        capture.Pull(ring, 2);

        Assert.Equal(2, capture.Count);
        Assert.Equal(3UL, capture.Dropped.LastSequence);
        var bytes = new byte[capture.GetEncodedLength()];
        capture.Encode(bytes);
        var decoded = ServiceCycleTraceCodec.Decode(bytes);
        Assert.Equal(4UL, decoded[0].Id.Sequence);
        Assert.Equal(5UL, decoded[1].Id.Sequence);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void WarmRepeatedMiddleLossCaptureAllocatesNothing()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 2);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        for (var i = 0; i < 3; i++) ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        var capture = new ServiceCycleTraceCapture(ServiceCycleTraceFixtures.Session, 2, 7);
        capture.Pull(ring, 1);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
        capture.Pull(ring, 1);

        var allocated = AllocationProbe.MeasureRepeated(100, () =>
        {
            ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
            ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
            capture.Pull(ring, 1);
        });

        Assert.Equal(0, allocated);
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void WarmAppendOverwriteAndDrainAllocateNothing()
    {
        var ring = new ServiceCycleEventRing(ServiceCycleTraceFixtures.Session, 8);
        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        var output = new ServiceCycleSemanticEvent[1];
        for (var i = 0; i < 32; i++)
        {
            ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
            ring.DrainSince(new ServiceCycleTraceCursor(ServiceCycleTraceFixtures.Session, (ulong)Math.Max(0, i)), output);
        }

        var cursor = ring.Cursor;

        var allocated = AllocationProbe.MeasureRepeated(1_000, () =>
        {
            ring.Append(ServiceCycleSemanticEventKind.CycleStarted, in payload);
            cursor = ring.DrainSince(cursor, output).Cursor;
        });

        Assert.Equal(0, allocated);
    }
}

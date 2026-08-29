using System;
using System.Buffers.Binary;
using System.Globalization;
using System.Linq;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.Tracing;
using OrbModding.Common;
using OrbModding.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Tracing;

public sealed class ServiceCycleTraceCodecTests
{
    [Fact]
    public void DecoderRejectsFormerSchemaOneAfterCaptureShapeCorrection()
    {
        var bytes = Encode(new[] { ServiceCycleTraceFixtures.Event(1) });
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        RewriteChecksum(bytes);

        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
    }

    [Fact]
    public void EveryEventKindRoundTripsExactly()
    {
        var events = ServiceCycleTraceFixtures.EveryEventKind();
        var bytes = Encode(events);

        var decoded = ServiceCycleTraceCodec.Decode(bytes);

        Assert.Equal(ServiceCycleTraceCodec.SchemaVersion, decoded.SchemaVersion);
        Assert.Equal(ServiceCycleTraceFixtures.Session, decoded.Session);
        Assert.False(decoded.Dropped.IsPresent);
        Assert.Equal(events.Length, decoded.Count);
        for (var i = 0; i < events.Length; i++) Assert.Equal(events[i], decoded[i]);
    }

    [Theory]
    [InlineData(null, 0, 0, 0)]
    [InlineData((int)NativeMutationOutcome.BeforeCaptureFailed, 0, 0, 0)]
    [InlineData((int)NativeMutationOutcome.ExecutionThrew, 1, 1, 0)]
    [InlineData((int)NativeMutationOutcome.AfterCaptureFailed, 1, 1, 0)]
    [InlineData((int)NativeMutationOutcome.PostconditionFailed, 1, 1, 0)]
    public void FaultedActionOptionalNativeOutcomeRoundTripsExactly(
        int? outcome, long calls, long attempts, long committed)
    {
        var payload = ServiceCycleSemanticPayload.ActionFact(
            in ServiceCycleTraceFixtures.Cycle, 8, 10, 0, 3, 7,
            outcome.HasValue ? (NativeMutationOutcome?)outcome.Value : null,
            calls, attempts, committed, 100, 10, ServiceCycleTraceFixtures.Frame);
        var item = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default,
            ServiceCycleSemanticEventKind.ActionFaulted, in payload);
        var decoded = ServiceCycleTraceCodec.Decode(Encode(new[] { item }));
        Assert.Equal(item, decoded[0]);
        Assert.Equal(outcome.HasValue, decoded[0].Payload.HasNativeOutcome);
    }

    [Fact]
    public void WorldGenerationAndStallCountersSurviveTheRoundTrip()
    {
        var decoded = ServiceCycleTraceCodec.Decode(Encode(ServiceCycleTraceFixtures.EveryEventKind()));

        var cycle = FirstOfKind(decoded, ServiceCycleSemanticEventKind.CycleStarted).Payload;
        Assert.Equal(ServiceCycleTraceFixtures.Cycle.WorldGeneration, cycle.World);
        Assert.Equal(0, cycle.CyclesStarted);
        Assert.Equal(0, cycle.WorldGateDeferrals);

        var pump = FirstOfKind(decoded, ServiceCycleSemanticEventKind.PumpCompleted).Payload;
        Assert.Equal(9, pump.CyclesStarted);
        Assert.Equal(13, pump.WorldGateDeferrals);
        Assert.Equal(0UL, pump.World);
    }

    /// <summary>
    /// A collection span rides slots the record already reserves, so what has to survive the trip is
    /// the reading rather than the bytes: a span decoded as an action count and an occurrence count
    /// would be numerically intact and completely misread.
    /// </summary>
    [Fact]
    public void ACollectionSpanReadsBackAsItsCategorySampledCountAndPassWidth()
    {
        var decoded = ServiceCycleTraceCodec.Decode(Encode(ServiceCycleTraceFixtures.EveryEventKind()));

        var span = FirstOfKind(decoded, ServiceCycleSemanticEventKind.WorldCategoryCollected).Payload;
        Assert.Equal(5, span.WorldCategory);
        Assert.Equal(2_628, span.WorldCategorySampled);
        Assert.Equal(61, span.WorldPassCategories);
        Assert.Equal(10, span.DurationTicks);
        Assert.Equal(2UL, span.Lifecycle);
        Assert.Equal(ServiceCycleTraceFixtures.Frame, span.FrameIdentity);
        // Not a service fact and not a cycle fact: a reader that folded spans into the collection
        // service's cycle rows would report one pass as sixty-odd cycles.
        Assert.Equal(0UL, span.Service);
        Assert.Equal(0UL, span.Cycle);
    }

    /// <summary>
    /// A category that cost this pass nothing is recorded at nothing. Structural categories are read
    /// once per lifecycle epoch and reused after, and re-charging them would render the cheapest
    /// categories of a pass as its dearest.
    /// </summary>
    [Fact]
    public void ASpanForAReusedCategoryCarriesNoDurationAndNoEpochWhenThereIsNone()
    {
        var payload = ServiceCycleSemanticPayload.WorldCategoryFact(
            category: 1, sampled: 0, passCategories: 3, lifecycle: 0, frameIdentity: -1,
            timestampTicks: 100, durationTicks: 0);
        var item = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default,
            ServiceCycleSemanticEventKind.WorldCategoryCollected, in payload);

        var decoded = ServiceCycleTraceCodec.Decode(Encode(new[] { item }));

        Assert.Equal(item, decoded[0]);
        Assert.Equal(0, decoded[0].Payload.DurationTicks);
        Assert.Equal(0, decoded[0].Payload.WorldCategorySampled);
        Assert.True((decoded[0].Payload.Fields & ServiceCycleSemanticFields.Lifecycle) == 0);
        Assert.True((decoded[0].Payload.Fields & ServiceCycleSemanticFields.FrameIdentity) == 0);
    }

    /// <summary>
    /// The identity a span carries is a position in the pass that read it, so a span outside its own
    /// pass is an emitter that lost track of which collection it is describing.
    /// </summary>
    [Fact]
    public void ASpanOutsideItsOwnPassIsRefused()
    {
        var payload = ServiceCycleSemanticPayload.WorldCategoryFact(
            category: 4, sampled: 0, passCategories: 3, lifecycle: 1, frameIdentity: 1,
            timestampTicks: 100, durationTicks: 0);

        Assert.Throws<ArgumentException>(() => new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1), default,
            ServiceCycleSemanticEventKind.WorldCategoryCollected, in payload));
    }

    [Fact]
    public void CaptureAndActionFactsCarryTheFrameTheyRanInsideOrNoneAtAll()
    {
        var decoded = ServiceCycleTraceCodec.Decode(Encode(ServiceCycleTraceFixtures.EveryEventKind()));

        var framed = new[]
        {
            ServiceCycleSemanticEventKind.CaptureStarted,
            ServiceCycleSemanticEventKind.CaptureCompleted,
            ServiceCycleSemanticEventKind.CaptureUnavailable,
            ServiceCycleSemanticEventKind.ActionAttempted,
            ServiceCycleSemanticEventKind.ActionCommitted,
            ServiceCycleSemanticEventKind.ActionSkipped,
            ServiceCycleSemanticEventKind.ActionRejected,
        };
        foreach (var kind in framed)
        {
            var payload = FirstOfKind(decoded, kind).Payload;
            Assert.True((payload.Fields & ServiceCycleSemanticFields.FrameIdentity) != 0, kind.ToString());
            Assert.Equal(ServiceCycleTraceFixtures.Frame, payload.FrameIdentity);
        }

        // A fact the runtime reported from outside any frame claims none: the field stays absent and
        // the offset stays zero, exactly as it did before frames were carried at all.
        foreach (var kind in new[]
                 {
                     ServiceCycleSemanticEventKind.CaptureFaulted,
                     ServiceCycleSemanticEventKind.ActionFaulted,
                 })
        {
            var payload = FirstOfKind(decoded, kind).Payload;
            Assert.True((payload.Fields & ServiceCycleSemanticFields.FrameIdentity) == 0, kind.ToString());
            Assert.Equal(0, payload.FrameIdentity);
        }
    }

    [Fact]
    public void EncodingIsCanonicalAcrossCultures()
    {
        var events = ServiceCycleTraceFixtures.EveryEventKind();
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            var german = Encode(events);
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR");
            CultureInfo.CurrentUICulture = new CultureInfo("tr-TR");
            var turkish = Encode(events);
            Assert.Equal(german, turkish);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }

    [Fact]
    public void EveryTruncatedBoundaryFailsClosed()
    {
        var bytes = Encode(new[]
        {
            ServiceCycleTraceFixtures.Event(1),
            ServiceCycleTraceFixtures.Event(2, parentSequence: 1),
        });
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes.AsSpan(0, length)));
    }

    [Theory]
    [InlineData(4, 99)]
    [InlineData(6, 1)]
    [InlineData(8, 1)]
    [InlineData(10, 2)]
    [InlineData(12, 0)]
    [InlineData(20, 0)]
    [InlineData(28, 3)]
    [InlineData(32, 1)]
    [InlineData(52, 1)]
    [InlineData(60, 1)]
    [InlineData(64 + 32, 999)]
    [InlineData(64 + 36, 1)]
    [InlineData(64 + 188, 1)]
    [InlineData(64 + 272, 0)]
    [InlineData(64 + 280, 5)]
    [InlineData(64 + 284, 5)]
    public void CorruptHeaderRecordAndEnumFieldsFailClosed(int offset, int value)
    {
        var bytes = Encode(new[] { ServiceCycleTraceFixtures.Event(1) });
        if (offset is 4 or 6 or 8 or 10) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset, 2), (ushort)value);
        else if (offset is 12 or 20) BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(offset, 8), (ulong)value);
        else BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(offset, 4), value);
        RewriteChecksum(bytes);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
    }

    [Fact]
    public void ForeignSchemaChecksumTrailingBytesAndOversizedCountFailClosed()
    {
        var valid = Encode(new[] { ServiceCycleTraceFixtures.Event(1) });

        var schema = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(
            schema.AsSpan(4, 2),
            checked((ushort)(ServiceCycleTraceCodec.SchemaVersion + 1)));
        RewriteChecksum(schema);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(schema));

        var checksum = (byte[])valid.Clone();
        checksum[^1] ^= 0x80;
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(checksum));

        var trailing = valid.Concat(new byte[] { 0 }).ToArray();
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(trailing));

        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(valid, maximumRecords: 0));
    }

    [Fact]
    public void DropMetadataMustBeExactAndInternallyCoherent()
    {
        var valid = Encode(new[] { ServiceCycleTraceFixtures.Event(3) },
            new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, 2));

        var missingFlag = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt16LittleEndian(missingFlag.AsSpan(10, 2), 0);
        RewriteChecksum(missingFlag);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(missingFlag));

        var reverse = (byte[])valid.Clone();
        BinaryPrimitives.WriteUInt64LittleEndian(reverse.AsSpan(36, 8), 4);
        BinaryPrimitives.WriteUInt64LittleEndian(reverse.AsSpan(44, 8), 2);
        RewriteChecksum(reverse);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(reverse));
    }

    [Fact]
    public void RecomputedCrcCannotImportUnrepresentableMaximumSequence()
    {
        var allDropped = Encode(Array.Empty<ServiceCycleSemanticEvent>(),
            new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1,
                ServiceCycleTraceEventId.MaximumSequence));
        BinaryPrimitives.WriteUInt64LittleEndian(allDropped.AsSpan(44, 8), ulong.MaxValue);
        RewriteChecksum(allDropped);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(allDropped));

        var resident = ServiceCycleTraceFixtures.Event(ServiceCycleTraceEventId.MaximumSequence);
        var withResident = Encode(new[] { resident },
            new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1,
                ServiceCycleTraceEventId.MaximumSequence - 1));
        BinaryPrimitives.WriteUInt64LittleEndian(withResident.AsSpan(20, 8), ulong.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(withResident.AsSpan(64 + 8, 8), ulong.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(withResident.AsSpan(44, 8),
            ServiceCycleTraceEventId.MaximumSequence);
        RewriteChecksum(withResident);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(withResident));
    }

    [Fact]
    public void EveryEventKindRejectsAnUnknownPayloadField()
    {
        foreach (var item in ServiceCycleTraceFixtures.EveryEventKind())
        {
            var payload = item.Payload;
            var bytes = Encode(new[] { new ServiceCycleSemanticEvent(
                new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1),
                default,
                item.Kind,
                in payload) });
            var fields = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(64 + 40, 8));
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 40, 8), fields | (1UL << 63));
            RewriteChecksum(bytes);
            Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
        }
    }

    [Theory]
    [InlineData(ServiceCycleSemanticEventKind.CycleQueued, 160, 4)]
    [InlineData(ServiceCycleSemanticEventKind.CaptureCompleted, 160, 3)]
    [InlineData(ServiceCycleSemanticEventKind.CaptureUnavailable, 160, 2)]
    [InlineData(ServiceCycleSemanticEventKind.ActionCommitted, 160, 2)]
    [InlineData(ServiceCycleSemanticEventKind.ActionCommitted, 144, -1)]
    [InlineData(ServiceCycleSemanticEventKind.ActionCommitted, 188, 2)]
    [InlineData(ServiceCycleSemanticEventKind.ActionCommitted, 208, 0)]
    [InlineData(ServiceCycleSemanticEventKind.ActionRejected, 160, 1)]
    [InlineData(ServiceCycleSemanticEventKind.ActionFaulted, 160, 5)]
    [InlineData(ServiceCycleSemanticEventKind.ActionFaulted, 188, 0)]
    [InlineData(ServiceCycleSemanticEventKind.ActionFaulted, 188, 6)]
    [InlineData(ServiceCycleSemanticEventKind.ActionFaulted, 200, 0)]
    [InlineData(ServiceCycleSemanticEventKind.ActionFaulted, 208, 1)]
    [InlineData(ServiceCycleSemanticEventKind.BatchCompleted, 160, 2)]
    [InlineData(ServiceCycleSemanticEventKind.BatchCompleted, 160, 1024)]
    [InlineData(ServiceCycleSemanticEventKind.BatchCompleted, 200, 2)]
    [InlineData(ServiceCycleSemanticEventKind.BatchAborted, 160, 1)]
    [InlineData(ServiceCycleSemanticEventKind.FaultObserved, 160, 5)]
    public void RecomputedCrcCannotSmuggleInvalidKindSpecificPayloads(
        ServiceCycleSemanticEventKind kind, int recordOffset, long value)
    {
        var bytes = Encode(new[] { ServiceCycleTraceFixtures.Event(1, kind) });
        if (recordOffset is 160 or 164 or 168 or 172 or 176 or 180 or 184)
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + recordOffset, 4), checked((int)value));
        else
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(64 + recordOffset, 8), value);
        RewriteChecksum(bytes);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
    }

    [Fact]
    public void RecomputedCrcCannotReviveRetiredPublicationAccounting()
    {
        var bytes = Encode(new[] {
            ServiceCycleTraceFixtures.Event(1, ServiceCycleSemanticEventKind.BatchCompleted),
        });
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 36, 4), 1);
        RewriteChecksum(bytes);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
    }

    [Theory]
    [InlineData(ServiceCycleSemanticEventKind.ActionRejected)]
    [InlineData(ServiceCycleSemanticEventKind.ActionAttempted)]
    [InlineData(ServiceCycleSemanticEventKind.BatchCompleted)]
    public void RecomputedCrcCannotSmuggleNativeOutcomeIntoKindsWithoutIndividualEvidence(
        ServiceCycleSemanticEventKind kind)
    {
        var bytes = Encode(new[] { ServiceCycleTraceFixtures.Event(1, kind) });
        var fields = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(64 + 40, 8));
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 40, 8),
            fields | (ulong)ServiceCycleSemanticFields.NativeMutationOutcome);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(64 + 188, 4),
            (int)NativeMutationOutcome.ExecutionThrew + 1);
        RewriteChecksum(bytes);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
    }

    [Fact]
    [Trait("Category", "PerformanceSimulation")]
    public void WarmCallerBufferedEncodingAllocatesNothing()
    {
        var events = ServiceCycleTraceFixtures.EveryEventKind();
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];

        var allocated = AllocationProbe.MeasureRepeated(
            100,
            () => ServiceCycleTraceCodec.Encode(ServiceCycleTraceFixtures.Session, default, events, bytes));

        Assert.Equal(0, allocated);
    }

    [Fact]
    public void InvalidSequenceAndDropMetadataAreRejectedBeforeDestinationMutation()
    {
        var destination = Enumerable.Repeat((byte)0xa5,
            ServiceCycleTraceCodec.GetEncodedLength(2)).ToArray();
        var baseline = (byte[])destination.Clone();
        var discontinuous = new[]
        {
            ServiceCycleTraceFixtures.Event(1),
            ServiceCycleTraceFixtures.Event(3, parentSequence: 1),
        };

        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session, default, discontinuous, destination));
        Assert.Equal(baseline, destination);

        var one = new[] { ServiceCycleTraceFixtures.Event(3, parentSequence: 2) };
        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session,
            new ServiceCycleTraceDropRange(ServiceCycleTraceFixtures.Session, 1, 1),
            one,
            destination));
        Assert.Equal(baseline, destination);

        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session,
            default,
            new[] { ServiceCycleTraceFixtures.Event(3) },
            destination));
        Assert.Equal(baseline, destination);
    }

    [Fact]
    public void EncodeRejectsBackwardTimestampParentBeforeDestinationMutation()
    {
        var destination = Enumerable.Repeat((byte)0xa5,
            ServiceCycleTraceCodec.GetEncodedLength(2)).ToArray();
        var baseline = (byte[])destination.Clone();
        var parentPayload = ServiceCycleSemanticPayload.CycleFact(
            in ServiceCycleTraceFixtures.Cycle,
            CommonServiceDecisionCodes.Ready.Value,
            30,
            0);
        var childPayload = ServiceCycleSemanticPayload.CycleFact(
            in ServiceCycleTraceFixtures.Cycle,
            0,
            20,
            0);
        var parent = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1),
            default,
            ServiceCycleSemanticEventKind.CycleQueued,
            in parentPayload);
        var child = new ServiceCycleSemanticEvent(
            new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 2),
            parent.Id,
            ServiceCycleSemanticEventKind.CycleStarted,
            in childPayload);

        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session,
            default,
            new[] { parent, child },
            destination));
        Assert.Equal(baseline, destination);
    }

    [Fact]
    public void EncodeRejectsUncheckedMaximumIdentitiesBeforeDestinationMutation()
    {
        var destination = Enumerable.Repeat((byte)0xa5,
            ServiceCycleTraceCodec.GetEncodedLength(1)).ToArray();
        var baseline = (byte[])destination.Clone();
        var impossibleDrop = ServiceCycleTraceDropRange.UncheckedForValidationTests(
            ServiceCycleTraceFixtures.Session, 1, ulong.MaxValue);
        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session, impossibleDrop,
            ReadOnlySpan<ServiceCycleSemanticEvent>.Empty, destination));
        Assert.Equal(baseline, destination);

        var payload = ServiceCycleTraceFixtures.Payload(ServiceCycleSemanticEventKind.CycleStarted);
        var impossibleId = ServiceCycleTraceEventId.UncheckedForValidationTests(
            ServiceCycleTraceFixtures.Session, ulong.MaxValue);
        var impossibleEvent = ServiceCycleSemanticEvent.UncheckedForValidationTests(
            impossibleId, ServiceCycleSemanticEventKind.CycleStarted, in payload);
        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session, default, new[] { impossibleEvent }, destination));
        Assert.Equal(baseline, destination);

        var validId = new ServiceCycleTraceEventId(ServiceCycleTraceFixtures.Session, 1);
        var impossibleParent = ServiceCycleTraceEventId.UncheckedForValidationTests(
            ServiceCycleTraceFixtures.Session, ulong.MaxValue);
        var impossibleParentEvent = ServiceCycleSemanticEvent.UncheckedForValidationTests(
            validId, impossibleParent, ServiceCycleSemanticEventKind.CycleStarted, in payload);
        Assert.Throws<ArgumentException>(() => ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session, default, new[] { impossibleParentEvent }, destination));
        Assert.Equal(baseline, destination);
    }

    [Fact]
    public void DecodeRejectsInvalidCausalGraphsRatherThanReturningUncheckedDocuments()
    {
        var baseline = Encode(new[]
        {
            ServiceCycleTraceFixtures.Event(1),
            ServiceCycleTraceFixtures.Event(2, parentSequence: 1),
        });

        AssertGraphMutationRejected(baseline, bytes =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 16, 8), ServiceCycleTraceFixtures.Session.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 24, 8), 1);
        });
        AssertGraphMutationRejected(baseline, bytes =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 16, 8), ServiceCycleTraceFixtures.Session.Value);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + 24, 8), 2);
        });
        AssertGraphMutationRejected(baseline, bytes =>
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + ServiceCycleTraceCodec.RecordBytes + 16, 8), 999));
        AssertGraphMutationRejected(baseline, bytes =>
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + ServiceCycleTraceCodec.RecordBytes + 8, 8), 1));
        AssertGraphMutationRejected(baseline, bytes =>
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(64 + ServiceCycleTraceCodec.RecordBytes + 8, 8), 3));
    }

    private static byte[] Encode(
        ServiceCycleSemanticEvent[] events,
        ServiceCycleTraceDropRange dropped = default)
    {
        var bytes = new byte[ServiceCycleTraceCodec.GetEncodedLength(events.Length)];
        Assert.Equal(bytes.Length, ServiceCycleTraceCodec.Encode(
            ServiceCycleTraceFixtures.Session, dropped, events, bytes));
        return bytes;
    }

    private static ServiceCycleSemanticEvent FirstOfKind(
        ServiceCycleTraceDocument document,
        ServiceCycleSemanticEventKind kind)
    {
        for (var index = 0; index < document.Count; index++)
            if (document[index].Kind == kind) return document[index];
        throw new InvalidOperationException($"The decoded trace carries no {kind} event.");
    }

    private static void RewriteChecksum(byte[] bytes)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56, 4),
            TraceCrc32.ComputeExcluding(bytes, 56, 4));
    }

    private static void AssertGraphMutationRejected(byte[] baseline, Action<byte[]> mutate)
    {
        var bytes = (byte[])baseline.Clone();
        mutate(bytes);
        RewriteChecksum(bytes);
        Assert.Throws<FormatException>(() => ServiceCycleTraceCodec.Decode(bytes));
    }
}

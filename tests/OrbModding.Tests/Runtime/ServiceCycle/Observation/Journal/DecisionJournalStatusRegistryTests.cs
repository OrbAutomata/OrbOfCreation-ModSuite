using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using Xunit;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Journal;

public sealed class DecisionJournalStatusRegistryTests
{
    [Fact]
    public void RegistrationPublishesOnlyChangedMachineNeutralStatus()
    {
        var registry = new DecisionJournalStatusRegistry();
        Assert.Equal(DecisionJournalStatusState.Unavailable, registry.Status.State);
        Assert.Equal(0, registry.Revision);

        using var registration = registry.Register();
        var status = Recording();
        Assert.True(registration.Publish(status));
        Assert.False(registration.Publish(status));
        Assert.Equal(status, registry.Status);
        Assert.Equal(1, registry.Revision);

        registration.Dispose();
        Assert.Equal(DecisionJournalStatusState.Unavailable, registry.Status.State);
        Assert.Equal(2, registry.Revision);
        Assert.Throws<ObjectDisposedException>(() => registration.Publish(status));
    }

    [Fact]
    public void StatusRejectsPathsAndContradictoryMetrics()
    {
        Assert.Throws<ArgumentException>(() => Status(
            DecisionJournalStatusState.Recording,
            DecisionJournalStatusResult.None,
            firstIncompleteSequence: 0,
            artifactName: "private/journal"));
        Assert.Throws<ArgumentException>(() => Status(
            DecisionJournalStatusState.Faulted,
            DecisionJournalStatusResult.None,
            firstIncompleteSequence: 0,
            artifactName: "journal"));
        Assert.Throws<ArgumentException>(() => Status(
            DecisionJournalStatusState.Recording,
            DecisionJournalStatusResult.WriteFailed,
            firstIncompleteSequence: 2,
            artifactName: "journal"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionJournalStatus(
            DecisionJournalStatusState.Recording,
            acceptedRecords: 2,
            writtenRecords: 2,
            discardedRecords: 1,
            bytesWritten: 1,
            writtenSegments: 1,
            retainedSegments: 1,
            evictedSegments: 0,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            pendingBlocks: 0,
            peakPendingBlocks: 0,
            firstIncompleteSequence: 0,
            DecisionJournalStatusResult.None,
            "journal"));
        Assert.Throws<ArgumentException>(() => new DecisionJournalStatus(
            DecisionJournalStatusState.Stopped,
            acceptedRecords: 3,
            writtenRecords: 2,
            discardedRecords: 0,
            bytesWritten: 120,
            writtenSegments: 1,
            retainedSegments: 1,
            evictedSegments: 0,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            pendingBlocks: 0,
            peakPendingBlocks: 1,
            firstIncompleteSequence: 0,
            DecisionJournalStatusResult.None,
            "journal"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DecisionJournalStatus(
            DecisionJournalStatusState.Recording,
            acceptedRecords: 0,
            writtenRecords: 0,
            discardedRecords: 0,
            bytesWritten: 0,
            writtenSegments: 1,
            retainedSegments: 0,
            evictedSegments: 0,
            startupPrunedSegments: 0,
            incompatibleSegmentsPruned: 0,
            staleTemporaryFilesRemoved: 0,
            pendingBlocks: 0,
            peakPendingBlocks: 0,
            firstIncompleteSequence: 0,
            DecisionJournalStatusResult.None,
            "journal"));
    }

    [Fact]
    public void RegistryRejectsCrossThreadAccessAndDuplicateProducer()
    {
        var registry = new DecisionJournalStatusRegistry();
        using var registration = registry.Register();
        Assert.False(registry.TryRegister(out _));
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { _ = registry.Status; }
            catch (Exception exception) { failure = exception; }
        });

        thread.Start();
        Assert.True(thread.Join(ServiceCycleTestDeadline.Value));

        Assert.IsType<InvalidOperationException>(failure);
    }

    private static DecisionJournalStatus Recording() => Status(
        DecisionJournalStatusState.Recording,
        DecisionJournalStatusResult.None,
        firstIncompleteSequence: 0,
        artifactName: "journal");

    private static DecisionJournalStatus Status(
        DecisionJournalStatusState state,
        DecisionJournalStatusResult result,
        long firstIncompleteSequence,
        string artifactName) => new(
        state,
        acceptedRecords: 3,
        writtenRecords: 2,
        discardedRecords: 0,
        bytesWritten: 120,
        writtenSegments: 1,
        retainedSegments: 1,
        evictedSegments: 0,
        startupPrunedSegments: 0,
        incompatibleSegmentsPruned: 0,
        staleTemporaryFilesRemoved: 0,
        pendingBlocks: 1,
        peakPendingBlocks: 1,
        firstIncompleteSequence,
        result,
        artifactName);
}

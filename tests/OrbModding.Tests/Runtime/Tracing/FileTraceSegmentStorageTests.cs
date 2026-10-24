using OrbModding.TestSupport;
using System;
using System.IO;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing;

public sealed class FileTraceSegmentStorageTests
{
    [Fact]
    public void FileStorageWritesASegmentViaTempThenAtomicRename()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        var storage = new FileTraceSegmentStorage(directory, filePrefix: "seg", extension: ".octr");

        var segment = storage.BeginSegment(0);
        var payload = new byte[] { 42, 7, 255, 0, 13 };
        storage.Append(segment, payload);

        Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));
        Assert.Single(Directory.GetFiles(directory, "*.tmp-*"));

        storage.CommitSegment(segment);

        var finals = Directory.GetFiles(directory, "seg-*.octr");
        Assert.Single(finals);
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));
        Assert.Equal(payload, File.ReadAllBytes(finals[0]));

        storage.DeleteOldestCommitted();
        Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));


    }

    [Fact]
    public void FileStorageDiscardRemovesTheTempWithoutPublishing()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        var storage = new FileTraceSegmentStorage(directory, filePrefix: "seg", extension: ".octr");
        var segment = storage.BeginSegment(1);
        storage.Append(segment, new byte[] { 1, 2, 3 });

        storage.DiscardSegment(segment);

        Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));
        Assert.Empty(Directory.GetFiles(directory, "*.tmp-*"));


    }

    [Fact]
    public void FileStorageConstructionIsSideEffectFreeAndRejectsPathComponents()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        _ = new FileTraceSegmentStorage(directory, "seg", ".octr");
        Assert.False(Directory.Exists(directory));
        Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "../escape", ".octr"));
        Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "bad/name", ".octr"));
        Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "bad\\name", ".octr"));
        Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "C:escape", ".octr"));
        Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "seg", "../octr"));
        Assert.Throws<ArgumentException>(() => new FileTraceSegmentStorage(directory, "seg", "octr"));


    }

    [Fact]
    public void FileStorageReconcilePrunesOldestResumesOrdinalAndCleansOwnedTemporaryFiles()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        var writer = new FileTraceSegmentStorage(directory, "seg", ".octr");
        for (var ordinal = 0; ordinal < 3; ordinal++)
        {
            var segment = writer.BeginSegment(ordinal);
            writer.Append(segment, new[] { (byte)ordinal });
            writer.CommitSegment(segment);
        }
        var stale = Path.Combine(
            directory,
            "seg-000003.octr.tmp-" + Guid.NewGuid().ToString("N"));
        File.WriteAllBytes(stale, new byte[] { 9 });
        var unrelated = Path.Combine(directory, "keep-me.txt");
        File.WriteAllText(unrelated, "unrelated");

        var recovered = new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2);

        Assert.Equal(3, recovered.NextOrdinal);
        Assert.Equal(2, recovered.RetainedSegments);
        Assert.Equal(1, recovered.StartupPrunedSegments);
        Assert.Equal(1, recovered.StaleTemporaryFilesRemoved);
        Assert.False(File.Exists(Path.Combine(directory, "seg-000000.octr")));
        Assert.True(File.Exists(Path.Combine(directory, "seg-000001.octr")));
        Assert.True(File.Exists(Path.Combine(directory, "seg-000002.octr")));
        Assert.True(File.Exists(unrelated));
        Assert.False(File.Exists(stale));


    }

    /// <summary>
    /// A store holding a name this writer cannot read is abandoned, not refused forever.
    /// </summary>
    /// <remarks>
    /// Both of these used to be permanent startup failures: the directory outlives the process, so
    /// every later launch met the same artifact and threw again, and the writer stayed dead on that
    /// machine until someone deleted files by hand. What was discarded is counted rather than
    /// silently dropped.
    /// </remarks>
    [Fact]
    public void FileStorageNeverOverwritesCommittedCollisionAndAbandonsUnreadableNames()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        var storage = new FileTraceSegmentStorage(directory, "seg", ".octr");
        var first = storage.BeginSegment(0);
        storage.Append(first, new byte[] { 1 });
        storage.CommitSegment(first);

        var collision = storage.BeginSegment(0);
        storage.Append(collision, new byte[] { 2 });
        Assert.Throws<IOException>(() => storage.CommitSegment(collision));
        storage.DiscardSegment(collision);
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(directory, "seg-000000.octr")));

        File.WriteAllBytes(Path.Combine(directory, "seg-999999999999.octr"), new byte[] { 3 });
        var overflowing = new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2);

        Assert.Equal(0, overflowing.NextOrdinal);
        Assert.Equal(0, overflowing.RetainedSegments);
        Assert.Equal(2, overflowing.IncompatibleSegmentsPruned);
        Assert.Empty(Directory.GetFiles(directory, "seg-*"));

        var replacement = new FileTraceSegmentStorage(directory, "seg", ".octr");
        var recovered = replacement.BeginSegment(0);
        replacement.Append(recovered, new byte[] { 4 });
        replacement.CommitSegment(recovered);
        var unowned = Path.Combine(directory, "seg-000001.octr.tmp-not-a-guid");
        File.WriteAllBytes(unowned, new byte[] { 5 });

        var unreadable = new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2);

        Assert.Equal(0, unreadable.NextOrdinal);
        Assert.Equal(1, unreadable.IncompatibleSegmentsPruned);
        Assert.Equal(1, unreadable.StaleTemporaryFilesRemoved);
        Assert.Empty(Directory.GetFiles(directory, "seg-*"));


    }

    /// <summary>
    /// Only the newest committed segment decides whether the store can still be written to.
    /// </summary>
    /// <remarks>
    /// It is the one the next segment follows, and reading a full store on the startup path costs
    /// what the retention cap allows it to hold.
    /// </remarks>
    [Fact]
    public void FileStorageKeepsAStoreItsProbeCanContinue()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        WriteOrdinals(directory, 3);
        var probe = new NewestByteProbe(2);

        var recovered = new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2, probe);

        Assert.Equal(1, probe.Reads);
        Assert.Equal(3, recovered.NextOrdinal);
        Assert.Equal(2, recovered.RetainedSegments);
        Assert.Equal(1, recovered.StartupPrunedSegments);
        Assert.Equal(0, recovered.IncompatibleSegmentsPruned);
        Assert.True(File.Exists(Path.Combine(directory, "seg-000002.octr")));


    }

    [Fact]
    public void FileStorageAbandonsAStoreItsProbeRejects()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        WriteOrdinals(directory, 3);
        var unrelated = Path.Combine(directory, "keep-me.txt");
        File.WriteAllText(unrelated, "unrelated");
        var probe = new NewestByteProbe(0);

        var recovered = new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(2, probe);

        Assert.Equal(1, probe.Reads);
        Assert.Equal(0, recovered.NextOrdinal);
        Assert.Equal(0, recovered.RetainedSegments);
        Assert.Equal(0, recovered.StartupPrunedSegments);
        Assert.Equal(3, recovered.IncompatibleSegmentsPruned);
        Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));
        Assert.True(File.Exists(unrelated));


    }

    [Fact]
    public void FileStorageAbandonsASegmentTooShortToProbe()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, "seg-000000.octr"), Array.Empty<byte>());

        var recovered = new FileTraceSegmentStorage(directory, "seg", ".octr")
            .Reconcile(2, new NewestByteProbe(0));

        Assert.Equal(0, recovered.NextOrdinal);
        Assert.Equal(1, recovered.IncompatibleSegmentsPruned);
        Assert.Empty(Directory.GetFiles(directory, "seg-*.octr"));


    }

    [Fact]
    public void FileStorageOrdinalExhaustionFailsBeforePruningCommittedEvidence()
    {
        using var temporary = new TempDirectory();
        var directory = temporary.Path;
        Directory.CreateDirectory(directory);
        var oldest = Path.Combine(directory, "seg-000000.octr");
        var newest = Path.Combine(directory, "seg-2147483647.octr");
        File.WriteAllBytes(oldest, new byte[] { 1 });
        File.WriteAllBytes(newest, new byte[] { 2 });

        Assert.Throws<TraceSegmentOrdinalExhaustedException>(() =>
            new FileTraceSegmentStorage(directory, "seg", ".octr").Reconcile(1));
        Assert.Throws<TraceSegmentOrdinalExhaustedException>(() =>
            new FileTraceSegmentStorage(directory, "seg", ".octr")
                .Reconcile(1, new NewestByteProbe(9)));
        Assert.True(File.Exists(oldest));
        Assert.True(File.Exists(newest));


    }

    private static void WriteOrdinals(string directory, int count)
    {
        var storage = new FileTraceSegmentStorage(directory, "seg", ".octr");
        for (var ordinal = 0; ordinal < count; ordinal++)
        {
            var segment = storage.BeginSegment(ordinal);
            storage.Append(segment, new[] { (byte)ordinal });
            storage.CommitSegment(segment);
        }
    }

    private sealed class NewestByteProbe : ITraceSegmentHeaderProbe
    {
        private readonly byte _accepted;

        internal NewestByteProbe(byte accepted) => _accepted = accepted;

        internal int Reads { get; private set; }

        public int HeaderBytes => 1;

        public bool IsCompatible(ReadOnlySpan<byte> header)
        {
            Reads++;
            return header[0] == _accepted;
        }
    }
}

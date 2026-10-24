using OrbModding.TestSupport;
using System;
using System.IO;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing;

public sealed class AtomicSegmentSessionStorageTests
{
    [Fact]
    public void ConstructionIsSideEffectFreeAndExposesOnlyTheArtifactName()
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        var storage = Storage(root, "session-000000000000002a");

        Assert.Equal("session-000000000000002a", storage.ArtifactName);
        Assert.False(Directory.Exists(root));


    }

    [Theory]
    [InlineData(".oscs", "manifest.oscm")]
    [InlineData(".osps", "manifest.ospm")]
    public void CommitsConfiguredDenseSegmentsAndManifestByteExactly(
        string segmentExtension,
        string manifestFileName)
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        var storage = Storage(
            root,
            "session-0000000000000001",
            segmentExtension,
            manifestFileName);
        var first = new byte[] { 0, 1, 2, 255 };
        var second = new byte[] { 9, 8, 7 };
        var manifest = new byte[] { 4, 3, 2, 1 };

        storage.Initialize();
        storage.CommitSegment(0, first);
        storage.CommitSegment(1, second);
        storage.CommitManifest(manifest);

        var session = Path.Combine(root, storage.ArtifactName);
        Assert.Equal(first, File.ReadAllBytes(Path.Combine(
            session,
            "segment-00000000" + segmentExtension)));
        Assert.Equal(second, File.ReadAllBytes(Path.Combine(
            session,
            "segment-00000001" + segmentExtension)));
        Assert.Equal(manifest, File.ReadAllBytes(Path.Combine(session, manifestFileName)));
        Assert.Empty(Directory.GetFileSystemEntries(session, "*.tmp-*"));
        Assert.Throws<InvalidOperationException>(() =>
            storage.CommitSegment(2, new byte[] { 8 }));


    }

    [Fact]
    public void RejectsInvalidOrderAndNeverOverwritesCollisions()
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        var storage = Storage(root, "session-0000000000000003");
        storage.Initialize();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            storage.CommitSegment(-1, Array.Empty<byte>()));
        Assert.Throws<InvalidOperationException>(() =>
            storage.CommitSegment(1, Array.Empty<byte>()));

        var session = Path.Combine(root, storage.ArtifactName);
        var collision = Path.Combine(session, "segment-00000000.test");
        File.WriteAllBytes(collision, new byte[] { 91 });

        Assert.Throws<IOException>(() => storage.CommitSegment(0, new byte[] { 7 }));
        Assert.Equal(new byte[] { 91 }, File.ReadAllBytes(collision));
        Assert.Empty(Directory.GetFiles(session, "*.tmp-*"));


    }

    [Fact]
    public void FailedPublishDoesNotExposeAPartialFinalFile()
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        var storage = Storage(root, "session-0000000000000004");
        storage.Initialize();
        var session = Path.Combine(root, storage.ArtifactName);
        var finalPath = Path.Combine(session, "segment-00000000.test");
        Directory.CreateDirectory(finalPath);

        Assert.ThrowsAny<IOException>(() =>
            storage.CommitSegment(0, new byte[] { 1, 2, 3 }));

        Assert.False(File.Exists(finalPath));
        Assert.Empty(Directory.GetFiles(session, "*.tmp-*"));


    }

    [Fact]
    public void InitializesOnlyANewSessionAndNeverPrunesEvidence()
    {
        using var temporary = new TempDirectory();
        var root = temporary.Path;
        var existingSession = Path.Combine(root, "session-0000000000000005");
        Directory.CreateDirectory(existingSession);
        var evidence = Path.Combine(existingSession, "segment-00000000.test");
        File.WriteAllBytes(evidence, new byte[] { 5 });

        var collision = Storage(root, "session-0000000000000005");
        Assert.Throws<IOException>(() => collision.Initialize());
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(evidence));

        var storage = Storage(root, "session-0000000000000006");
        storage.Initialize();
        for (var ordinal = 0; ordinal < 12; ordinal++)
            storage.CommitSegment(ordinal, new[] { (byte)ordinal });
        storage.CommitManifest(new byte[] { 6 });

        Assert.Equal(12, Directory.GetFiles(
            Path.Combine(root, storage.ArtifactName),
            "segment-*.test").Length);
        Assert.Equal(new byte[] { 5 }, File.ReadAllBytes(evidence));


    }

    private static AtomicSegmentSessionStorage Storage(
        string root,
        string artifactName,
        string segmentExtension = ".test",
        string manifestFileName = "manifest.test") =>
        new(root, artifactName, segmentExtension, manifestFileName);

}

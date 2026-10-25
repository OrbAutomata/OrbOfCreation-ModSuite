using OrbModding.TestSupport;
using System;
using System.IO;
using OrbModding.Common.Runtime.Tracing;
using Xunit;

namespace OrbModding.Tests.Runtime.Tracing;

public sealed class AtomicSessionDirectoryTests
{
    [Fact]
    public void ArtifactNameCannotEscapeItsRoot()
    {
        using var temporary = new TempDirectory();
        var parent = temporary.Path;
        var root = Path.Combine(parent, "root");

        Assert.Throws<ArgumentException>(() => new AtomicSessionDirectory(root, "../escape"));

        Assert.False(Directory.Exists(root));
        Assert.False(File.Exists(Path.Combine(parent, "escape")));
        Assert.False(Directory.Exists(Path.Combine(parent, "escape")));


    }

    [Fact]
    public void FileNameCannotEscapeItsSession()
    {
        using var temporary = new TempDirectory();
        var parent = temporary.Path;
        var root = Path.Combine(parent, "root");
        var directory = new AtomicSessionDirectory(root, "session");
        directory.Initialize();

        Assert.Throws<ArgumentException>(() => directory.CommitFile("../escape", new byte[] { 1 }));

        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(root, "session")));
        Assert.False(File.Exists(Path.Combine(root, "escape")));
        Assert.False(Directory.Exists(Path.Combine(root, "escape")));


    }

}

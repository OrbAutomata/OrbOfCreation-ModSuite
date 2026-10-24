using OrbModding.TestSupport;
using System;
using System.IO;
using OrbModding.ServiceCycleTrace;
using Xunit;

namespace OrbModding.Tests.Tools;

public sealed class TraceCaptureLocatorTests
{
    [Fact]
    public void RunFolderResolvesItsOnlyFullSession()
    {
        using var root = new TraceRootTestDirectory();
        var session = root.AddRun("run-20260101-101010-aaaa", "session-000000000000002a");
        var run = Path.Combine(root.Path, "run-20260101-101010-aaaa");

        var selection = TraceCaptureLocator.Locate(run);

        Assert.Equal(session, selection.FullSessionDirectory);
        Assert.Equal(run, selection.RunDirectory);
        Assert.Empty(selection.Notes);
    }

    [Fact]
    public void SessionDirectoryResolvesToTheRunFolderThatHoldsIt()
    {
        using var root = new TraceRootTestDirectory();
        var session = root.AddRun("run-20260101-101010-aaaa", "session-000000000000002a");

        var selection = TraceCaptureLocator.Locate(session);

        Assert.Equal(session, selection.FullSessionDirectory);
        Assert.Equal(Path.Combine(root.Path, "run-20260101-101010-aaaa"), selection.RunDirectory);
    }

    [Fact]
    public void TraceRootReadsTheNewestRunFolderAndNamesTheOthers()
    {
        using var root = new TraceRootTestDirectory();
        root.AddRun("run-20260101-101010-aaaa", "session-000000000000002a");
        root.AddRun("run-20260103-090000-bbbb", "session-000000000000002c");
        var newest = root.AddRun("run-20260103-235959-cccc", "session-000000000000002b");

        var selection = TraceCaptureLocator.Locate(root.Path);

        Assert.Equal(newest, selection.FullSessionDirectory);
        Assert.Equal(2, selection.Notes.Count);
        Assert.Contains("run-20260103-235959-cccc", selection.Notes[0]);
        Assert.Contains("run-20260101-101010-aaaa", selection.Notes[1]);
        Assert.Contains("run-20260103-090000-bbbb", selection.Notes[1]);
        Assert.DoesNotContain("run-20260103-235959-cccc", selection.Notes[1]);
    }

    [Fact]
    public void TraceRootWithOneRunFolderStillReportsWhatItRead()
    {
        using var root = new TraceRootTestDirectory();
        var session = root.AddRun("run-20260101-101010-aaaa", "session-000000000000002a");

        var selection = TraceCaptureLocator.Locate(root.Path);

        Assert.Equal(session, selection.FullSessionDirectory);
        Assert.Contains("run-20260101-101010-aaaa", Assert.Single(selection.Notes));
    }

    [Fact]
    public void RunFolderHoldingTwoFullSessionsIsRejectedByName()
    {
        using var root = new TraceRootTestDirectory();
        root.AddRun("run-20260101-101010-aaaa", "session-000000000000002a");
        var run = Path.Combine(root.Path, "run-20260101-101010-aaaa");
        Directory.CreateDirectory(Path.Combine(run, "full", "session-000000000000002b"));

        var error = Assert.Throws<InvalidDataException>(() => TraceCaptureLocator.Locate(run));

        Assert.Contains("session-000000000000002a", error.Message);
        Assert.Contains("session-000000000000002b", error.Message);
    }

    [Fact]
    public void DirectoryThatHoldsNoCaptureNamesEveryShapeThatWouldWork()
    {
        using var root = new TraceRootTestDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "journal"));

        var error = Assert.Throws<InvalidDataException>(() => TraceCaptureLocator.Locate(root.Path));

        Assert.Contains("full/session-", error.Message);
        Assert.Contains("run-", error.Message);
    }

    [Fact]
    public void RunFolderWithoutARecordedSessionIsNotMistakenForACapture()
    {
        using var root = new TraceRootTestDirectory();
        Directory.CreateDirectory(Path.Combine(root.Path, "run-20260101-101010-aaaa", "recent"));

        Assert.Throws<InvalidDataException>(() => TraceCaptureLocator.Locate(root.Path));
    }

    [Fact]
    public void MissingInputIsReportedWithThePathThatWasNamed()
    {
        using var temporary = new TempDirectory();
        var missing = temporary.Path;

        var error = Assert.Throws<DirectoryNotFoundException>(() => TraceCaptureLocator.Locate(missing));

        Assert.Contains(Path.GetFileName(missing), error.Message);
    }

    private sealed class TraceRootTestDirectory : IDisposable
    {
        private readonly TempDirectory _temporary = new(create: true);
        internal TraceRootTestDirectory()
        {
            Path = _temporary.Path;
            Directory.CreateDirectory(Path);
        }

        internal string Path { get; }

        internal string AddRun(string run, string session)
        {
            var path = System.IO.Path.Combine(Path, run, "full", session);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            _temporary.Dispose();
        }
    }
}

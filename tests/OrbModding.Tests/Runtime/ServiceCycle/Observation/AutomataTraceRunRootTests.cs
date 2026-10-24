using OrbModding.TestSupport;
using System;
using System.IO;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation;

public sealed class AutomataTraceRunRootTests
{
    [Fact]
    public void TheRunFolderNameIsAFixedWidthUtcTimestamp()
    {
        Assert.Matches("^run-[0-9]{8}-[0-9]{6}-[0-9a-f]{4}$", AutomataTraceRunRoot.RunName);
    }

    /// <summary>
    /// Nothing but this sweep deletes a completed capture folder, so the sweep is the whole cap on
    /// the trace root.
    /// </summary>
    /// <remarks>
    /// Whole folders, oldest first: a surviving folder is still the correlated full/profile pair the
    /// analysis tool requires, which pruning by file or by byte budget would destroy.
    /// </remarks>
    [Fact]
    public void SweepingKeepsTheNewestFoldersAndCountsTheCurrentLaunchAmongThem()
    {
        using var root = new TempDirectory(create: true);
        CreateRunFolder(root, "run-20260101-000001-aaaa");
        CreateRunFolder(root, "run-20260102-000002-bbbb");
        CreateRunFolder(root, "run-20260103-000003-cccc");
        CreateRunFolder(root, "run-20260104-000004-dddd");

        var removed = AutomataTraceRunRoot.SweepRunFolders(
            root.Path,
            "run-20260105-000005-eeee",
            retained: 3);

        Assert.Equal(2, removed);
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260101-000001-aaaa")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260102-000002-bbbb")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260103-000003-cccc")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260104-000004-dddd")));
    }

    [Fact]
    public void SweepingNeverDeletesTheCurrentRunFolder()
    {
        using var root = new TempDirectory(create: true);
        CreateRunFolder(root, "run-20260101-000001-aaaa");
        CreateRunFolder(root, "run-20260102-000002-bbbb");

        var removed = AutomataTraceRunRoot.SweepRunFolders(
            root.Path,
            "run-20260101-000001-aaaa",
            retained: 1);

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260101-000001-aaaa")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260102-000002-bbbb")));
    }

    /// <summary>
    /// The always-on journal directory shares the trace root with the run folders and must survive.
    /// </summary>
    /// <remarks>
    /// The sweep deletes only names it is certain it wrote, so the uppercase suffix is left alone
    /// too. It carries a different timestamp than the lowercase folder because the developer's
    /// filesystem is case-insensitive and the two names would otherwise be one directory.
    /// </remarks>
    [Fact]
    public void SweepingLeavesEveryEntryThatIsNotAWellFormedRunFolder()
    {
        using var root = new TempDirectory(create: true);
        CreateRunFolder(root, "journal");
        CreateRunFolder(root, "run-notatimestamp");
        CreateRunFolder(root, "run-20260102-000002-BBBB");
        CreateRunFolder(root, "run-20260101-000001-aaaa");

        var removed = AutomataTraceRunRoot.SweepRunFolders(root.Path, "run-x", retained: 1);

        Assert.Equal(1, removed);
        Assert.True(Directory.Exists(Path.Combine(root.Path, "journal")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-notatimestamp")));
        Assert.True(Directory.Exists(Path.Combine(root.Path, "run-20260102-000002-BBBB")));
        Assert.False(Directory.Exists(Path.Combine(root.Path, "run-20260101-000001-aaaa")));
    }

    [Fact]
    public void SweepingAnAbsentTraceRootIsNotAFailure()
    {
        using var temporary = new TempDirectory();
        var absent = temporary.Path;

        Assert.Equal(0, AutomataTraceRunRoot.SweepRunFolders(absent, "run-x", retained: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AutomataTraceRunRoot.SweepRunFolders(absent, "run-x", retained: 0));
    }


    private static void CreateRunFolder(TempDirectory root, string name)
    {
        var folder = root.CreateDirectory(name);
        File.WriteAllBytes(Path.Combine(folder, "artifact.bin"), new byte[] { 1 });
    }
}

using OrbModding.TestSupport;
using System;
using System.IO;
using System.Text;
using OrbAutomata;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation;

public sealed class AutomataLegacyObservabilityCleanupTests : IDisposable
{
    private readonly TempDirectory _temporary = new();
    private string _configRoot => _temporary.Path;

    public void Dispose()
    {
        _temporary.Dispose();
    }

    [Fact]
    public void CleanupRemovesOnlyExactRetiredFormatsAndTheirEmptyDirectories()
    {
        Write("trace/full/session/segment.oscs", "OSCS", payloadBytes: 7);
        Write("trace/full/session/manifest.oscm", "OSCM", payloadBytes: 5);
        Write("trace/profile/session/segment.osps", "OSPS", payloadBytes: 3);
        Write("trace/profile/session/manifest.ospm", "OSPM", payloadBytes: 2);
        Write("replay/auto-harvest/replay.oscr", "OSCR", payloadBytes: 11);
        Write("trace/journal/segment.osjd", "OSJD", payloadBytes: 13);

        var result = AutomataLegacyObservabilityCleanup.Run(_configRoot);

        Assert.Equal(5, result.RemovedFiles);
        Assert.Equal((4 + 7) + (4 + 5) + (4 + 3) + (4 + 2) + (4 + 11), result.RemovedBytes);
        Assert.Equal(0, result.UnknownEntries);
        Assert.Equal(0, result.Failures);
        Assert.False(Directory.Exists(SuitePath("trace/full")));
        Assert.False(Directory.Exists(SuitePath("trace/profile")));
        Assert.False(Directory.Exists(SuitePath("replay/auto-harvest")));
        Assert.True(File.Exists(SuitePath("trace/journal/segment.osjd")));
    }

    [Fact]
    public void UnknownContentStaysAndMakesTheSummaryLoud()
    {
        Write("trace/full/owned.oscs", "OSCS", payloadBytes: 1);
        Write("trace/full/wrong.oscs", "NOPE", payloadBytes: 1);
        Write("trace/full/readme.txt", "text", payloadBytes: 1);

        var result = AutomataLegacyObservabilityCleanup.Run(_configRoot);

        Assert.Equal(1, result.RemovedFiles);
        Assert.Equal(2, result.UnknownEntries);
        Assert.True(result.HasWarnings);
        Assert.True(File.Exists(SuitePath("trace/full/wrong.oscs")));
        Assert.True(File.Exists(SuitePath("trace/full/readme.txt")));
        Assert.Contains("left 2 unrecognized entries", result.Describe());
    }

    private void Write(string relativePath, string magic, int payloadBytes)
    {
        var path = SuitePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = new byte[4 + payloadBytes];
        Encoding.ASCII.GetBytes(magic, 0, 4, bytes, 0);
        File.WriteAllBytes(path, bytes);
    }

    private string SuitePath(string relativePath) =>
        Path.Combine(
            _configRoot,
            "OrbOfCreation-ModSuite",
            relativePath.Replace('/', Path.DirectorySeparatorChar));
}

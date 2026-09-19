using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using OrbAutomata;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests.Runtime.GameMcp;

public sealed class GameMcpScreenshotBudgetTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "orb-mcp-screenshot-budget-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void BeforeCaptureRejectsTheThirdOwnedScreenshotWithoutEncodingAnotherFrame()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "mcp-first.png"), new byte[] { 1 });
        File.WriteAllBytes(Path.Combine(_directory, "mcp-second.png"), new byte[] { 2 });
        File.WriteAllBytes(Path.Combine(_directory, "someone-elses.png"), new byte[] { 3 });

        var admission = GameMcpScreenshotBudget.BeforeCapture(_directory);

        Assert.Equal(GameMcpScreenshotBudgetStatus.FileLimitReached, admission.Status);
        Assert.False(admission.IsAvailable);
    }

    [Fact]
    public void BeforeCommitAllowsTheEnvelopeBoundaryAndRejectsOneByteBeyondIt()
    {
        Directory.CreateDirectory(_directory);
        var existing = Path.Combine(_directory, "mcp-first.png");
        using (var stream = File.Create(existing))
            stream.SetLength(GameMcpScreenshotBudget.RetainedBytes - 100);

        var atLimit = GameMcpScreenshotBudget.BeforeCommit(_directory, incomingBytes: 100);
        var beyondLimit = GameMcpScreenshotBudget.BeforeCommit(_directory, incomingBytes: 101);

        Assert.True(atLimit.IsAvailable);
        Assert.Equal(GameMcpScreenshotBudgetStatus.ByteLimitReached, beyondLimit.Status);
    }

    [Fact]
    public void AnAbsentOwnedDirectoryHasItsFirstCaptureSlot()
    {
        Assert.True(GameMcpScreenshotBudget.BeforeCapture(_directory).IsAvailable);
    }

    /// <summary>
    /// Storage the suite cannot inspect answers the operator in the suite's own words; the
    /// file-system exception is filed under the reference that answer names.
    /// </summary>
    /// <remarks>
    /// A run folder the operator's own machine will not open is the shape of every reason this arm
    /// exists for. What the caller must not be handed is the machine's words for it — a path, a
    /// permission, a .NET type — because those are what whoever fixes it needs, and they go to the
    /// log under the reference the answer names instead.
    /// </remarks>
    [Fact]
    public void StorageThatWillNotBeInspectedAnswersInTheSuitesWordsUnderALogReference()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        Directory.CreateDirectory(_directory);
        File.SetUnixFileMode(_directory, UnixFileMode.None);
        try
        {
            var admission = GameMcpScreenshotBudget.BeforeCapture(_directory);

            Assert.Equal(GameMcpScreenshotBudgetStatus.StorageUnavailable, admission.Status);
            Assert.StartsWith(
                "Screenshots cannot be stored right now.",
                admission.Reason,
                StringComparison.Ordinal);
            Assert.DoesNotContain(_directory, admission.Reason);

            var reference = Assert.Single(
                Regex.Matches(admission.Reason, "MCP-[0-9A-F]{8}").Select(match => match.Value));
            var line = Assert.Single(
                logged, entry => entry.Contains(reference, StringComparison.Ordinal));
            Assert.Contains("screenshot storage", line);
        }
        finally
        {
            File.SetUnixFileMode(
                _directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            GameActionFaultLog.ConfigureLog(null);
        }
    }
}

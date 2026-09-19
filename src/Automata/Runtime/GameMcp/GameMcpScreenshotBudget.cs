using System;
using System.IO;
using OrbModding.Common;

namespace OrbAutomata;

internal enum GameMcpScreenshotBudgetStatus
{
    Available = 0,
    FileLimitReached = 1,
    ByteLimitReached = 2,
    StorageUnavailable = 3,
}

internal readonly struct GameMcpScreenshotBudgetAdmission
{
    public GameMcpScreenshotBudgetAdmission(
        GameMcpScreenshotBudgetStatus status,
        string reason)
    {
        Status = status;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    public GameMcpScreenshotBudgetStatus Status { get; }
    public string Reason { get; }
    public bool IsAvailable => Status == GameMcpScreenshotBudgetStatus.Available;
}

/// <summary>
/// A fixed, non-configurable envelope for explicitly saved Game MCP screenshots in the active run.
/// The slot check happens before Unity captures or encodes a frame; the byte check happens once,
/// after encoding and before any file is created. The 6 MiB limit is per run folder; with at most
/// eight retained runs, screenshots have an effective retained envelope of approximately 48 MiB.
/// </summary>
internal static class GameMcpScreenshotBudget
{
    internal const int RetainedFiles = 2;
    internal const long RetainedBytes = 6L * 1024L * 1024L;

    internal static GameMcpScreenshotBudgetAdmission BeforeCapture(string directory) =>
        Inspect(directory, incomingBytes: 0);

    internal static GameMcpScreenshotBudgetAdmission BeforeCommit(
        string directory,
        long incomingBytes)
    {
        if (incomingBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(incomingBytes));
        return Inspect(directory, incomingBytes);
    }

    private static GameMcpScreenshotBudgetAdmission Inspect(
        string directory,
        long incomingBytes)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new GameMcpScreenshotBudgetAdmission(
                GameMcpScreenshotBudgetStatus.StorageUnavailable,
                "the owned screenshot directory could not be resolved");
        }

        try
        {
            var count = 0;
            long bytes = 0;
            if (Directory.Exists(directory))
            {
                foreach (var path in Directory.EnumerateFiles(directory, "mcp-*.png"))
                {
                    count++;
                    bytes = checked(bytes + new FileInfo(path).Length);
                }
            }

            if (count >= RetainedFiles)
            {
                return new GameMcpScreenshotBudgetAdmission(
                    GameMcpScreenshotBudgetStatus.FileLimitReached,
                    $"the active run already contains {count} saved screenshots; " +
                    $"the fixed limit is {RetainedFiles}");
            }
            if (bytes > RetainedBytes || incomingBytes > RetainedBytes - bytes)
            {
                return new GameMcpScreenshotBudgetAdmission(
                    GameMcpScreenshotBudgetStatus.ByteLimitReached,
                    $"the save would exceed the fixed {RetainedBytes}-byte screenshot envelope " +
                    $"for the active run ({bytes} bytes retained, {incomingBytes} incoming)");
            }
            return new GameMcpScreenshotBudgetAdmission(
                GameMcpScreenshotBudgetStatus.Available,
                string.Empty);
        }
        catch (Exception exception) when (IsRecoverable(exception))
        {
            return new GameMcpScreenshotBudgetAdmission(
                GameMcpScreenshotBudgetStatus.StorageUnavailable,
                "Screenshots cannot be stored right now." +
                GameActionFaultLog.Record(exception, "screenshot storage"));
        }
    }

    private static bool IsRecoverable(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or OverflowException or System.Security.SecurityException;
}

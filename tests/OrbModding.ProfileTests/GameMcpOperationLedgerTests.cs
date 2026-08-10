using OrbAutomata.GameMcp;
using Xunit;

namespace OrbModding.ProfileTests;

/// <summary>
/// Thirteen of these lines were the whole record of one 43-minute session's MCP traffic, and every
/// one of them read <c>Game MCP operation 85 completed committed (committed):</c>.
/// </summary>
public sealed class GameMcpOperationLedgerTests
{
    [Fact]
    public void ACompletedOperationNamesItsVerbDurationAndFrame()
    {
        var line = GameMcpOperationLedger.Describe(
            Command(85, "world_overview"),
            GameMcpCommandResult.Committed("committed", 4, 7),
            frame: 48815,
            elapsedMilliseconds: 12.34);

        Assert.Equal(
            "Game MCP operation 85 (world_overview) completed committed in 12.3 ms at frame 48815.",
            line);
    }

    [Fact]
    public void ARefusalKeepsItsReasonAndDistinguishesCodeFromDisposition()
    {
        var line = GameMcpOperationLedger.Describe(
            Command(86, "game_purchase"),
            GameMcpCommandResult.Rejected("not_affordable", "the price rose before the frame ran"),
            frame: 48820,
            elapsedMilliseconds: 0.4);

        Assert.Equal(
            "Game MCP operation 86 (game_purchase) completed refused (not_affordable) in 0.4 ms " +
            "at frame 48820: the price rose before the frame ran.",
            line);
    }

    [Fact]
    public void AnOperationlessCommandStillReportsItselfWithoutAnEmptyVerb()
    {
        var line = GameMcpOperationLedger.Describe(
            Command(87, tool: null),
            GameMcpCommandResult.Committed("committed", 4, 7),
            frame: 3,
            elapsedMilliseconds: 1);

        Assert.Equal("Game MCP operation 87 completed committed in 1.0 ms at frame 3.", line);
    }

    [Fact]
    public void ACommandMeasuresItsOwnLifetime()
    {
        var command = Command(88, "world_overview");

        Assert.True(
            command.ElapsedMilliseconds >= 0,
            $"a command reported a negative lifetime: {command.ElapsedMilliseconds}");
    }

    private static GameMcpCommand Command(long sequence, string? tool)
    {
        GameMcpFrameOperation? operation = null;
        if (tool is not null)
        {
            operation = new GameMcpFrameInbox().Submit(new GameMcpOperationRequestBuilder
            {
                ToolName = tool,
                Classification = GameMcpOperationClass.ReadOnly,
                RequiredData = GameMcpFrameData.None,
            }.Freeze());
        }

        return new GameMcpCommand(
            sequence,
            GameMcpCommandKind.Purchase,
            1,
            1,
            "single",
            System.Guid.Empty,
            System.Guid.Empty,
            string.Empty,
            1,
            string.Empty,
            string.Empty,
            false,
            operation);
    }
}

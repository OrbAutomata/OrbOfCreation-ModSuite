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
            "Game MCP operation 85 (world_overview mode=single) completed committed in 12.3 ms " +
            "at frame 48815.",
            line);
    }

    /// <summary>
    /// A verb whose modes do different things says which one it did, on the lines that landed as
    /// well as on the ones that did not. The offer fetch is the press that made this loud: it
    /// arms a whole challenge set and unlocks the reset, and its committed line read exactly like
    /// a queue toggle's.
    /// </summary>
    [Fact]
    public void ACommittedMutationNamesTheModeItPressed()
    {
        var line = GameMcpOperationLedger.Describe(
            Command(72, "time_challenge", mode: "reroll"),
            GameMcpCommandResult.Committed("committed", 4, 7),
            frame: 20780,
            elapsedMilliseconds: 298.5);

        Assert.Equal(
            "Game MCP operation 72 (time_challenge mode=reroll) completed committed in 298.5 ms " +
            "at frame 20780.",
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
            "Game MCP operation 86 (game_purchase mode=single) completed refused " +
            "(not_affordable) in 0.4 ms at frame 48820: the price rose before the frame ran.",
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

    [Fact]
    public void APagedReadSaysWhatItWasAskedForAndHowMuchCameBack()
    {
        var line = GameMcpOperationLedger.DescribeAnswered(
            Read(12, "world_list", request =>
            {
                request.Category = "alchemy-recipes";
                request.Offset = 25;
                request.Limit = 25;
                request.LimitFromCaller = true;
            }),
            GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["status"] = "available",
                ["total"] = 125,
                ["rows"] = Rows(25),
            }),
            frame: 48815,
            elapsedMilliseconds: 3.42);

        Assert.Equal(
            "Game MCP operation 12 (world_list category=alchemy-recipes offset=25 limit=25) " +
            "completed read 25 rows in 3.4 ms at frame 48815.",
            line);
    }

    [Fact]
    public void ABatchCountsItsIdsAndNeverPrintsThem()
    {
        var line = GameMcpOperationLedger.DescribeAnswered(
            Read(13, "world_get", request => request.Uuids = Uuids(200)),
            GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["results"] = Rows(200),
            }),
            frame: 48830,
            elapsedMilliseconds: 41.17);

        Assert.Equal(
            "Game MCP operation 13 (world_get uuids=200) completed read 200 rows in 41.2 ms " +
            "at frame 48830.",
            line);
        Assert.DoesNotContain("00000000-0000-4000-8000", line);
    }

    [Fact]
    public void ARefusedReadKeepsItsCodeAndItsSentence()
    {
        var line = GameMcpOperationLedger.DescribeAnswered(
            Read(14, "world_list", request => request.Category = "type-modifiers"),
            GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["status"] = "unavailable",
                ["reasonCode"] = "ERR_INPUT",
                ["reason"] = "no world category is named type-modifiers",
            }),
            frame: 48832,
            elapsedMilliseconds: 0.21);

        Assert.Equal(
            "Game MCP operation 14 (world_list category=type-modifiers) completed refused " +
            "(ERR_INPUT) in 0.2 ms at frame 48832: no world category is named type-modifiers.",
            line);
    }

    [Fact]
    public void AReadThatAnswersInTextIsSizedInBytes()
    {
        var line = GameMcpOperationLedger.DescribeAnswered(
            Read(15, "suite_health"),
            GameMcpToolExecution.Text("available\nscene: Main"),
            frame: 48833,
            elapsedMilliseconds: 0.34);

        Assert.Equal(
            "Game MCP operation 15 (suite_health) completed read 21 bytes in 0.3 ms at frame 48833.",
            line);
    }

    [Fact]
    public void AReadThatAnswersOneBlockClaimsNoSize()
    {
        var line = GameMcpOperationLedger.DescribeAnswered(
            Read(16, "world_overview"),
            GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["status"] = "available",
                ["economy"] = new GameMcpObjectBuilder { ["resourceRows"] = 80 },
            }),
            frame: 48834,
            elapsedMilliseconds: 1.2);

        Assert.Equal(
            "Game MCP operation 16 (world_overview) completed read in 1.2 ms at frame 48834.",
            line);
    }

    [Fact]
    public void ACommandAnsweredInsideItsOwnFrameKeepsTheMutationVocabulary()
    {
        var line = GameMcpOperationLedger.DescribeAnswered(
            Read(17, "game_purchase", request => request.Amount = 3),
            GameMcpToolExecution.Read(new GameMcpObjectBuilder
            {
                ["status"] = "refused",
                ["reasonCode"] = "not_affordable",
                ["reason"] = "Needs 1.9e347 Alchemic Scroll (have 1.58e347).",
            }),
            frame: 48840,
            elapsedMilliseconds: 0.9);

        Assert.Equal(
            "Game MCP operation 17 (game_purchase amount=3) completed refused (not_affordable) " +
            "in 0.9 ms at frame 48840: Needs 1.9e347 Alchemic Scroll (have 1.58e347).",
            line);
    }

    private static GameMcpArrayBuilder Rows(int count)
    {
        var rows = new GameMcpArrayBuilder();
        for (var index = 0; index < count; index++)
            rows.Add(new GameMcpObjectBuilder { ["row"] = index });
        return rows;
    }

    private static string[] Uuids(int count)
    {
        var uuids = new string[count];
        for (var index = 0; index < count; index++)
        {
            uuids[index] = new System.Guid(
                index, 0, 0x4000, 0x80, 0, 0, 0, 0, 0, 0, 0).ToString("D");
        }
        return uuids;
    }

    private static GameMcpFrameOperation Read(
        long sequence,
        string tool,
        System.Action<GameMcpOperationRequestBuilder>? arguments = null)
    {
        var inbox = new GameMcpFrameInbox();
        GameMcpFrameOperation? operation = null;
        for (var index = 0; index < sequence; index++)
        {
            var request = new GameMcpOperationRequestBuilder
            {
                ToolName = tool,
                Classification = GameMcpOperationClass.ReadOnly,
                RequiredData = GameMcpFrameData.World,
            };
            arguments?.Invoke(request);
            operation = inbox.Submit(request.Freeze());
        }
        return operation!;
    }

    private static GameMcpCommand Command(long sequence, string? tool, string mode = "single")
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
            mode,
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

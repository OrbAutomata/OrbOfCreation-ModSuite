using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using Newtonsoft.Json.Linq;
using OrbAutomata;
using OrbAutomata.GameMcp;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class GameMcpInlineCompletionTests
{
    [Fact]
    public void FinalGameplayCompletionIsFlatOnlyAfterObservedPostStateIsAttached()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        var evidence = ServiceNativeMutationEvidence.Observed(
            NativeMutationOutcome.Verified,
            new NativeMutationCallOutcome(1, 1, 1));
        var native = ServiceActionResult.Committed(CommonActionResultCodes.Committed, evidence);
        var terminal = GameMcpCommandResult.FromAction(
            in native,
            12,
            7).WithDetails(new GameMcpObjectBuilder
            {
                ["level"] = 4,
                ["available"] = false,
            }.Freeze());
        var projected = GameMcpTestHarness.Json(terminal.Project(command));
        Assert.Equal("committed", (string?)projected["status"]);
        Assert.Null(projected["code"]);
        Assert.Equal(new[] { "status", "level", "available" },
            projected.Properties().Select(property => property.Name));
        Assert.Null(projected["receiptId"]);
    }

    [Fact]
    public void InboxClaimsEveryAcceptedOperationWithoutAnArbitraryCapacity()
    {
        var operations = new GameMcpFrameInbox();
        for (var index = 0; index < 128; index++)
            GameMcpAcceptanceFixture.SubmitHarvest(operations);

        Assert.Equal(128, operations.ClaimPending().Length);
        Assert.Empty(operations.ClaimPending());
    }
}

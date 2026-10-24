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

public sealed class GameMcpEmergencyStopTests
{
    [Fact]
    public void StopAndGameplayRetainSubmissionOrderForFrameExecution()
    {
        var operations = new GameMcpFrameInbox();
        var before = GameMcpAcceptanceFixture.SubmitHarvest(operations);
        var stop = operations.Submit(new GameMcpOperationRequestBuilder
        {
            ToolName = "suite_emergency_stop",
            Route = GameMcpTestHarness.Route("suite_emergency_stop"),
            Mode = "engage",
        }.Freeze());
        var after = GameMcpAcceptanceFixture.SubmitHarvest(operations);

        Assert.Equal(new[] { before, stop, after }, operations.ClaimPending());
    }
}

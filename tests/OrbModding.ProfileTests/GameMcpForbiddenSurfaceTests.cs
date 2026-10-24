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

public sealed class GameMcpForbiddenSurfaceTests
{
    [Fact]
    public void SurfaceContainsNoArbitraryInputOrSaveResetTools()
    {
        var names = GameMcpAcceptanceFixture.ToolNames();
        var combined = string.Join("|", names).ToLowerInvariant();
        Assert.DoesNotContain("save", combined);
        Assert.DoesNotContain("reset", combined);
        Assert.DoesNotContain("keyboard", combined);
        Assert.DoesNotContain("mouse", combined);
        Assert.DoesNotContain("invoke_native", combined);
    }
}

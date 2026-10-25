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

public sealed class GameMcpRefusalSentenceTests
{
    private static readonly Guid Absent = Guid.Parse("3f2a6c18-9b41-4f0e-8d77-1c5a2e6b90d4");

    [Fact]
    public void An_id_no_row_carries_is_refused_without_reciting_the_id()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var result = GameMcpTestHarness.Json(
            GameMcpWorldDetail.GetRow(state, "spell-recipes", Absent.ToString("D")));

        Assert.Equal("ERR_NOT_FOUND", (string?)result["reasonCode"]);
        var reason = (string?)result["reason"] ?? string.Empty;
        Assert.DoesNotContain(Absent.ToString("D"), reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("spell-recipes", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void The_all_zero_id_is_refused_as_an_id_that_names_nothing()
    {
        var state = GameMcpAcceptanceFixture.SpellSnapshot(4);

        var result = GameMcpTestHarness.Json(GameMcpWorldDetail.GetRows(
            state, "spell-recipes", new[] { Guid.Empty.ToString("D") }));

        var row = Assert.Single(result["results"]!.Values<JObject>())!;
        Assert.Equal("ERR_INPUT", (string?)row["reasonCode"]);
        Assert.Contains("names nothing", (string?)row["reason"]!, StringComparison.Ordinal);
    }
}

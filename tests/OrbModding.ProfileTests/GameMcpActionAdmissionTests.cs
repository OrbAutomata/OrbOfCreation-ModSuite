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

public sealed class GameMcpActionAdmissionTests
{
    [Fact]
    public void ACommandAtTheCurrentLifecycleAndConfigurationIsAdmitted()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        Assert.False(GameMcpNativeActionAdmission.TryReject(
            command,
            currentLifecycleGeneration: command.ExpectedLifecycleGeneration,
            currentConfigurationGeneration: command.ExpectedConfigurationGeneration,
            emergencyStopEngaged: false,
            out _));
    }

    [Fact]
    public void LifecycleConfigurationAndEmergencyStopRemainLiveGates()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            command.ExpectedLifecycleGeneration + 1,
            command.ExpectedConfigurationGeneration,
            false,
            out var lifecycle));
        Assert.Equal("lifecycle_replaced", lifecycle.Code);

        Assert.True(GameMcpNativeActionAdmission.TryReject(
            command,
            command.ExpectedLifecycleGeneration,
            command.ExpectedConfigurationGeneration,
            true,
            out var stop));
        Assert.Equal("emergency_stop", stop.Code);
    }
}

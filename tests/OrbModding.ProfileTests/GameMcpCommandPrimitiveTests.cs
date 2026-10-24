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

public sealed class GameMcpCommandPrimitiveTests
{
    [Fact]
    public void ImmutableCommandCrossesNoJsonOrUnityObjects()
    {
        var command = GameMcpAcceptanceFixture.NativeCommand();
        var properties = typeof(GameMcpCommand).GetProperties(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.DoesNotContain(
            properties,
            property =>
                typeof(JToken).IsAssignableFrom(property.PropertyType) ||
                property.PropertyType.FullName?.StartsWith("UnityEngine.", StringComparison.Ordinal) == true);
        GameMcpNativeActionAdmission.AssertNativeType(command, "StructureSO");
        Assert.Throws<ArgumentException>(() =>
            GameMcpNativeActionAdmission.AssertNativeType(command, "UpgradeSO"));
    }
}

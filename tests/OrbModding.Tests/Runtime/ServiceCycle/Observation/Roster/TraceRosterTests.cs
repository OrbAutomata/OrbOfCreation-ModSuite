using System.Text;
using OrbAutomata;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Observation.Roster;

public sealed class TraceRosterTests
{
    [Fact]
    public void ARosterSurvivesTheTripToTextAndBack()
    {
        var roster = new ServiceCycleTraceRoster(new[]
        {
            new ServiceCycleTraceRosterEntry(ServiceCycleTraceRoster.ServiceKind, 1, "orbautomata.world-collection", "World collection"),
            new ServiceCycleTraceRosterEntry(ServiceCycleTraceRoster.ServiceKind, 2, "orbautomata.auto-harvest", "Auto Harvest"),
        });

        var decoded = TraceRosterFormat.Decode(Encoding.UTF8.GetString(TraceRosterFormat.Encode(roster)));

        Assert.Equal(2, decoded.Count);
        Assert.Equal(1UL, decoded[0].Identity);
        Assert.Equal("orbautomata.world-collection", decoded[0].MachineId);
        Assert.Equal("World collection", decoded[0].DisplayName);
        Assert.Equal("Auto Harvest", decoded[1].DisplayName);
        Assert.Equal(ServiceCycleTraceRoster.ServiceKind, decoded[1].Kind);
    }

    /// <summary>
    /// A service the suite has no name for keeps the identity it registered. A reader shown
    /// "orbautomata.auto-agromancy" knows what ran and can see a display name is missing; one shown
    /// "Service 4" knows neither.
    /// </summary>
    [Fact]
    public void AnUnnamedServiceIsRecordedUnderItsRegisteredIdentityRatherThanDropped()
    {
        var roster = new ServiceCycleTraceRoster(new[]
        {
            new ServiceCycleTraceRosterEntry(ServiceCycleTraceRoster.ServiceKind, 4, "orbautomata.auto-agromancy", string.Empty),
        });

        var decoded = TraceRosterFormat.Decode(Encoding.UTF8.GetString(TraceRosterFormat.Encode(roster)));

        Assert.Equal(1, decoded.Count);
        Assert.Equal("orbautomata.auto-agromancy", decoded[0].MachineId);
        Assert.Equal(string.Empty, decoded[0].DisplayName);
    }

    /// <summary>
    /// A world-collection category registered its own name and that name has spaces in it, so the
    /// machine identity field has to be able to hold one. It is the last field before the separator
    /// and takes whatever is left of the head.
    /// </summary>
    [Fact]
    public void ACategoryWhoseNameHasSpacesSurvivesTheTripToTextAndBack()
    {
        var roster = new ServiceCycleTraceRoster(new[]
        {
            new ServiceCycleTraceRosterEntry(
                ServiceCycleTraceRoster.WorldCategoryKind, 31, "scribe relations", string.Empty),
        });

        var decoded = TraceRosterFormat.Decode(Encoding.UTF8.GetString(TraceRosterFormat.Encode(roster)));

        Assert.Equal(1, decoded.Count);
        Assert.Equal(ServiceCycleTraceRoster.WorldCategoryKind, decoded[0].Kind);
        Assert.Equal(31UL, decoded[0].Identity);
        Assert.Equal("scribe relations", decoded[0].MachineId);
    }

    /// <summary>
    /// Service identities and category identities are both small ordinals, so the kind is what keeps
    /// them apart. A reader that ignored it would name category 2 after Auto Harvest.
    /// </summary>
    [Fact]
    public void CategoriesAndServicesShareIdentityNumbersAndAreKeptApartByKind()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1), clock);
        using var first = registry.Register(
            new ExecutionServiceDefinition("orbautomata.auto-harvest"),
            new LifecycleGeneration(1));

        var roster = AutomataServiceCycleTraceRoster.Build(
            registry,
            new[] { "resources", "scribe relations" });

        Assert.Equal(3, roster.Count);
        Assert.Equal(ServiceCycleTraceRoster.ServiceKind, roster[0].Kind);
        Assert.Equal(1UL, roster[0].Identity);
        Assert.Equal(ServiceCycleTraceRoster.WorldCategoryKind, roster[1].Kind);
        Assert.Equal(1UL, roster[1].Identity);
        Assert.Equal("resources", roster[1].MachineId);
        Assert.Equal(2UL, roster[2].Identity);
        Assert.Equal("scribe relations", roster[2].MachineId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a roster at all")]
    [InlineData("OSCV 1 2\nservice 1 a = b\n")]
    [InlineData("OSCR 9 1\nservice 1 a = b\n")]
    public void TextThatIsNotThisFormatReadsAsNoNamesRatherThanAsAFailure(string text)
    {
        Assert.Equal(0, TraceRosterFormat.Decode(text).Count);
    }

    /// <summary>
    /// One unreadable line costs its own entry and no other. A roster is a convenience beside the
    /// evidence, so a reader that refused the whole file over one bad line would trade every name for
    /// a strictness nothing depends on.
    /// </summary>
    [Fact]
    public void AMalformedLineIsSkippedWithoutCostingTheRestOfTheRoster()
    {
        var decoded = TraceRosterFormat.Decode(
            "OSCR 1 3\nservice 1 orbautomata.a = First\nrubbish\nservice 3 orbautomata.c = Third\n");

        Assert.Equal(2, decoded.Count);
        Assert.Equal("First", decoded[0].DisplayName);
        Assert.Equal("Third", decoded[1].DisplayName);
    }

    [Fact]
    public void TheRosterNamesTheTraceIdentityAServiceWillActuallyAppearUnder()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, new LifecycleGeneration(1), clock);
        using var first = registry.Register(
            new ExecutionServiceDefinition("orbautomata.auto-harvest"),
            new LifecycleGeneration(1));
        using var second = registry.Register(
            new ExecutionServiceDefinition("orbautomata.auto-buy"),
            new LifecycleGeneration(1));

        var roster = AutomataServiceCycleTraceRoster.Build(registry);

        // Registration ordinal plus one, the derivation the emitters use, so the roster names the
        // number the stream carries rather than the ordinal behind it.
        Assert.Equal(2, roster.Count);
        Assert.Equal(1UL, roster[0].Identity);
        Assert.Equal("Auto Harvest", roster[0].DisplayName);
        Assert.Equal(2UL, roster[1].Identity);
        Assert.Equal("Auto Buy", roster[1].DisplayName);
    }

    [Fact]
    public void TheSuiteNamesEveryServiceItRegisters()
    {
        Assert.Equal("World collection", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.world-collection")));
        Assert.Equal("Auto Harvest", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.auto-harvest")));
        Assert.Equal("Auto Buy", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.auto-buy")));
        Assert.Equal("Spell Leveling", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.spell-level")));
        Assert.Equal("Auto Cast", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.auto-cast")));
        Assert.Equal("Auto Concept", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.auto-concept")));
        Assert.Equal("Auto Items", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.auto-items")));
        Assert.Equal("Auto Scribe", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.auto-scribe")));
        Assert.Equal("Mentor", AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbmentor.mastery-sharing")));
        Assert.Equal(string.Empty, AutomataServiceCycleTraceRoster.DisplayName(new ServiceId("orbautomata.unregistered")));
    }
}

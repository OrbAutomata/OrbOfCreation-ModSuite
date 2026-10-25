using System.Collections.Generic;
using OrbAutomata;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The capture port's only behaviour of its own: saying what a pass managed, without saying it four
/// times a second.
/// </summary>
public sealed class AutomataWorldCapturePortTests
{
    /// <summary>
    /// The announce is the only line in the log that names the population, so it has to speak when
    /// the population changes and stay quiet while it drifts. A prestige moved a live session from
    /// 6,683 entities to 4,051 and this line said nothing, because a healthy pass compared as the
    /// literal word "complete".
    /// </summary>
    [Fact]
    public void DriftIsQuietAndAPopulationChangeIsAnnouncedInBothDirections()
    {
        var seeded = WorldCaptureTestContext.SeedScribeRelations();
        var announced = new List<int>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(),
            () => 1,
            () => 1,
            r => announced.Add(r.TotalSampled));
        var frame = new GameWorldCycleFrame();

        var resources = global::ResourceSO.All.Count;
        try
        {
            port.Collect(frame);
            var baseline = Assert.Single(announced);
            Assert.True(baseline >= 11, $"the stub world is too small to drift: {baseline}");

            AddResource();
            port.Collect(frame);
            Assert.Single(announced);

            var grown = (baseline / 5) + 2;
            for (var index = 1; index < grown; index++) AddResource();
            port.Collect(frame);
            Assert.Equal(new[] { baseline, baseline + grown }, announced);

            global::ResourceSO.All.RemoveRange(
                resources,
                global::ResourceSO.All.Count - resources);
            port.Collect(frame);
            Assert.Equal(new[] { baseline, baseline + grown, baseline }, announced);
        }
        finally
        {
            global::ResourceSO.All.Clear();
            global::AlchemyManager.instance = null;
            foreach (var identity in seeded)
                global::IdScriptableObject.RuntimeLookup.Remove(identity);
        }
    }

    private static void AddResource() => global::ResourceSO.All.Add(
        new global::ResourceSO { uuid = System.Guid.NewGuid().ToString() });

    /// <summary>
    /// The reason this exists: without it a build that renamed one member reaches the operator as a
    /// count of unavailable categories and no member name anywhere.
    /// </summary>
    [Fact]
    public void AShortfallIsAnnouncedWithItsCategoryAndReason()
    {
        var announced = new List<string>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(_ => null),
            () => 1,
            () => 1,
            r => announced.Add(r.Describe()));

        port.Collect(new GameWorldCycleFrame());
        port.Collect(new GameWorldCycleFrame());

        var line = Assert.Single(announced);
        Assert.StartsWith("World collection incomplete", line);
        Assert.Contains("resources", line);
    }

    [Fact]
    public void AnInstanceThatContradictsItsContainingQueueFailsCaptureLoudly()
    {
        using var faults = new OrbModding.TestSupport.CapturedGameActionFaultLog();
        var seeded = WorldCaptureTestContext.SeedScribeRelations();
        var active = Assert.IsType<global::CraftingInstanceListVariable>(
            global::IdScriptableObject.RuntimeLookup[KnownEntities.ActiveScribeInstances.Uuid]);
        active.value.Add(new global::CraftingInstance { Automatic = true });
        var announced = new List<WorldCollectionReport>();
        var port = new AutomataWorldCapturePort(
            new GameWorldCollector(),
            () => 1,
            () => 1,
            r => announced.Add(r));

        try
        {
            port.Collect(new GameWorldCycleFrame());

            var observed = Assert.Single(announced);
            var line = observed.Describe();
            Assert.StartsWith("World collection incomplete", line);
            Assert.DoesNotContain("CraftingInstance.IsAuto() contradicted", line);
            faults.AssertLogged(observed.For("scribe relations").Failure.Reason, "CraftingInstance.IsAuto() contradicted");
        }
        finally
        {
            global::AlchemyManager.instance = null;
            foreach (var identity in seeded)
                global::IdScriptableObject.RuntimeLookup.Remove(identity);
        }
    }


}

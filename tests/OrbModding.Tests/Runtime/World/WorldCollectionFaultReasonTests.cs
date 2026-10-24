using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldCollectionFaultReasonTests
{
    [Fact]
    public void EveryFailedRequirementCaptureLogsTheWholeExceptionAndRetriesWithoutLeakingItsMessage()
    {
        using var setup = new HealthyWorldReadSetup();
        var owner = new global::UpgradeSO { maxLevel = 1 };
        var failure = new InvalidOperationException("private native failure payload");
        owner.prerequisitesPerLevel.prerequisites.Add(new Requirements.UpgradeRequirement
        {
            item = new global::UpgradeSO { GetGuidFailure = failure },
            reqType = Requirements.UpgradeRequirementType.OneLevel,
        });
        global::UpgradeSO.All.Add(owner);
        var logs = new List<string>();
        GameActionFaultLog.ConfigureLog(logs.Add);
        try
        {
            var collector = new GameWorldCollector();
            var frame = new GameWorldCycleFrame { CollectedAtEpoch = 1 };
            var first = collector.Collect(frame).For("entity requirements");
            var second = collector.Collect(frame).For("entity requirements");

            Assert.Equal(1, first.Skipped);
            Assert.Equal(1, second.Skipped);
            Assert.Equal(2, logs.Count);
            Assert.All(logs, log =>
            {
                Assert.Contains(nameof(InvalidOperationException), log, StringComparison.Ordinal);
                Assert.Contains(failure.Message, log, StringComparison.Ordinal);
                Assert.Contains(" at ", log, StringComparison.Ordinal);
            });
            Assert.DoesNotContain(failure.Message, first.FirstFailure, StringComparison.Ordinal);
            Assert.DoesNotContain(failure.Message, second.FirstFailure, StringComparison.Ordinal);
            Assert.Contains("Reference MCP-", first.FirstFailure, StringComparison.Ordinal);
            Assert.Contains("Reference MCP-", second.FirstFailure, StringComparison.Ordinal);
            Assert.NotEqual(first.FirstFailure, second.FirstFailure);
        }
        finally
        {
            GameActionFaultLog.ConfigureLog(null);
            global::UpgradeSO.All.Clear();
        }
    }
}

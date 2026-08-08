using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The pass that samples the upgrade cost curve at levels the upgrade is not standing on.
/// </summary>
/// <remarks>
/// The prices the stand-in answers with are fixture values rather than a second implementation of
/// <c>SetToLevel</c>, so nothing here proves the scaling arithmetic — that comparison only means
/// something against the running game. What offline settles is everything around it: which levels
/// are asked for, which are skipped because the game would clamp them, that the memo is put back
/// where the next render expects it, and that a disagreement is reported rather than absorbed.
/// </remarks>
public sealed class AutomataUpgradeCostVerifierTests : IDisposable
{
    public AutomataUpgradeCostVerifierTests() => global::UpgradeSO.All.Clear();

    public void Dispose() => global::UpgradeSO.All.Clear();

    [Fact]
    public void AnUnresolvableContractMakesTheVerifierUnavailableRatherThanPassing() =>
        Assert.False(new AutomataUpgradeCostVerifier(typeof(object)).IsAvailable);

    [Fact]
    public void APartiallyShapedTypeStillFailsClosed() =>
        Assert.False(new AutomataUpgradeCostVerifier(typeof(PartialUpgrade)).IsAvailable);

    [Fact]
    public void AnUnavailableVerifierRefusesToVerifyAndSaysWhy()
    {
        var run = new DifferentialRun();

        var verified = new AutomataUpgradeCostVerifier(typeof(object))
            .TryVerify(new object(), run, Session(), out var failure);

        Assert.False(verified);
        Assert.NotEmpty(failure);
        Assert.Equal(0, run.Compared);
    }

    [Fact]
    public void NullArgumentsAreRejectedRatherThanTreatedAsNothingToDo()
    {
        var verifier = new AutomataUpgradeCostVerifier(typeof(object));
        var run = new DifferentialRun();

        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(null!, run, Session(), out _));
        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(new object(), null!, Session(), out _));
        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(new object(), run, null!, out _));
    }

    /// <summary>
    /// An upgrade with no ceiling is priced at every sampled offset, and the memo is left holding the
    /// level the game's own callers ask for rather than the last one probed.
    /// </summary>
    [Fact]
    public void AnUncappedUpgradeIsComparedAtEverySampledLevelAndLeavesItsMemoWhereItFoundIt()
    {
        var upgrade = Upgrade(level: 0, queuedLevels: 0, maxLevel: 0, authored: 100);

        var run = new DifferentialRun("Upgrade cost curve");
        var session = Session();
        Assert.True(new AutomataUpgradeCostVerifier(typeof(global::UpgradeSO))
            .TryVerify(upgrade, run, session, out var failure));

        Assert.Empty(failure);
        Assert.Equal(3, run.Compared);
        Assert.True(run.Passed);
        Assert.Equal(new[] { 0, 1, 4, 0 }, upgrade.LeveledCostLevelsAsked);
        Assert.Equal(0, upgrade.CachedCostLevel);
        Assert.Equal(0, session.ExpectedSkips);
    }

    /// <summary>
    /// A finite upgrade is sampled only where the game would not clamp. Comparing a clamped offset
    /// would need the ceiling reproduced on both sides, which is the transcription this pass exists
    /// to avoid making twice.
    /// </summary>
    [Fact]
    public void AnOffsetTheGameWouldClampIsSkippedRatherThanCompared()
    {
        var upgrade = Upgrade(level: 1, queuedLevels: 0, maxLevel: 3, authored: 100);

        var run = new DifferentialRun("Upgrade cost curve");
        Assert.True(new AutomataUpgradeCostVerifier(typeof(global::UpgradeSO))
            .TryVerify(upgrade, run, Session(), out _));

        // The ceiling is maxLevel - 1 = 2, so level+0 and level+1 are comparable and level+4 is not.
        // The trailing 1 is the restore, which asks for the level the game's own callers would.
        Assert.Equal(2, run.Compared);
        Assert.Equal(new[] { 1, 2, 1 }, upgrade.LeveledCostLevelsAsked);
    }

    /// <summary>
    /// A maxed-out upgrade has no level the game would price without clamping, which is a skip rather
    /// than agreement. Counting it as a comparison would pad the pass with entities it never checked.
    /// </summary>
    [Fact]
    public void AMaxedUpgradeIsRecordedAsAnExpectedSkip()
    {
        var upgrade = Upgrade(level: 3, queuedLevels: 0, maxLevel: 3, authored: 100);

        var run = new DifferentialRun("Upgrade cost curve");
        var session = Session();
        Assert.True(new AutomataUpgradeCostVerifier(typeof(global::UpgradeSO))
            .TryVerify(upgrade, run, session, out _));

        Assert.Equal(0, run.Compared);
        Assert.Equal(1, session.ExpectedSkips);
    }

    [Fact]
    public void APriceTheGameDisagreesWithIsReportedRatherThanAbsorbed()
    {
        var upgrade = Upgrade(level: 0, queuedLevels: 0, maxLevel: 0, authored: 100);
        upgrade.LeveledCosts[1] = CostList(upgrade.resourceCost.costs[0].resource!, 990);

        var run = new DifferentialRun("Upgrade cost curve");
        Assert.True(new AutomataUpgradeCostVerifier(typeof(global::UpgradeSO))
            .TryVerify(upgrade, run, Session(), out _));

        Assert.Equal(3, run.Compared);
        Assert.False(run.Passed);
    }

    private static global::UpgradeSO Upgrade(int level, int queuedLevels, int maxLevel, double authored)
    {
        var resource = new global::ResourceSO();
        var upgrade = new global::UpgradeSO
        {
            level = level,
            queuedLevels = queuedLevels,
            maxLevel = maxLevel,
            resourceCost = CostList(resource, authored),
        };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    private static global::ResourceCostList CostList(global::ResourceSO resource, double amount)
    {
        var list = new global::ResourceCostList();
        list.costs.Add(new global::ResourceTuple(resource, new BigDouble(amount)));
        return list;
    }

    private static DifferentialVerificationSession Session()
    {
        var session = new DifferentialVerificationSession(
            "Upgrade cost curve", tickBudget: 1, entityBudget: int.MaxValue);
        session.Start();
        return session;
    }

    /// <summary>Carries the level fields and none of the cost members.</summary>
    private sealed class PartialUpgrade
    {
        public int level;
        public int queuedLevels;
        public int maxLevel;

        public Guid GetGuid() => Guid.Empty;

        public bool HasFiniteLevels() => maxLevel > 0;
    }
}

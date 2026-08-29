using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The pass that asks the game whether the two plot-node quantity ports are right.
/// </summary>
/// <remarks>
/// The agreeing path is not asserted against a stub that recomputes the same expression — that would
/// prove the port agrees with a copy of itself. What is checked here is the wiring: that the verifier
/// refuses a shape it cannot read, that it feeds the port the inputs it read from the entity, and
/// that a disagreement is reported as one rather than absorbed.
/// </remarks>
public sealed class AutomataPlotQuantityVerifierTests : IDisposable
{
    public AutomataPlotQuantityVerifierTests() => global::PlotNodeSO.All.Clear();

    public void Dispose() => global::PlotNodeSO.All.Clear();

    [Fact]
    public void AnUnresolvableContractMakesTheVerifierUnavailableRatherThanPassing() =>
        Assert.False(new AutomataPlotQuantityVerifier(typeof(object)).IsAvailable);

    /// <summary>
    /// A type carrying the usage records but none of the accessors is the dangerous shape: a resolver
    /// that proceeded on a partial match would compare the port against nothing.
    /// </summary>
    [Fact]
    public void APartiallyShapedTypeStillFailsClosed() =>
        Assert.False(new AutomataPlotQuantityVerifier(typeof(PartialNode)).IsAvailable);

    [Fact]
    public void AnUnavailableVerifierRefusesToVerifyAndSaysWhy()
    {
        var run = new DifferentialRun();

        var verified = new AutomataPlotQuantityVerifier(typeof(object))
            .TryVerify(new object(), run, out var failure);

        Assert.False(verified);
        Assert.NotEmpty(failure);
        Assert.Equal(0, run.Compared);
    }

    [Fact]
    public void NullArgumentsAreRejectedRatherThanTreatedAsNothingToDo()
    {
        var verifier = new AutomataPlotQuantityVerifier(typeof(object));

        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(null!, new DifferentialRun(), out _));
        Assert.Throws<ArgumentNullException>(() => verifier.TryVerify(new object(), null!, out _));
    }

    /// <summary>
    /// Both quantities are compared, and the port is fed the node's own idle, total and usage
    /// numbers rather than anything the fixture handed it.
    /// </summary>
    [Fact]
    public void BothQuantitiesAreComparedAgainstTheGamesOwnAnswers()
    {
        // Four idle and two growing, with one main use and three of the "any" kind: the "any" term
        // is absorbed by the two busy nodes first and only the third bites the idle count.
        var node = Node(idle: 4, growing: 2, usageMain: 1, usageAny: 3);
        node.RemainingQuantityAnswer = 2;
        node.RemainingTotalQuantityAnswer = 2;

        var run = new DifferentialRun("Plot node quantity");
        Assert.True(new AutomataPlotQuantityVerifier(typeof(global::PlotNodeSO))
            .TryVerify(node, run, out var failure));

        Assert.Empty(failure);
        Assert.Equal(2, run.Compared);
        Assert.True(run.Passed);
    }

    /// <summary>
    /// A node the game answers differently about is a mismatch, named by the member that disagreed.
    /// </summary>
    [Fact]
    public void ADisagreementIsReportedRatherThanAbsorbed()
    {
        var node = Node(idle: 4, growing: 2, usageMain: 1, usageAny: 3);
        node.RemainingQuantityAnswer = 99;
        node.RemainingTotalQuantityAnswer = 2;

        var run = new DifferentialRun("Plot node quantity");
        Assert.True(new AutomataPlotQuantityVerifier(typeof(global::PlotNodeSO))
            .TryVerify(node, run, out _));

        Assert.False(run.Passed);
        Assert.Contains(
            run.Finding().Detail,
            row => row.Contains("GetRemainingQuantity", StringComparison.Ordinal));
    }

    private static global::PlotNodeSO Node(int idle, int growing, int usageMain, int usageAny)
    {
        var node = new global::PlotNodeSO();
        node.phaseInstances.Add(new global::PlotNodePhaseInstance(global::PlotNodePhases.Idle, idle));
        node.phaseInstances.Add(
            new global::PlotNodePhaseInstance(global::PlotNodePhases.Growing, growing));
        node.actionQuantityUsageMain = new global::ValueModifierRecord(new BigDouble(usageMain));
        node.actionQuantityUsageAny = new global::ValueModifierRecord(new BigDouble(usageAny));
        global::PlotNodeSO.All.Add(node);
        return node;
    }

    /// <summary>Carries the usage records and none of the accessors.</summary>
    private sealed class PartialNode
    {
        public global::ValueModifierRecord actionQuantityUsageMain = new(BigDouble.Zero);
        public global::ValueModifierRecord actionQuantityUsageAny = new(BigDouble.Zero);

        public Guid GetGuid() => Guid.Empty;
    }
}

using System;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The per-action instance ceiling <c>WorldPlotAction</c> copies instead of calling for.
/// </summary>
/// <remarks>
/// <para>
/// <c>plot-lifecycle.instance-maximum-action</c> is declared both <c>reflection</c> — the action
/// boundary calls it — and <c>mirrored</c>, because collection copies the number rather than paying
/// a call per action per pass. The metadata contract proves the member still exists with that
/// signature; nothing there can prove the <em>value</em>, and on the mirrored half the value is the
/// whole dependency.
/// </para>
/// <para>
/// The game returns one literal for every action, so the constant is readable from the method body
/// without running the game. A build that made the ceiling depend on the action would fail the
/// shape assertion here first, which is the honest answer: the suite would then have to call rather
/// than copy.
/// </para>
/// </remarks>
public sealed class PlotInstanceCeilingContractTests
{
    /// <summary>WorldPlotAction.MaximumInstances, the suite's copy of this number.</summary>
    private const int MirroredCeiling = 10000;

    [GameAssemblyFact]
    public void TheMirroredPlotInstanceCeilingStillHoldsTheNumberTheSuiteCopied()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var body = assembly.GetMethodBodyBytes(
            "PlotNodeActionInstance", "GetMaximumInstances");

        // ldc.i4 <int32>; ret — the whole method.
        Assert.True(
            body.Length == 6 && body[0] == 0x20 && body[5] == 0x2A,
            "PlotNodeActionInstance.GetMaximumInstances no longer returns a single literal, so " +
            "world collection can no longer copy the ceiling and must call for it instead.");
        Assert.Equal(MirroredCeiling, BitConverter.ToInt32(body, 1));
    }
}

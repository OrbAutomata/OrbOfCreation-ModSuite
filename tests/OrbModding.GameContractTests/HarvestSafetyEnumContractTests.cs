using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The plot, timer, cost and filter discriminants <c>AutoHarvestActionSafety</c> hard-codes as
/// integers.
/// </summary>
/// <remarks>
/// <para>
/// The world binder hands these axes over as their raw ordinals, so the safety gate compares
/// ordinals. The manifest declares each member as <c>mirrored</c>, which proves the member still
/// exists and still belongs to that enum; nothing there can prove its <em>value</em>, and the value
/// is the whole dependency.
/// </para>
/// <para>
/// This is what fails when a member is renumbered. Without it the gate would keep answering and
/// read one phase as another — and since every comparison is fail-closed, the visible symptom
/// would be Auto Harvest quietly declining to act rather than anything that looks like a defect.
/// </para>
/// </remarks>
public sealed class HarvestSafetyEnumContractTests
{
    // AutoHarvestActionSafety.PlotPhaseIdle / PlotPhaseGrowing / PlotPhaseResting
    [InlineData("PlotNodeSO+PlotNodePhases", "Idle", 0)]
    [InlineData("PlotNodeSO+PlotNodePhases", "Growing", 1)]
    [InlineData("PlotNodeSO+PlotNodePhases", "Resting", 2)]
    // AutoHarvestActionSafety.TimerTypeSingle / TimerTypeParallel
    [InlineData("TimerList+TimerType", "Single", 0)]
    [InlineData("TimerList+TimerType", "Parallel", 1)]
    // AutoHarvestActionSafety.CostTypeExitPhase
    [InlineData("PlotNodeActionSO+CostType", "ExitPhase", 1)]
    // AutoHarvestActionSafety.FilterTypeWhiteList
    [InlineData("FilterEffectMod+FilterType", "WhiteList", 1)]
    [GameAssemblyTheory]
    public void EveryMirroredHarvestDiscriminantStillHoldsItsNumber(
        string enumType,
        string member,
        int expected)
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var members = assembly.GetInt32EnumMembers(enumType);

        Assert.True(
            members.TryGetValue(member, out var actual),
            $"{enumType} no longer declares {member}, which the harvest safety gate compares.");
        Assert.Equal(expected, actual);
    }
}

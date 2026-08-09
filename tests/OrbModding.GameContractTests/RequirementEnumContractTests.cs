using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The requirement discriminants <c>WorldRequirementEvaluator</c> hard-codes as integers.
/// </summary>
/// <remarks>
/// <para>
/// The evaluator branches on <c>reqType</c> as a bare <c>int</c> — <c>row.ReqType == UpgradeOneLevel</c>
/// — because mirroring the enums as suite types would keep compiling while a build renumbered them.
/// The manifest declares each member as <c>mirrored</c>, which proves the member still exists and
/// still belongs to that enum; nothing there can prove its <em>value</em>, and the value is the
/// whole dependency.
/// </para>
/// <para>
/// This is what fails when a member is renumbered. Without it the evaluator would keep answering,
/// silently reading one requirement as another — every one of these branches decides whether a
/// player-facing row says a prerequisite is met.
/// </para>
/// </remarks>
public sealed class RequirementEnumContractTests
{
    // WorldRequirementEvaluator.UpgradeOneLevel / UpgradeMaxLevel / UpgradeAtLeast
    [InlineData("Requirements.UpgradeRequirementType", "OneLevel", 0)]
    [InlineData("Requirements.UpgradeRequirementType", "MaxLevel", 1)]
    [InlineData("Requirements.UpgradeRequirementType", "AtLeast", 2)]
    // StructureQuantity
    [InlineData("Requirements.StructureRequirementType", "Quantity", 0)]
    // SpellDiscovered / SpellLevel / SpellMasteryLevel
    [InlineData("Requirements.SpellRequirementType", "Discovered", 0)]
    [InlineData("Requirements.SpellRequirementType", "SpellLevel", 2)]
    [InlineData("Requirements.SpellRequirementType", "MasteryLevel", 3)]
    // AlchemyDiscovered / AlchemyRecipeLevel / AlchemyMasteryLevel / AlchemyAdvancementLevel
    [InlineData("Requirements.AlchemyRecipeType", "Discovered", 0)]
    [InlineData("Requirements.AlchemyRecipeType", "RecipeLevel", 2)]
    [InlineData("Requirements.AlchemyRecipeType", "MasteryLevel", 3)]
    [InlineData("Requirements.AlchemyRecipeType", "AdvLevel", 4)]
    // RitualDiscovered / RitualReachedLevel
    [InlineData("Requirements.RitualRequirementType", "Discovered", 0)]
    [InlineData("Requirements.RitualRequirementType", "ReachedLevel", 1)]
    // NumberValue
    [InlineData("Requirements.NumberRequirementType", "Value", 0)]
    // GenericLevel
    [InlineData("Requirements.GenericRequirementType", "Level", 1)]
    // PrerequisiteLinkBase / PrerequisiteLinkTier
    [InlineData("Requirements.PrerequisiteLinkType", "Base", 0)]
    [InlineData("Requirements.PrerequisiteLinkType", "Tier", 1)]
    // ListAnyVisible / ListAnyAvailable
    [InlineData("Requirements.ListRequirementType", "AnyVisible", 1)]
    [InlineData("Requirements.ListRequirementType", "AnyAvailable", 2)]
    [GameAssemblyTheory]
    public void EveryMirroredRequirementDiscriminantStillHoldsItsNumber(
        string enumType,
        string member,
        int expected)
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var members = assembly.GetInt32EnumMembers(enumType);

        Assert.True(
            members.TryGetValue(member, out var actual),
            $"{enumType} no longer declares {member}, which the requirement evaluator branches on.");
        Assert.Equal(expected, actual);
    }
}

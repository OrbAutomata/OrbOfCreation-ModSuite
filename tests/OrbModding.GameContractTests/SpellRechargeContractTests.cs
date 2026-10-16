using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// One spell carries three different recharge quantities, and only one of them is on any screen.
/// The wire published the recipe asset's authored constant, which is none of the ones the game
/// prints.
/// </summary>
public sealed class SpellRechargeContractTests
{
    /// <summary>
    /// The bar's number: the instance's cooldown after cooldown speed.
    /// </summary>
    [GameAssemblyFact]
    public void TheRechargeIsTheCooldownTimeDividedByCooldownSpeed()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var recharge = Assert.Single(assembly.GetMethods("Spell", "GetRecharge"));
        Assert.Equal("public", recharge.Visibility);
        Assert.False(recharge.IsStatic);
        Assert.Equal("BigDouble", recharge.ReturnType);
        Assert.Empty(recharge.ParameterTypes);

        Assert.True(assembly.MethodReferencesMethod("Spell", "GetRecharge", "Spell", "GetCooldown"));
        Assert.True(assembly.MethodReferencesField(
            "Spell", "GetRecharge", "Spell", "rechargeProcessor"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "GetCooldown", "Spell", "GetCooldownTime"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "GetCooldown", "Spell", "GetCooldownSpeed"));
    }

    /// <summary>
    /// What the casting bar counts down, and what the recipe tooltip heads with, are the same two
    /// transforms — the bar applies them to the remaining amount. Neither reads the recipe asset's
    /// authored duration.
    /// </summary>
    [GameAssemblyFact]
    public void TheBarAndTheTooltipHeadlineBothCountTheInstanceRecharge()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UISpellButton", "RenderContent", "Spell", "GetRechargeRemaining"));
        Assert.True(assembly.MethodReferencesMethod(
            "Spell", "GetRechargeRemaining", "Spell", "GetCooldownTimeRemaining"));

        var headline = assembly
            .GetMethodBodyDefinitionReferences("SpellRecipeSO", "GetRealTooltipNodes")
            .Concat(assembly.GetMethodBodyMemberReferences("SpellRecipeSO", "GetRealTooltipNodes"))
            .ToArray();
        Assert.Contains(headline, reference =>
            reference.DeclaringType == "Spell" && reference.MemberName == "GetRecharge");
        Assert.Contains(headline, reference =>
            reference.DeclaringType == "Duration" &&
            reference.MemberName == "StylizeAccurateText");
        Assert.DoesNotContain(headline, reference =>
            reference.DeclaringType == "SpellRecipeSO" && reference.MemberName == "baseRecharge");
    }

    /// <summary>
    /// Which spelling a recharge gets is the processor's core type, and that core type is the
    /// recipe's own authored <c>baseRecharge.type</c>: <c>CreateProcessor</c> seeds the processor
    /// with exactly one module built from it, and <c>GetCoreType</c> answers with the first
    /// module's. So the published authoring says which of the two spellings a slot's number takes.
    /// </summary>
    [GameAssemblyFact]
    public void TheRechargeSpellingFollowsTheRecipesAuthoredProcessorType()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesField(
            "Duration+Entry", "CreateProcessor", "Duration+Entry", "type"));
        Assert.True(assembly.MethodReferencesMethod(
            "Duration+Entry", "CreateProcessor", "Duration+Processor", "AddModule"));
        Assert.True(assembly.MethodReferencesField(
            "Duration+Processor", "GetCoreType", "Duration+Processor", "modules"));
        Assert.True(assembly.MethodReferencesField(
            "Duration+Processor", "GetCoreType", "Duration+MicroProcessor", "t"));

        var stylize = assembly
            .GetMethodBodyDefinitionReferences("Duration", "StylizeAccurateText")
            .Concat(assembly.GetMethodBodyMemberReferences("Duration", "StylizeAccurateText"))
            .ToArray();
        Assert.Contains(stylize, reference =>
            reference.DeclaringType == "Utils" && reference.MemberName == "BeautifyTimeAccurate");
        Assert.Contains(stylize, reference =>
            reference.DeclaringType == "Utils" && reference.MemberName == "BeautifyNumber");
    }

    /// <summary>
    /// The authored constant the wire used to publish is still captured — the world carries every
    /// game fact — and it is no longer any player row's recharge.
    /// </summary>
    [Fact]
    public void ManifestNamesBothTheAuthoredConstantAndTheInstanceRecharge()
    {
        var manifest = NativeContractManifest.Load();

        Assert.Single(manifest.Contracts, contract => contract.Id == "spell-recipe.base-recharge");
        Assert.Single(manifest.Contracts, contract => contract.Id == "spell.get-recharge");
    }
}

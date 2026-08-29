using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class GenericDiscoverableContractTests
{
    [GameAssemblyFact]
    public void DiscoverableInterface_HasExactlySixImplementersAndPinnedMemberTokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            new[]
            {
                "AlchemyRecipeSO",
                "EquipmentSO",
                "GlyphSO",
                "RitualSO",
                "SpellRecipeSO",
                "TimeRuneSO",
            },
            assembly.GetTypesImplementing("IDiscoverable"));
        Assert.Equal(0x06001C8F, assembly.GetMethodToken("IDiscoverable", "GetGlyphRecipe"));
        Assert.Equal(0x06001C90, assembly.GetMethodToken("IDiscoverable", "GetResourceRecipe"));
        Assert.Equal(0x06001C92, assembly.GetMethodToken("IDiscoverable", "GetDiscoverCost"));
        Assert.Equal(0x06001C93, assembly.GetMethodToken("IDiscoverable", "IsDiscoverVisible"));
        Assert.Equal(0x06001C94, assembly.GetMethodToken("IDiscoverable", "CanDiscover"));
        Assert.Equal(0x06001C95, assembly.GetMethodToken("IDiscoverable", "IsDiscovered"));
        Assert.Equal(0x06001C96, assembly.GetMethodToken("IDiscoverable", "IsDiscoverRequired"));
        Assert.Equal(0x06001C97, assembly.GetMethodToken("IDiscoverable", "Discover"));
    }

    [GameAssemblyFact]
    public void EveryConcreteDiscoveryPipeline_KeepsItsPinnedVerdictAndMutationTokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertTokens(assembly, "AlchemyRecipeSO", 0x06000863, 0x06000845, 0x06000844, 0x06000850);
        AssertTokens(assembly, "EquipmentSO", 0x06000B35, 0x06000B3B, 0x06000B3C, 0x06000B10);
        AssertTokens(assembly, "GlyphSO", 0x06000BF3, 0x06000BF5, 0x06000BF6, 0x06000BFB);
        AssertTokens(assembly, "RitualSO", 0x060013BB, 0x060013C2, 0x060013C3, 0x06001366);
        AssertTokens(assembly, "SpellRecipeSO", 0x06001442, 0x06001451, 0x06001454, 0x06001432);
        AssertTokens(assembly, "TimeRuneSO", 0x06001851, 0x06001855, 0x06001856, 0x06001858);
    }

    [GameAssemblyFact]
    public void GenericDiscoverableUi_UsesInterfaceCallbackAndCostButtonPaysBeforeCallback()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x0600231C, assembly.GetMethodToken("UIDiscoverablePage", "HandleClick"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "HandleClick", "IDiscoverable", "Discover"));

        var enough = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "HasEnough");
        var payment = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "PerformCost");
        var callback = assembly.GetMethodBodyMemberReferences("UICostButton", "OnClick")
            .Where(reference => reference.MemberName == "Invoke")
            .Select(reference => reference.Offset)
            .DefaultIfEmpty(-1)
            .Max();
        Assert.True(enough >= 0, "UICostButton.OnClick must check native affordability.");
        Assert.True(payment > enough, "UICostButton.OnClick must pay only after affordability.");
        Assert.True(
            callback > payment,
            "UICostButton.OnClick must invoke the page callback only after payment. Native refs: " +
            string.Join("; ", References(assembly, "UICostButton", "OnClick")));
    }

    /// <summary>
    /// The button prices the row it is showing and admits the press on that row's own verdicts, so
    /// naming the row names the whole press: there is nothing else on the screen to say.
    /// </summary>
    /// <remarks>
    /// The selection the page keeps is the row's own authored recipe, copied in by
    /// <c>OnDiscoverableClick</c> and read back only to re-derive the same row. Nothing on the
    /// discover path reads it, which is why the suite presses the row directly.
    /// </remarks>
    [GameAssemblyFact]
    public void DiscoverButton_PricesTheShownRowAndAdmitsOnThatRowsOwnVerdicts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        var price = assembly.MethodReferenceOffset(
            "UIDiscoverablePage", "Render", "IDiscoverable", "GetDiscoverCost");
        var handOver = assembly.MethodReferenceOffset(
            "UIDiscoverablePage", "Render", "UICostButton", "SetCost");
        Assert.True(price >= 0, "The page must price the row it resolved.");
        Assert.True(handOver > price, "The button must charge exactly that price.");
        Assert.True(assembly.MethodReferencesField(
            "UIDiscoverablePage", "Render", "UIDiscoverablePage", "totalCost"));

        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "IsGlyphSelectionValid", "ResourceCostList", "HasEnough"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "IsGlyphSelectionValid", "IDiscoverable", "CanDiscover"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "IsGlyphSelectionValid", "IDiscoverable", "IsDiscovered"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "HandleClick", "UIDiscoverablePage", "IsGlyphSelectionValid"),
            "The press is admitted by the same predicate the button is drawn from. Native refs: " +
            string.Join("; ", References(assembly, "UIDiscoverablePage", "HandleClick")));

        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "OnDiscoverableClick", "IDiscoverable", "GetGlyphRecipe"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoverablePage", "OnDiscoverableClick", "IDiscoverable", "GetResourceRecipe"));
    }

    [GameAssemblyFact]
    public void EquipmentDiscovery_DelegatesToCreateAsTheArtifactOutcome()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06000B11, assembly.GetMethodToken("EquipmentSO", "Create"));
        Assert.True(assembly.MethodReferencesMethod(
            "EquipmentSO", "Discover", "EquipmentSO", "Create"));
    }

    private static void AssertTokens(
        GameAssemblyMetadata assembly,
        string type,
        int cost,
        int canDiscover,
        int isDiscovered,
        int discover)
    {
        Assert.Equal(cost, assembly.GetMethodToken(type, "GetDiscoverCost"));
        Assert.Equal(canDiscover, assembly.GetMethodToken(type, "CanDiscover"));
        Assert.Equal(isDiscovered, assembly.GetMethodToken(type, "IsDiscovered"));
        Assert.Equal(discover, assembly.GetMethodToken(type, "Discover"));
    }

    private static string[] References(
        GameAssemblyMetadata assembly,
        string type,
        string method) =>
        assembly.GetMethodBodyDefinitionReferences(type, method)
            .Concat(assembly.GetMethodBodyMemberReferences(type, method))
            .OrderBy(reference => reference.Offset)
            .Select(reference =>
                $"IL_{reference.Offset:X4} 0x{reference.Token:X8} " +
                $"{reference.DeclaringType}.{reference.MemberName}")
            .ToArray();
}

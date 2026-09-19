using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class DiscoveryTreeOfferContractTests
{
    [GameAssemblyFact]
    public void DiscoveryTreeDecisionReader_PinsEveryNewNativeMemberToken()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x04000647, assembly.GetFieldToken("DiscoveryTreeSO", "currentChoiceIds"));
        Assert.Equal(0x06001B44, assembly.GetMethodToken("GuidContainer", "get_guid"));
        Assert.Equal(0x06000AD5, assembly.GetMethodToken("DiscoveryTreeSO", "IsVisible"));
        Assert.Equal(0x06000AC6,
            assembly.GetMethodToken("DiscoveryTreeSO", "HasImmediateRequiredDiscover"));
        Assert.Equal(0x06000AB8, assembly.GetMethodToken("DiscoveryTreeSO", "GetNextItemCost"));
        Assert.Equal(0x06001E50, assembly.GetMethodToken("ResourceCostList", "GetEntries"));
        Assert.Equal(0x06001E0F, assembly.GetMethodToken("ResourceCostList", "HasEnough"));
        Assert.Equal(0x06001F96, assembly.GetMethodToken("ResourceTuple", "GetValue"));
        Assert.Equal(0x060012BE, assembly.GetMethodToken("ResourceSO", "GetTrueQuantity"));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeOffersCanContainExactlyThePublishedExplainableEntityFamilies()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var discoverables = new[]
        {
            "AlchemyRecipeSO",
            "EquipmentSO",
            "GlyphSO",
            "RitualSO",
            "SpellRecipeSO",
            "TimeRuneSO",
        };
        Assert.Equal(discoverables, assembly.GetTypesImplementing("IDiscoverable"));
        Assert.All(discoverables, type =>
            Assert.True(
                assembly.ImplementsInterface(type, "ITooltipable"),
                type + " does not implement ITooltipable through its native base chain"));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeInitiate_IsSynchronousAndEntersCraftingModeWithoutUi()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x06000AA8, assembly.GetMethodToken("DiscoveryTreeSO", "InitiateCraftingMode"));
        Assert.Equal(0x06000AA9, assembly.GetMethodToken("DiscoveryTreeSO", "EnterCraftingMode"));
        Assert.Equal(0x06000AA7, assembly.GetMethodToken("DiscoveryTreeSO", "EnterMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "InitiateCraftingMode", "DiscoveryTreeSO", "EnterCraftingMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "EnterCraftingMode", "DiscoveryTreeSO", "EnterMode"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "EnterMode", "DiscoveryTreeSO", "actionMode"));

        var synchronousReferences = References(assembly, "DiscoveryTreeSO", "InitiateCraftingMode")
            .Concat(References(assembly, "DiscoveryTreeSO", "EnterCraftingMode"))
            .Concat(References(assembly, "DiscoveryTreeSO", "EnterMode"));
        Assert.DoesNotContain(synchronousReferences, reference =>
            reference.Contains("UIDiscoveryTreePage", System.StringComparison.Ordinal));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeOffer_MatchesCompleteLifecycleBindingSet()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.HasType("DiscoveryTreeSO"));
        Assert.True(assembly.HasType("DiscoveryTreeSO+DiscoveryTreeModes"));
        Assert.True(assembly.HasType("GuidContainer"));
        Assert.True(assembly.HasType("IDiscoverable"));
        Assert.True(assembly.HasType("IHasGuid"));
        Assert.Equal(
            new[] { "Idle", "Crafting", "Choice" },
            assembly.GetInt32EnumMembers("DiscoveryTreeSO+DiscoveryTreeModes").Keys.ToArray());

        Assert.Equal("System.Collections.Generic.List`1<DiscoveryTreeSO>",
            assembly.GetFieldType("DiscoveryTreeSO", "All"));
        Assert.Equal("DiscoveryTreeSO+DiscoveryTreeModes",
            assembly.GetFieldType("DiscoveryTreeSO", "actionMode"));
        Assert.Equal("System.Int32", assembly.GetFieldType("DiscoveryTreeSO", "rerollsLeft"));
        Assert.Equal("System.Collections.Generic.List`1<GuidContainer>",
            assembly.GetFieldType("DiscoveryTreeSO", "currentChoiceIds"));
        Assert.Equal("GuidContainer",
            assembly.GetFieldType("DiscoveryTreeSO", "selectedChoiceId"));

        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "GuidContainer", "get_guid", false, "System.Guid");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsVisible", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsInIdleMode", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsInCraftingMode", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "IsInChoiceMode", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "HasCurrentlyRemMainPoolDiscoveries", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "HasImmediateRequiredDiscover", false, "System.Boolean");
        AssertMethod(assembly, "DiscoveryTreeSO", "GetNextItemCost", false, "ResourceCostList");
        AssertMethod(assembly, "DiscoveryTreeSO", "GetItemFromGuid", false, "IDiscoverable", "System.Guid");
        AssertMethod(assembly, "DiscoveryTreeSO", "InitiateCraftingMode", false, "System.Void");
        AssertMethod(assembly, "DiscoveryTreeSO", "SelectItemId", false, "System.Void", "System.Guid");
        AssertMethod(assembly, "DiscoveryTreeSO", "DiscoverSelectedItem", false, "System.Void");
        AssertMethod(assembly, "DiscoveryTreeSO", "RerollChoices", false, "System.Void");
        AssertMethod(assembly, "IHasGuid", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "IDiscoverable", "IsDiscoverRequired", false, "System.Boolean");
        AssertMethod(assembly, "ResourceCostList", "HasEnough", false, "System.Boolean");
        AssertMethod(assembly, "ResourceCostList", "PerformCost", false, "System.Void");
    }

    [GameAssemblyFact]
    public void DiscoveryTreeUi_ReachesTheAuditedDataPipelineAndPaysBeforeInitiate()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnDiscoveryClick", "DiscoveryTreeSO", "IsInIdleMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnDiscoveryClick", "DiscoveryTreeSO", "InitiateCraftingMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "SelectItemGuid", "DiscoveryTreeSO", "SelectItemId"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnConfirmClick", "DiscoveryTreeSO", "DiscoverSelectedItem"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnRerollClick", "DiscoveryTreeSO", "RerollChoices"));

        var enough = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "HasEnough");
        var payment = assembly.MethodReferenceOffset(
            "UICostButton", "OnClick", "ResourceCostList", "PerformCost");
        Assert.True(enough >= 0, "UICostButton.OnClick must check HasEnough.");
        Assert.True(payment > enough, "UICostButton.OnClick must perform cost after affordability.");
        Assert.False(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "InitiateCraftingMode", "ResourceCostList", "PerformCost"));
    }

    [GameAssemblyFact]
    public void DiscoveryTreeNativeStages_MaterializeOffersThroughThePublishedChoiceList()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "IncrementCrafting", "DiscoveryTreeSO", "EnterChoiceMode"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "EnterChoiceMode", "DiscoveryTreeSO", "currentChoiceIds"));
    }

    /// <summary>
    /// The game draws Confirm on two facts and presses it with no third one: the tree is in choice
    /// mode, something is selected. DiscoverItem itself counts, resets the mode and calls
    /// Discover() — with no already-discovered check and no cost anywhere on the path — so the
    /// transition the suite verifies is the tree leaving choice mode, and the suite adds no gate
    /// the button does not have.
    /// </summary>
    [GameAssemblyFact]
    public void ConfirmIsDrawnOnChoiceModeAndASelectionAndCostsNothingToPress()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "QuickRenderConfirmButton", "DiscoveryTreeSO", "IsInChoiceMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "QuickRenderConfirmButton",
            "DiscoveryTreeSO", "HasChoiceSelected"));

        // The suite reads the selection off the same field the button's rule reads.
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "HasChoiceSelected", "DiscoveryTreeSO", "selectedChoiceId"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "HasChoiceSelected", "GuidContainer", "IsEmpty"));

        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "DiscoverSelectedItem", "DiscoveryTreeSO", "DiscoverItem"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "DiscoverItem", "DiscoveryTreeSO", "ResetMode"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "DiscoverItem", "IDiscoverable", "Discover"));
        Assert.False(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "DiscoverItem", "IDiscoverable", "IsDiscovered"));
        Assert.False(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "DiscoverItem", "ResourceCostList", "PerformCost"));
        Assert.False(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnConfirmClick", "ResourceCostList", "PerformCost"));

        // ResetMode is what a caller gets back: idle, no offers, no selection.
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "ResetMode", "DiscoveryTreeSO", "EnterMode"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "ResetMode", "DiscoveryTreeSO", "currentChoiceIds"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "ResetMode", "DiscoveryTreeSO", "selectedChoiceId"));

        var reset = assembly.MethodReferenceOffset(
            "DiscoveryTreeSO", "DiscoverItem", "DiscoveryTreeSO", "ResetMode");
        var discover = assembly.MethodReferenceOffset(
            "DiscoveryTreeSO", "DiscoverItem", "IDiscoverable", "Discover");
        Assert.True(reset < discover,
            "DiscoverItem resets the mode before it discovers, so a failure between the two " +
            "leaves the press landed and the ledger short.");
    }

    /// <summary>
    /// A reroll is drawn on the budget alone. A required discovery makes a reroll a waste, which is
    /// advice about what the press buys, not a rule about whether the game offers it.
    /// </summary>
    [GameAssemblyFact]
    public void RerollIsDrawnOnTheBudgetAloneAndNotOnARequiredDiscovery()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnRerollClick", "DiscoveryTreeSO", "HasRerolls"));
        Assert.False(assembly.MethodReferencesMethod(
            "UIDiscoveryTreePage", "OnRerollClick",
            "DiscoveryTreeSO", "HasImmediateRequiredDiscover"));
        Assert.False(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "RerollChoices", "DiscoveryTreeSO", "HasImmediateRequiredDiscover"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "RerollChoices", "DiscoveryTreeSO", "rerollsLeft"));
    }

    /// <summary>
    /// The press starts a roll; the offers exist three seconds later. The wire says so rather than
    /// leaving an empty offer list to read as a press that did nothing.
    /// </summary>
    [GameAssemblyFact]
    public void ARollTakesThreeSecondsBeforeTheGameHasAnyOffers()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(3f, assembly.GetSingleConstant("DiscoveryTreeSO", "CraftTime"));
        Assert.True(assembly.MethodReferencesField(
            "DiscoveryTreeSO", "IncrementCrafting", "DiscoveryTreeSO", "actionTime"));
        Assert.True(assembly.MethodReferencesMethod(
            "DiscoveryTreeSO", "IncrementCrafting", "DiscoveryTreeSO", "EnterChoiceMode"));
    }

    /// <summary>
    /// What a confirmed spell offer does next: the game mints a level-0 copy of the recipe and loads
    /// it only when the loadout has a free spot and the new spell's usage cost fits. The post-state
    /// reports both halves off the settled world rather than predicting either.
    /// </summary>
    [GameAssemblyFact]
    public void DiscoveringASpellMintsALevelZeroCopyAndLoadsItOnlyIntoAFreeSpot()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "SpellRecipeSO", "Discover", "SpellManager", "PostDiscoverRecipe"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellManager", "PostDiscoverRecipe", "SpellRecipeSO", "CreateEmpty"));
        Assert.True(assembly.MethodReferencesField(
            "SpellManager", "PostDiscoverRecipe", "SpellManager", "activeSpells"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellManager", "PostDiscoverRecipe", "ResourceCostList", "HasEnough"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellManager", "PostDiscoverRecipe", "SpellManager", "AddSpell"));

        var empty = assembly.MethodReferenceOffset(
            "SpellManager", "PostDiscoverRecipe", "SpellRecipeSO", "CreateEmpty");
        var add = assembly.MethodReferenceOffset(
            "SpellManager", "PostDiscoverRecipe", "SpellManager", "AddSpell");
        Assert.True(empty >= 0 && empty < add,
            "The copy is minted before it is loaded, so a loaded spell is always the new one.");
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameterTypes)
    {
        var matches = assembly.GetMethods(typeName, methodName);
        Assert.Contains(matches, method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameterTypes));
    }

    private static string[] References(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName) =>
        assembly.GetMethodBodyDefinitionReferences(typeName, methodName)
            .Select(reference =>
                $"IL_{reference.Offset:X4} 0x{reference.Token:X8} {reference.Kind} " +
                $"{reference.DeclaringType}.{reference.MemberName}")
            .ToArray();
}

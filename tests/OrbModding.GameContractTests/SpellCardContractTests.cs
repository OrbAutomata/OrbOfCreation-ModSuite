using System;
using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The spell card on Magic > Spellbook > Spells: what opens it, and what its Confirm Mastery
/// button presses.
/// </summary>
public sealed class SpellCardContractTests
{
    /// <summary>
    /// Clicking a spell's icon toggles the page's selected-recipe variable, so a second click on an
    /// open card closes it. <c>game_navigate</c> clicks only when the variable does not already hold
    /// the spell, and reads it back with <c>IsItem</c>.
    /// </summary>
    [GameAssemblyFact]
    public void Clicking_a_spell_toggles_the_variable_its_card_is_drawn_from()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "SpellRecipeVariable",
            assembly.GetFieldType("UISpellRecipeList", "selectedSpellRecipe"));
        Assert.Contains(
            assembly.GetMethods("UISpellRecipeList", "ClickRecipe"),
            method => !method.IsStatic && method.ReturnType == "System.Void" &&
                method.ParameterTypes.Count == 1 && method.ParameterTypes[0] == "SpellRecipeSO");
        Assert.True(assembly.MethodReferencesField(
            "UISpellRecipeList", "ClickRecipe", "UISpellRecipeList", "selectedSpellRecipe"));
        Assert.Contains(
            assembly.GetMethodBodyMemberReferences("UISpellRecipeList", "ClickRecipe"),
            reference => reference.MemberName == "ToggleValue" &&
                reference.DeclaringType.Contains("AbstractVariable`1", StringComparison.Ordinal));
    }

    /// <summary>
    /// The card's Confirm Mastery button is priced by <c>GetLevelCost()</c> and calls
    /// <c>PurchaseLevel()</c> — the pair <c>game_spell_mastery single</c> performs before it
    /// verifies the mastery level rose by exactly one — and readiness is the spell's own mastery
    /// bar and nothing else.
    /// </summary>
    [GameAssemblyFact]
    public void Confirm_mastery_is_the_level_cost_and_purchase_the_mastery_verb_presses()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeItem", "ForceRenderActionButton", "SpellRecipeSO", "GetLevelCost"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeItem", "ForceRenderActionButton", "SpellRecipeSO", "PurchaseLevel"));
        Assert.True(assembly.MethodReferencesField(
            "SpellRecipeSO", "IsReadyToLevelMastery", "SpellRecipeSO", "masteryXpContainer"));
        Assert.True(assembly.MethodReferencesField(
            "SpellRecipeSO", "PurchaseLevel", "SpellRecipeSO", "masteryLevel"));
    }

    /// <summary>
    /// What a confirm grants each type is <c>ModTypeXp(spellTypeXpPerMastery)</c>: the per-level
    /// global through the type's Type Xp and the player's All Spell Types Xp. The grant applies it,
    /// and the card prints it beside each type track, so the world folds those three readings.
    /// </summary>
    [GameAssemblyFact]
    public void The_type_xp_a_confirm_grants_is_the_per_level_global_through_both_type_xp_modifiers()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.Double", assembly.GetFieldType("GlobalValues", "spellTypeXpPerMastery"));
        Assert.True(assembly.MethodReferencesField(
            "SpellRecipeSO", "GetLevelSpellTypeXp", "GlobalValues", "spellTypeXpPerMastery"));
        Assert.True(assembly.MethodReferencesField(
            "SpellTypeSO", "ModTypeXp", "SpellTypeSO", "typeXpMod"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellTypeSO", "ModTypeXp", "Player", "GetAllSpellTypeXpMod"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellTypeSO", "ModTypeXp", "DoubleVariable", "AsPercent"));
        Assert.True(assembly.MethodReferencesMethod(
            "SpellTypeSO", "GainTypeXp", "SpellTypeSO", "ModTypeXp"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellRecipeItem", "RenderContent", "SpellRecipeSO", "GetLevelSpellTypeXp"));
        Assert.True(assembly.MethodReferencesMethod(
            "UISpellTypeItem", "RenderContent", "SpellTypeSO", "ModTypeXp"));
        Assert.Contains(
            assembly.GetMethodBodyDefinitionReferences("UISpellTypeItem", "RenderContent"),
            reference => reference.MemberName == "BeautifyNumber" &&
                reference.DeclaringType == "Utils");
    }
}

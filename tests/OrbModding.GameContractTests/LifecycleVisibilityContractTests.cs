using Xunit;

namespace OrbModding.GameContractTests;

/// <summary>
/// The members each category's own row renderer decides visibility on, pinned — because those
/// members are what the published <c>state</c> word means, and a surface that says "locked" is
/// making a claim about what the player is shown.
/// </summary>
public sealed class LifecycleVisibilityContractTests
{
    /// <summary>
    /// The five row renderers behind the lifecycle word, each asked for the member the suite
    /// derives that category's word from. A renderer that started asking something else would make
    /// the word a claim about a member nobody renders on.
    /// </summary>
    [GameAssemblyFact]
    public void EveryRowRendererDecidesVisibilityOnTheMemberTheSurfaceWordsItFrom()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "UIAlchemyRecipe", "IsVisible", "AlchemyRecipeSO", "IsAvailable"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIGlyphListItem", "IsVisible", "GlyphSO", "IsAvailable"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIRitual", "IsVisible", "RitualSO", "IsDiscovered"));
        Assert.True(assembly.MethodReferencesMethod(
            "UIPlotNode", "IsVisible", "PlotNodeSO", "IsVisible"));

        // The undiscovered ritual placeholder is the other half of the ritual answer: a locked
        // ritual is not an absent row, it is a row that is not the ritual.
        Assert.True(assembly.MethodReferencesMethod(
            "UIUndiscoveredRitual", "IsVisible", "RitualSO", "IsDiscovered"));
    }

    /// <summary>
    /// A glyph, a ritual and a plot node hand their whole lock to one published member, so the
    /// suite's two-word derivation for each is the game's own predicate rather than a transcription
    /// of it.
    /// </summary>
    [GameAssemblyFact]
    public void ThreeCategoriesCollapseTheirWholeLockOntoOnePublishedMember()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        // RitualSO.IsAvailable() and IsVisible() are both IsDiscovered(), which is the `discovered`
        // field the world already publishes.
        Assert.True(assembly.MethodReferencesMethod(
            "RitualSO", "IsAvailable", "RitualSO", "IsDiscovered"));
        Assert.True(assembly.MethodReferencesMethod(
            "RitualSO", "IsVisible", "RitualSO", "IsDiscovered"));
        Assert.True(assembly.MethodReferencesField(
            "RitualSO", "IsDiscovered", "RitualSO", "discovered"));

        // GlyphSO.IsVisible() is GlyphSO.IsAvailable(), which the binder already reads whole.
        Assert.True(assembly.MethodReferencesMethod(
            "GlyphSO", "IsVisible", "GlyphSO", "IsAvailable"));

        // PlotNodeSO.IsVisible() is the `visible` field and nothing else.
        Assert.True(assembly.MethodReferencesField(
            "PlotNodeSO", "IsVisible", "PlotNodeSO", "visible"));
    }

    /// <summary>
    /// The reason <c>alchemy-recipe.visibility-type</c> is captured at all: a recipe's lock is
    /// <c>discovered</c> on one branch and a prerequisite container on the other, and only
    /// <c>visibilityType</c> says which. Reading <c>discovered</c> alone would be right by authored
    /// coincidence rather than by construction.
    /// </summary>
    [GameAssemblyFact]
    public void AnAlchemyRecipesLockIsTwoBranchesSelectedByTheCapturedVisibilityType()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "AlchemyRecipeSO", "IsAvailable", "AlchemyRecipeSO", "IsDiscoveredRecipe"));
        Assert.True(assembly.MethodReferencesMethod(
            "AlchemyRecipeSO", "IsAvailable", "AlchemyRecipeSO", "IsDiscovered"));

        // The other branch is the write the capture exists to avoid: Check() latches, and a
        // per-pass capture may not write.
        Assert.True(assembly.MethodReferencesField(
            "AlchemyRecipeSO", "IsAvailable", "AlchemyRecipeSO", "visibilityPrerequisites"));
        Assert.Equal(
            "Prerequisites+Container",
            assembly.GetFieldType("AlchemyRecipeSO", "visibilityPrerequisites"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("Prerequisites+Container", "available"));

        // The selector, and the value the suite treats as the discovery branch.
        Assert.True(assembly.MethodReferencesField(
            "AlchemyRecipeSO", "IsDiscoveredRecipe", "AlchemyRecipeSO", "visibilityType"));
        Assert.Equal(
            "AlchemyRecipeSO+VisibilityType",
            assembly.GetFieldType("AlchemyRecipeSO", "visibilityType"));
        Assert.Equal(
            0,
            assembly.GetInt32EnumMembers("AlchemyRecipeSO+VisibilityType")["Discover"]);
    }

    /// <summary>
    /// Challenges are the one category here with a real ceiling, and the order the suite composes
    /// its three words in is the order the game's own predicate composes them in: exhaustion first,
    /// because <c>IsAvailableToRun()</c> returns false the moment <c>IsMaxLevel()</c> holds.
    /// </summary>
    [GameAssemblyFact]
    public void AChallengesAvailabilityIsAskedAfterItsCeilingExactlyAsTheGameAsksIt()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.MethodReferencesMethod(
            "ChallengeSO", "IsAvailableToRun", "ChallengeSO", "IsMaxLevel"));
        Assert.True(assembly.MethodReferencesField(
            "ChallengeSO", "IsAvailableToRun", "ChallengeSO", "availabilityPrerequisites"));
        Assert.True(assembly.MethodReferencesMethod(
            "ChallengeSO", "IsMaxLevel", "ChallengeSO", "HasMaxLevel"));

        // The run enum the `run` column carries, whose five values are a run's own outcome and
        // never a lifecycle. The game names them itself, one tooltip per value.
        var states = assembly.GetInt32EnumMembers("ChallengeSO+ChallengeState");
        Assert.Equal(0, states["None"]);
        Assert.Equal(1, states["QueuedStart"]);
        Assert.Equal(2, states["CurrentlyActive"]);
        Assert.Equal(3, states["Passed"]);
        Assert.Equal(4, states["Failed"]);
        Assert.Equal(5, states.Count);
    }
}

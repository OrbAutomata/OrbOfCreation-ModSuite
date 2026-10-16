using System;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class PrestigeContractTests
{
    [GameAssemblyFact]
    public void Public_reset_is_only_the_screen_fade_wrapper_around_the_private_transaction()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x06000651, assembly.GetMethodToken("PersistentResetManager", "PersistentReset"));
        Assert.Equal(0x06000652, assembly.GetMethodToken("PersistentResetManager", "PersistentResetLogic"));
        Assert.True(assembly.MethodReferencesMethod("PersistentResetManager", "PersistentReset",
            "UIScreenFlash", "FadeIn"));
        Assert.True(assembly.MethodReferencesMethod("PersistentResetManager", "PersistentReset",
            "PersistentResetManager", "PersistentResetLogic"));
    }

    [GameAssemblyFact]
    public void Reset_modal_admission_reads_world_cycle_and_fetched_challenge_flags()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("BoolVariable", assembly.GetFieldType(
            "UIPersistentResetModal", "hasCompleteWorldCycle"));
        Assert.Equal("BoolVariable", assembly.GetFieldType(
            "UIPersistentResetModal", "hasFetchedChallenges"));
        Assert.Equal("BoolVariable", assembly.GetFieldType(
            "PersistentResetManager", "hasCompleteWorldCycle"));
        Assert.Equal("BoolVariable", assembly.GetFieldType(
            "PersistentResetManager", "hasFetchedChallenges"));
        var references = assembly.GetMethodBodyDefinitionReferences(
            "UIPersistentResetModal", "ResetWorldInteractable");
        Assert.Contains(references, reference => reference.MemberName == "hasCompleteWorldCycle" &&
            reference.DeclaringType == "UIPersistentResetModal");
        Assert.Contains(references, reference => reference.MemberName == "hasFetchedChallenges" &&
            reference.DeclaringType == "UIPersistentResetModal");
    }

    [GameAssemblyFact]
    public void Native_transaction_persists_then_resets_then_activates_challenges_before_scene_reload()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        var method = "PersistentResetLogic";
        var setup = assembly.MethodReferenceOffset("PersistentResetManager", method,
            "PersistentResetManager", "SetupPersistentValues");
        var reset = assembly.MethodReferenceOffset("PersistentResetManager", method,
            "GameManager", "PersistentResetGameState");
        var rewards = assembly.MethodReferenceOffset("PersistentResetManager", method,
            "ChallengeListVariable", "ActivateRewards");
        var challenges = assembly.MethodReferenceOffset("PersistentResetManager", method,
            "ChallengeListVariable", "Activate");
        var resource = assembly.MethodReferenceOffset("PersistentResetManager", method,
            "PersistentResetManager", "SetPersistentResource");
        var clean = assembly.MethodReferenceOffset("PersistentResetManager", method,
            "GameManager", "CleanGame");
        Assert.True(setup < reset);
        Assert.True(reset < rewards);
        Assert.True(rewards < challenges);
        Assert.True(challenges < resource);
        Assert.True(resource < clean);
    }

    [Fact]
    public void Manifest_names_every_prestige_action_and_predecision_touch()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "prestige.reset-manager.type-action", "prestige.bool-variable.type-action",
            "prestige.reset-manager-instance-action",
            "prestige.reset-cycle-complete-action", "prestige.reset-fetched-action",
            "prestige.bool-get-action", "prestige.reset-logic-action",
            "prestige.persistent-resource-capture", "prestige.persist-value-capture",
            "prestige.persist-value-new-capture", "prestige.persist-value-last-capture",
            "prestige.reset-count-capture", "prestige.resource-guid-capture",
        };
        Assert.All(expected, id => Assert.Single(manifest.Contracts, contract => contract.Id == id));
    }
}

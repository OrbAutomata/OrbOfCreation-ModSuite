using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ChallengeContractTests
{
    [GameAssemblyFact]
    public void Challenge_state_and_target_transitions_keep_the_audited_identity_tokens()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(new[]
        {
            new KeyValuePair<string, int>("None", 0),
            new KeyValuePair<string, int>("QueuedStart", 1),
            new KeyValuePair<string, int>("CurrentlyActive", 2),
            new KeyValuePair<string, int>("Passed", 3),
            new KeyValuePair<string, int>("Failed", 4),
        }, assembly.GetInt32EnumMembers("ChallengeSO+ChallengeState").ToArray());
        Assert.Equal(0x06000936, assembly.GetMethodToken("ChallengeSO", "ToggleQueueActivation"));
        Assert.Equal(0x06000937, assembly.GetMethodToken("ChallengeSO", "AbandonChallenge"));
        Assert.Equal(0x06001639, assembly.GetMethodToken("ChallengeListVariable", "IsChallengeRestricted"));
    }

    [GameAssemblyFact]
    public void Ui_target_verbs_delegate_to_the_exact_native_action_members()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x0600224C, assembly.GetMethodToken("UIChallengeItem", "ToggleActivate"));
        Assert.Equal(0x0600224D, assembly.GetMethodToken("UIChallengeItem", "AbandonActivation"));
        Assert.Equal(0x0600224E, assembly.GetMethodToken("UIChallengeItem", "ToggleSelection"));
        Assert.True(assembly.MethodReferencesMethod("UIChallengeItem", "ToggleActivate",
            "ChallengeSO", "ToggleQueueActivation"));
        Assert.True(assembly.MethodReferencesMethod("UIChallengeItem", "AbandonActivation",
            "ChallengeSO", "AbandonChallenge"));
        Assert.Contains(
            assembly.GetMethodBodyMemberReferences("UIChallengeItem", "ToggleSelection"),
            reference => reference.MemberName == "Toggle" &&
                reference.DeclaringType.Contains("GenericListVariable`1", StringComparison.Ordinal));
    }

    [GameAssemblyFact]
    public void Both_fetch_surfaces_commit_the_reroll_decision_before_replacing_the_offer_list()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x06002444, assembly.GetMethodToken("UITimeScreenManager", "FetchNewChallenges"));
        Assert.Equal(0x060024EE, assembly.GetMethodToken("UIPersistentResetModal", "FetchNewChallenges"));
        Assert.True(assembly.MethodReferenceOffset("UITimeScreenManager", "FetchNewChallenges",
            "IntVariable", "SetValue") < assembly.MethodReferenceOffset(
                "UITimeScreenManager", "FetchNewChallenges", "ChallengeManager", "LoadNewActiveChallenges"));
        Assert.True(assembly.MethodReferenceOffset("UITimeScreenManager", "FetchNewChallenges",
            "BoolVariable", "SetValue") < assembly.MethodReferenceOffset(
                "UITimeScreenManager", "FetchNewChallenges", "ChallengeManager", "LoadNewActiveChallenges"));
        Assert.True(assembly.MethodReferenceOffset("UIPersistentResetModal", "FetchNewChallenges",
            "IntVariable", "SetValue") < assembly.MethodReferenceOffset(
                "UIPersistentResetModal", "FetchNewChallenges", "PersistentResetManager", "FetchNewChallenges"));
        Assert.True(assembly.MethodReferenceOffset("UIPersistentResetModal", "FetchNewChallenges",
            "BoolVariable", "SetValue") < assembly.MethodReferenceOffset(
                "UIPersistentResetModal", "FetchNewChallenges", "PersistentResetManager", "FetchNewChallenges"));
    }

    [GameAssemblyFact]
    public void Both_native_fetchers_cycle_out_then_materialize_queued_offers()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(0x060004DA, assembly.GetMethodToken("ChallengeManager", "LoadNewActiveChallenges"));
        Assert.Equal(0x06000653, assembly.GetMethodToken("PersistentResetManager", "FetchNewChallenges"));
        Assert.Equal(0x06001631, assembly.GetMethodToken("ChallengeListVariable", "Instantiate"));
        Assert.Equal(0x06001634, assembly.GetMethodToken("ChallengeListVariable", "CycleOut"));
        Assert.Equal(0x06000935, assembly.GetMethodToken("ChallengeSO", "QueueActivation"));

        Assert.True(assembly.MethodReferenceOffset("ChallengeManager", "LoadNewActiveChallenges",
            "ChallengeListVariable", "CycleOut") < assembly.MethodReferenceOffset(
                "ChallengeManager", "LoadNewActiveChallenges", "ChallengeListVariable", "Instantiate"));
        Assert.True(assembly.MethodReferenceOffset("PersistentResetManager", "FetchNewChallenges",
            "ChallengeListVariable", "CycleOut") < assembly.MethodReferenceOffset(
                "PersistentResetManager", "FetchNewChallenges", "ChallengeListVariable", "Instantiate"));
        Assert.True(assembly.MethodReferencesMethod("ChallengeListVariable", "Instantiate",
            "ChallengeSO", "QueueActivation"));
    }

    /// <summary>
    /// A timed challenge's limit is the game's own number, asked for at the game's own level.
    /// </summary>
    /// <remarks>
    /// The suite publishes <c>GetTimeLimit</c>'s answer rather than folding
    /// <c>timeLimit</c> through <c>timeLimitScaling</c> itself, so what has to hold is that the
    /// method still owns that fold, that the condition still says which conditions are timed, and
    /// that <c>ChallengeSO.level</c> is still the level both native callers pass.
    /// </remarks>
    [GameAssemblyFact]
    public void The_timed_conditions_limit_is_the_games_own_fold_at_the_challenges_own_level()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal(new[]
        {
            new KeyValuePair<string, int>("None", 0),
            new KeyValuePair<string, int>("Time", 1),
            new KeyValuePair<string, int>("Prereq", 2),
        }, assembly.GetInt32EnumMembers("ChallengeCondition+BeforeType").ToArray());
        Assert.Equal("ChallengeCondition", assembly.GetFieldType("ChallengeSO", "challengeCondition"));

        // The fold the suite declines to transcribe: the authored struct, the modifier list, and
        // the two calls that combine them at a level.
        References(
            assembly.GetMethodBodyDefinitionReferences("ChallengeCondition", "GetTimeLimit"),
            ("ChallengeCondition", "timeLimit"),
            ("ChallengeCondition", "timeLimitScaling"),
            ("TimeValue", "AsBigDouble"),
            ("ModifierListRef", "MultiplyScalar"),
            ("ValueModifierList", "Adjust"));

        // The race the limit is one side of, and the format the game draws it in — which is the
        // ultra-precise one, the same rule the elapsed variable's own flags select.
        References(
            assembly.GetMethodBodyDefinitionReferences("ChallengeCondition", "HasFailedTime"),
            ("Player", "GetResetTimePassed"),
            ("ChallengeCondition", "GetTimeLimit"));
        References(
            assembly.GetMethodBodyDefinitionReferences("ChallengeCondition", "GetTimeBeforeNodes"),
            ("ChallengeCondition", "GetTimeLimit"),
            ("Utils", "BeautifyTimeUltraPrecise"));

        // Both native callers hand the condition `ChallengeSO.level`, which is the level the world
        // row already publishes — so the suite asks for the limit of the run being attempted.
        References(
            assembly.GetMethodBodyDefinitionReferences("ChallengeSO", "SlowIncrement"),
            ("ChallengeSO", "level"),
            ("ChallengeSO", "challengeCondition"),
            ("ChallengeCondition", "GetResult"));
        References(
            assembly.GetMethodBodyDefinitionReferences("ChallengeSO", "GetTooltipNodes"),
            ("ChallengeSO", "level"),
            ("ChallengeSO", "challengeCondition"),
            ("ChallengeCondition", "GetTooltipNodes"));
    }

    /// <summary>
    /// Asserts one method body reaches each named member. Definition references are matched by
    /// declaring type and name rather than by a unique-method lookup, because two of these targets
    /// — <c>ValueModifierList.Adjust</c> and <c>ChallengeCondition.GetTooltipNodes</c> — are
    /// overloaded or share a name with the caller, and neither ambiguity is what is being pinned.
    /// </summary>
    private static void References(
        IReadOnlyList<MethodBodyDefinitionReference> body,
        params (string Type, string Member)[] expected) =>
        Assert.All(expected, target => Assert.Contains(
            body,
            reference => reference.DeclaringType == target.Type &&
                reference.MemberName == target.Member));

    [Fact]
    public void Manifest_names_every_challenge_action_and_shared_decision_touch()
    {
        var manifest = NativeContractManifest.Load();
        var expected = new[]
        {
            "challenge.challenge.type-action", "challenge.manager.type-action",
            "challenge.reset-manager.type-action", "challenge.list.type-action",
            "challenge.int-variable.type-action", "challenge.bool-variable.type-action",
            "challenge.manager-instance-action", "challenge.reset-manager-instance-action",
            "challenge.manager-preferred-action", "challenge.manager-active-action",
            "challenge.reset-active-action", "challenge.reset-rerolls-left-action",
            "challenge.reset-cycle-complete-action",
            "challenge.reset-fetched-action", "challenge.list-values-action",
            "challenge.list-empty-spot-action",
            "challenge.list-contains-action", "challenge.list-toggle-action",
            "challenge.list-restricted-action", "challenge.challenge-state-action",
            "challenge.challenge-toggle-queue-action", "challenge.challenge-abandon-action",
            "challenge.int-as-int-action", "challenge.int-set-action",
            "challenge.bool-get-action", "challenge.bool-set-action",
            "challenge.manager-fetch-action",
            "challenge.available-to-run-capture", "challenge.completed-once-capture",
            "challenge.maximum-level-capture", "challenge.next-difficulty-capture",
            "challenge.next-reward-capture", "challenge.condition-capture",
            "challenge.condition-before-type-capture", "challenge.condition-time-limit-capture",
        };
        Assert.All(expected, id => Assert.Single(manifest.Contracts, contract => contract.Id == id));
    }
}

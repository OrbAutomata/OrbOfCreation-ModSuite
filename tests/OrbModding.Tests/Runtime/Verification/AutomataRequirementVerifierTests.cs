using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

/// <summary>
/// The pass that asks the game whether the suite's admission verdict is right.
/// </summary>
/// <remarks>
/// <para>
/// What can be checked without the real assemblies is the wiring, and the wiring is where the danger
/// is: this verifier reaches for a method the game has two of, and the other one latches a field. The
/// disagreeing and agreeing paths for entities that actually have conditions are checked in game,
/// because a stand-in built to evaluate them would be a second copy of the arithmetic under test.
/// </para>
/// <para>
/// The one comparison made here is real on both sides: an entity with no conditions is admitted by the
/// suite because it published no rows, and by the game because an empty container passes.
/// </para>
/// </remarks>
public sealed class AutomataRequirementVerifierTests : IDisposable
{
    public AutomataRequirementVerifierTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void AnUnresolvableContractMakesTheVerifierUnavailableRatherThanPassing()
    {
        Assert.False(new AutomataRequirementVerifier(
            typeof(object), RequirementOwnerShape.UpgradeQueuedLevel).IsAvailable);
    }

    /// <summary>
    /// A build that carries only the latching overload is refused. Binding to it would make a
    /// diagnostic stamp a game id and set an availability latch on every entity it looked at.
    /// </summary>
    [Fact]
    public void AShapeWithOnlyTheLatchingOverloadIsRefused()
    {
        Assert.False(new AutomataRequirementVerifier(
            typeof(LatchingOnlyOwner), RequirementOwnerShape.UpgradeQueuedLevel).IsAvailable);
    }

    [Fact]
    public void AnUnavailableVerifierRefusesToVerifyAndSaysWhy()
    {
        var verifier = new AutomataRequirementVerifier(
            typeof(object), RequirementOwnerShape.UpgradeQueuedLevel);
        var run = new DifferentialRun();

        var verified = verifier.TryVerify(new object(), TestWorlds.Empty, run, Session(), out var failure);

        Assert.False(verified);
        Assert.NotEmpty(failure);
        Assert.Equal(0, run.Compared);
        Assert.DoesNotContain("PASSED", run.Summarize(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Both sides say yes, for the same reason, and the pass records having asked. This is also what
    /// proves the verifier found the parameterised overload rather than the latching one, since the
    /// latching one on the stand-in answers from a field nobody set.
    /// </summary>
    [Fact]
    public void AnEntityWithNoConditionsAgreesWithTheGame()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1, level = 2, queuedLevels = 1 };
        global::UpgradeSO.All.Add(upgrade);

        var verifier = new AutomataRequirementVerifier(
            typeof(global::UpgradeSO), RequirementOwnerShape.UpgradeQueuedLevel);
        Assert.True(verifier.IsAvailable);

        var run = new DifferentialRun("Upgrade requirement");
        Assert.True(verifier.TryVerify(upgrade, Collect(), run, Session(), out var failure));

        Assert.Empty(failure);
        Assert.Equal(1, run.Compared);
        Assert.True(run.Passed);
    }

    /// <summary>
    /// A condition the suite cannot evaluate makes the entity unverifiable and names the class. It is
    /// deliberately not a mismatch: the verdict already refuses the purchase, so what needs reporting
    /// is which class nobody has modelled.
    /// </summary>
    [Fact]
    public void AnUnmodelledConditionIsUnverifiableAndNamesItsClass()
    {
        var upgrade = new global::UpgradeSO { maxLevel = -1 };
        global::UpgradeSO.All.Add(upgrade);
        upgrade.prerequisitesPerLevel.prerequisites.Add(new Requirements.OpaqueRequirement());

        var verifier = new AutomataRequirementVerifier(
            typeof(global::UpgradeSO), RequirementOwnerShape.UpgradeQueuedLevel);
        var run = new DifferentialRun("Upgrade requirement");

        Assert.False(verifier.TryVerify(upgrade, Collect(), run, Session(), out var failure));

        Assert.Contains("OpaqueRequirement", failure, StringComparison.Ordinal);
        Assert.Equal(0, run.Compared);
    }

    /// <summary>
    /// A research whose requirement level is negative is skipped rather than compared.
    /// </summary>
    /// <remarks>
    /// <c>MeetsLevelRequirements()</c> answers true outright below zero and never reaches its
    /// container, so there is no answer of the game's own to disagree with. Comparing anyway would
    /// pad the pass with a tautology and hide how much of the registry it really covered.
    /// </remarks>
    [Fact]
    public void AResearchTheGameNeverAsksAboutIsSkippedRatherThanComparedAgainstItself()
    {
        var research = new global::ResearchSO { maxLevel = -1, level = -4 };
        global::ResearchSO.All.Add(research);

        var verifier = new AutomataRequirementVerifier(
            typeof(global::ResearchSO), RequirementOwnerShape.ResearchRequirementLevel);
        Assert.True(verifier.IsAvailable);

        var session = Session();
        var run = session.Run;
        Assert.True(verifier.TryVerify(research, Collect(), run, session, out var failure));
        session.RecordVerified();

        Assert.Empty(failure);
        Assert.Equal(0, run.Compared);
        Assert.Equal(1, session.ExpectedSkips);
        Assert.Equal(0, session.EntitiesVerified);
    }

    /// <summary>
    /// A tiered link is compared once per tier, at level zero, against that tier's own container.
    /// </summary>
    /// <remarks>
    /// One verdict for the whole link would fold tiers the game keeps apart, and would leave the
    /// tier the planner actually consulted unnamed when the two sides disagreed.
    /// </remarks>
    [Fact]
    public void ATieredLinkIsComparedOncePerTier()
    {
        var link = new global::PrerequisiteLinkSO();
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        link.linkTiers.Add(new global::PrerequisiteLinkSO.LinkDefinition());
        global::PrerequisiteLinkSO.All.Add(link);

        var verifier = new AutomataRequirementVerifier(
            typeof(global::PrerequisiteLinkSO), RequirementOwnerShape.PrerequisiteLinkTier);
        Assert.True(verifier.IsAvailable);

        var session = Session();
        var run = session.Run;
        Assert.True(verifier.TryVerify(link, Collect(), run, session, out var failure));

        Assert.Empty(failure);
        Assert.Equal(2, run.Compared);
        Assert.True(run.Passed);
    }

    [Fact]
    public void AnEmptyUsageProgramAgreesWithTheNativeOracle()
    {
        var recipe = new global::AlchemyRecipeSO();
        recipe.usagePrerequisites.available = true;
        global::AlchemyRecipeSO.All.Add(recipe);

        var verifier = new AutomataUsagePrerequisiteVerifier(typeof(global::AlchemyRecipeSO));
        var run = new DifferentialRun("Concept usage prerequisite");

        Assert.True(verifier.IsAvailable);
        Assert.True(verifier.TryVerify(recipe, Collect(), run, out var failure));
        Assert.Empty(failure);
        Assert.Equal(1, run.Compared);
        Assert.True(run.Passed);
        Assert.Equal(1, recipe.usagePrerequisites.CheckCalls);
    }

    [Fact]
    public void AUsageOracleDisagreementIsNamedAsADivergence()
    {
        var recipe = new global::AlchemyRecipeSO();
        global::AlchemyRecipeSO.All.Add(recipe);
        var verifier = new AutomataUsagePrerequisiteVerifier(typeof(global::AlchemyRecipeSO));
        var run = new DifferentialRun("Concept usage prerequisite");

        Assert.True(verifier.TryVerify(recipe, Collect(), run, out var failure));

        Assert.Empty(failure);
        Assert.False(run.Passed);
        Assert.Contains("usage-prerequisites", run.Summarize(), StringComparison.Ordinal);
    }

    private static GameWorldState Collect()
    {
        var collector = new GameWorldCollector();
        collector.Collect();
        return collector.Build();
    }

    private static DifferentialVerificationSession Session()
    {
        var session = new DifferentialVerificationSession(
            "Requirement", tickBudget: 1, entityBudget: int.MaxValue);
        session.Start();
        return session;
    }

    private static void ClearRegistries()
    {
        global::UpgradeSO.All.Clear();
        global::StructureSO.All.Clear();
        global::ResearchSO.All.Clear();
        global::PrerequisiteLinkSO.All.Clear();
        global::GameManager.currentFrame = 0;
        global::AlchemyRecipeSO.All.Clear();
    }

    /// <summary>A build whose container exposes only the overload that writes.</summary>
    private sealed class LatchingOnlyContainer
    {
        public bool Check() => true;
    }

    private sealed class LatchingOnlyOwner
    {
        public LatchingOnlyContainer prerequisitesPerLevel = new();
        public int level = 0;
        public int queuedLevels = 0;

        public Guid GetGuid() => Guid.Empty;
    }
}

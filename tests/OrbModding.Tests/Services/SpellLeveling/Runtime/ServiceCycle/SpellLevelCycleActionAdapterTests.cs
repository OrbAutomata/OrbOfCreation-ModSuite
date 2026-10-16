using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.SpellLeveling.Runtime.ServiceCycle;

/// <summary>
/// End-to-end tests for the Spell Leveling boundary: the real action adapter over the real native
/// adapter over the game stubs. The boundary decides the unpublished leveling prerequisite and
/// revalidates the published readiness and affordability facts immediately before spending.
/// </summary>
public sealed class SpellLevelCycleActionAdapterTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public SpellLevelCycleActionAdapterTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void ASingleLevelCommitsAsAnExactPlusOneDelta()
    {
        var spell = Spell(masteryLevel: 3, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(4, spell.masteryLevel);
        Assert.Equal(1, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void ALockedProgressionRejectsWithItsOwnCodeAndSpendsNothing()
    {
        // The one refusal the planner cannot avoid: prerequisites are not published, so the worker
        // plans optimistically and learns the answer here. Its own code is what lets the feature
        // status say Locked rather than a generic native rejection.
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: false);

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.ProgressionLocked, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void AnUnaffordableLevelRejectsWithoutSpending()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.GetLevelCost().affordable = false;

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void ASpellThatStoppedBeingReadySinceThePlanRejectsWithoutSpending()
    {
        // The snapshot said ready and the game now says otherwise. That is the staleness the boundary
        // exists to absorb, and absorbing it costs one penalty-free rejection.
        var spell = Spell(masteryLevel: 1, ready: false, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, result.Code);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void AnAllSkipsAnUnaffordableCreateSpringAndLevelsAffordableDenseExpansion()
    {
        var water = new global::ResourceSO { name = "Water", quantity = new BigDouble(5, 0) };
        var space = new global::ResourceSO { name = "Space", quantity = new BigDouble(2, 1) };
        var createSpring = Spell(masteryLevel: 24, ready: true, unlocked: true);
        createSpring.levelCost.costs.Add(new global::ResourceTuple(water, new BigDouble(1, 1)));
        var denseExpansion = Spell(masteryLevel: 25, ready: true, unlocked: true);
        denseExpansion.levelCost.costs.Add(new global::ResourceTuple(space, new BigDouble(1, 1)));
        var notReady = Spell(masteryLevel: 2, ready: false, unlocked: true);
        UnlockLevelAll(level: 1);

        var result = Execute(SpellLevelActionKind.All, Id(createSpring));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(1, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(24, createSpring.masteryLevel);
        Assert.Equal(26, denseExpansion.masteryLevel);
        Assert.Equal(2, notReady.masteryLevel);
    }

    [Fact]
    public void AnAllWithNoAffordableLiveSpellRejectsWithoutCallingOrQuarantiningTheBatch()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.levelCost.affordable = false;
        UnlockLevelAll(level: 1);
        var levels = new SpellLevelNativeAdapter();
        var capability = new SpellLevelCapabilityState();
        capability.Observe(AutoSpellLevelCapability.All);
        var boundaryStatus = new SpellLevelBoundaryStatusState();

        var rejected = Execute(
            SpellLevelActionKind.All,
            Id(spell),
            levels: levels,
            observeCapability: capability.Observe,
            observeBoundaryStatus: boundaryStatus.Observe);

        Assert.Equal(ServiceActionDisposition.Rejected, rejected.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, rejected.Code);
        Assert.Equal(0, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(AutoSpellLevelCapability.All, capability.Current);
        Assert.Equal("no ready spell has an affordable level cost", boundaryStatus.WaitingReason);

        spell.levelCost.affordable = true;
        var committed = Execute(
            SpellLevelActionKind.All,
            Id(spell),
            levels: levels,
            observeCapability: capability.Observe,
            observeBoundaryStatus: boundaryStatus.Observe);

        Assert.Equal(ServiceActionDisposition.Committed, committed.Disposition);
        Assert.Equal(1, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(2, spell.masteryLevel);
        Assert.Null(boundaryStatus.WaitingReason);
    }

    [Fact]
    public void AnAllPlannedAgainstAQueuedButUncommittedUpgradeRejectsWithoutCalling()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        UnlockLevelAll(level: 0);
        LevelAllUpgrade().queuedLevels = 1;
        var capability = new SpellLevelCapabilityState();
        capability.Observe(AutoSpellLevelCapability.All);

        var result = Execute(
            SpellLevelActionKind.All,
            Id(spell),
            observeCapability: capability.Observe);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.LevelNotAffordable, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(AutoSpellLevelCapability.Single, capability.Current);
    }

    [Fact]
    public void AnAllWithAnUnavailableExactBindingFailsClosedWithoutCalling()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        UnlockLevelAll(level: 1);
        var manager = global::SpellManager.instance!;
        global::IdScriptableObject.RuntimeLookup.Remove(KnownEntities.UnlockLevelAllSpells.Uuid);

        var result = Execute(SpellLevelActionKind.All, Id(spell));

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.Equal(0, manager.TryLevelAllCalls);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void ALifecycleEpochDriftRejectsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    [Fact]
    public void AnAllLifecycleEpochDriftRejectsWithoutCallingTheBatch()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        UnlockLevelAll(level: 1);

        var result = Execute(SpellLevelActionKind.All, Id(spell), nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(0, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void LosingTheActionFamilyRejectsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), ownsActionFamily: false);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(SpellLevelActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void ADisabledConfigurationRejectsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), autoLevelSpells: false);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void AMutationThatDoesNotMoveTheLevelFaultsAndBlocksFurtherSpending()
    {
        // The audited postcondition. A native call that spends and does not deliver means the suite no
        // longer understands the contract, so it faults and the port refuses to call again until the
        // next lifecycle rather than spending on every cycle forever.
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        var levels = new SpellLevelNativeAdapter();

        var first = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, first.Code);

        spell.SuppressLevelMutation = false;
        var second = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Faulted, second.Disposition);
        Assert.Equal(1, spell.masteryLevel);

        // A lifecycle boundary is the one thing that clears it, which is the whole retained-state rule.
        levels.InvalidateLifecycle();
        var third = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, third.Disposition);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void ALevelTheGameAppliedBeforeThrowingCommitsAndKeepsLeveling()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.ThrowAfterLevelMutation = true;
        var levels = new SpellLevelNativeAdapter();

        var first = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, first.Disposition);
        Assert.Equal(2, spell.masteryLevel);

        spell.ThrowAfterLevelMutation = false;
        spell.readyToLevel = true;
        var second = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, second.Disposition);
        Assert.Equal(3, spell.masteryLevel);
    }

    [Fact]
    public void AnAttemptedAllNoOpFaultsAndBlocksTheBatchUntilLifecycleReplacement()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        spell.levelCost.AffordableLevels = 1;
        UnlockLevelAll(level: 1);
        var levels = new SpellLevelNativeAdapter();

        var first = Execute(SpellLevelActionKind.All, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, first.Code);
        Assert.Equal(1, global::SpellManager.instance!.TryLevelAllCalls);

        spell.SuppressLevelMutation = false;
        spell.levelCost.AffordableLevels = 1;
        var blocked = Execute(SpellLevelActionKind.All, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Faulted, blocked.Disposition);
        Assert.Equal(1, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(1, spell.masteryLevel);

        levels.InvalidateLifecycle();
        var recovered = Execute(SpellLevelActionKind.All, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, recovered.Disposition);
        Assert.Equal(2, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void TheBoundaryChangesCapabilityOnlyForProgressionOrCommittedUpgradeFacts()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: false);
        var observed = new List<AutoSpellLevelCapability>();

        Execute(SpellLevelActionKind.Single, Id(spell), observeCapability: observed.Add);
        Assert.Equal(AutoSpellLevelCapability.Locked, Assert.Single(observed));

        observed.Clear();
        spell.levelingPrerequisites.available = true;
        Execute(SpellLevelActionKind.Single, Id(spell), observeCapability: observed.Add);
        Assert.DoesNotContain(observed, _ => true);
    }

    [Fact]
    public void TheCapabilityProbeReadsTheGameRatherThanTheSnapshot()
    {
        var levels = new SpellLevelNativeAdapter();
        var spell = Spell(masteryLevel: 1, ready: false, unlocked: false);

        Assert.True(levels.TryReadCapability(out var locked));
        Assert.Equal(AutoSpellLevelCapability.Locked, locked);

        // Readiness is not what unlocks the feature — the prerequisite is.
        spell.levelingPrerequisites.available = true;
        Assert.True(levels.TryReadCapability(out var single));
        Assert.Equal(AutoSpellLevelCapability.Single, single);

        UnlockLevelAll(level: 1);
        Assert.True(levels.TryReadCapability(out var all));
        Assert.Equal(AutoSpellLevelCapability.All, all);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static Guid Id(global::SpellRecipeSO spell) => Guid.Parse(spell.uuid);

    private static global::SpellRecipeSO Spell(int masteryLevel, bool ready, bool unlocked)
    {
        var spell = new global::SpellRecipeSO
        {
            uuid = Guid.NewGuid().ToString(),
            masteryLevel = masteryLevel,
            discovered = true,
            readyToLevel = ready,
        };
        spell.levelingPrerequisites.available = unlocked;
        global::SpellRecipeSO.All.Add(spell);
        global::SpellManager.instance!.availableSpellRecipes.value.Add(spell);
        return spell;
    }

    /// <summary>
    /// Sets the committed level of the level-all upgrade. The upgrade itself always exists: the native
    /// port resolves it during binding, so a build without it has no spell-level contract at all —
    /// which is a different failure from "the player has not bought it yet".
    /// </summary>
    private static void UnlockLevelAll(int level) => LevelAllUpgrade().level = level;

    private static global::UpgradeSO LevelAllUpgrade() =>
        (global::UpgradeSO)global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid];

    private static ServiceActionResult Execute(
        SpellLevelActionKind kind,
        Guid uuid,
        long nativeEpoch = PlannedEpoch,
        bool ownsActionFamily = true,
        bool autoLevelSpells = true,
        SpellLevelNativeAdapter? levels = null,
        Action<AutoSpellLevelCapability>? observeCapability = null,
        Action<SpellLevelSubmission>? observeBoundaryStatus = null)
    {
        var adapter = new SpellLevelCycleActionAdapter(
            levels ?? new SpellLevelNativeAdapter(),
            () => nativeEpoch,
            () => ownsActionFamily,
            observeCapability,
            observeBoundaryStatus);
        var action = new SpellLevelCycleAction(kind, uuid, PlannedEpoch);
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = false },
            AutoBuy = new AutoBuyConfiguration
            {
                Mode = AutoBuyOperationMode.Active,
                AutoLevelSpells = autoLevelSpells,
            },
        };
        return adapter.TryExecute(in action, in config, default);
    }

    private static void ResetNativeState()
    {
        global::IdScriptableObject.RuntimeLookup.Clear();
        global::SpellRecipeSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::SpellManager.instance = new global::SpellManager();

        // The upgrade has to be reachable the way production reaches it, which is the typed registry
        // rather than the category's All list. Seeding only the list leaves the port unable to bind.
        var levelAll = new global::UpgradeSO
        {
            uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D"),
            level = 0,
            available = true,
        };
        global::UpgradeSO.All.Add(levelAll);
        global::IdScriptableObject.RuntimeLookup.Add(KnownEntities.UnlockLevelAllSpells.Uuid, levelAll);
    }
}

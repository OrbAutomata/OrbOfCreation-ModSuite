using OrbModding.TestSupport;
using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.SpellLeveling.Runtime.ServiceCycle;

/// <summary>
/// End-to-end tests for the Spell Leveling boundary: the real action adapter over the native
/// GameAction over the game stubs. The boundary decides the unpublished leveling prerequisite and
/// revalidates the published readiness and affordability facts immediately before spending.
/// </summary>
[Collection(GameActionFaultLogCollection.Name)]
public sealed class SpellLevelCycleActionAdapterTests : IDisposable
{
    private const long PlannedEpoch = 7;

    public SpellLevelCycleActionAdapterTests() => ResetNativeState();

    public void Dispose()
    {
        ResetNativeState();
        GameActionFaultLog.ConfigureLog(null);
    }

    [Fact]
    public void ASingleLevelCommitsWhenTheLevelRises()
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
    public void ALevelThatRisesByMoreThanOneStillCommits()
    {
        var spell = Spell(masteryLevel: 3, ready: true, unlocked: true);
        spell.ExtraLevelsPerPurchase = 1;

        var result = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(5, spell.masteryLevel);
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
    public void AnAllWithNoAffordableLiveSpellRejectsWithoutCallingTheBatch()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.levelCost.affordable = false;
        UnlockLevelAll(level: 1);
        var levels = Levels();
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
        Assert.Equal(SpellLevelActionResultCodes.LevelAllLocked, result.Code);
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
    public void AnUnreadableLifecycleEpochFaultsWithoutTouchingTheGame()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);

        var result = Execute(SpellLevelActionKind.Single, Id(spell), readEpoch: static () => throw new InvalidOperationException("The lifecycle epoch is unreadable."));

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
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
    public void APaidLevelThatDoesNotRiseFaultsAndBacksItsSpellOffWithoutBlockingTheNextPress()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        var levels = Levels();

        var first = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        spell.SuppressLevelMutation = false;
        var second = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, first.Code);
        Assert.True(first.BacksOffTarget);
        Assert.Equal(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)), first.TargetInitialBackoff);
        Assert.Equal(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(30)), first.TargetBackoffCap);
        Assert.Equal(ServiceActionDisposition.Committed, second.Disposition);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void ALevelTheGameAppliedBeforeThrowingCommitsAndKeepsLeveling()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.ThrowAfterLevelMutation = new InvalidOperationException("injected failure after the level landed");
        var levels = Levels();

        var first = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, first.Disposition);
        Assert.Equal(2, spell.masteryLevel);

        spell.ThrowAfterLevelMutation = null;
        spell.readyToLevel = true;
        var second = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, second.Disposition);
        Assert.Equal(3, spell.masteryLevel);
    }

    /// <summary>
    /// A level the game applied before an unexpected throw is the level the player paid for: it
    /// commits without blocking, and the throw goes to the suite log under a reference.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowAfterTheLevelLandsCommitsAndIsLoggedWithAReference()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.ThrowAfterLevelMutation = new NullReferenceException("injected defect after the level landed");
        var levels = Levels();

        var first = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Committed, first.Disposition);
        Assert.Equal(2, spell.masteryLevel);
        var line = Assert.Single(logged);
        Assert.Matches("MCP-[0-9A-F]{8}", line);
        Assert.Contains(typeof(NullReferenceException).FullName!, line);
        Assert.Contains("injected defect after the level landed", line);

        spell.ThrowAfterLevelMutation = null;
        spell.readyToLevel = true;
        var second = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        Assert.Equal(ServiceActionDisposition.Committed, second.Disposition);
        Assert.Equal(3, spell.masteryLevel);
    }

    /// <summary>
    /// A throw outside the expected native failures after the cost was paid is a post-payment fault:
    /// the exception goes whole to the suite log, the reason names its reference, and the next press
    /// is not blocked.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowAfterPaymentWithNoLevelFaultsUnderItsLogReference()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        spell.ThrowAfterLevelMutation = new NullReferenceException("injected failure before the level landed");
        var levels = Levels();

        var faulted = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        spell.SuppressLevelMutation = false;
        spell.ThrowAfterLevelMutation = null;
        var committed = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, faulted.Disposition);
        Assert.True(faulted.BacksOffTarget);
        Assert.Equal(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)), faulted.TargetInitialBackoff);
        Assert.DoesNotContain("injected failure", faulted.Reason);
        var entry = Assert.Single(logged);
        Assert.Contains("System.NullReferenceException: injected failure before the level landed", entry);
        Assert.Contains(" Reference " + entry.Split(' ')[3] + " is in the suite log.", faulted.Reason);
        Assert.Equal(ServiceActionDisposition.Committed, committed.Disposition);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void AThrowAfterPaymentWithNoLevelRepeatingUnderAFreshReferenceIsWarnedAboutOnce()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var warnings = new List<string>();
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        spell.ThrowAfterLevelMutation = new NullReferenceException("injected failure before the level landed");
        var adapter = new SpellLevelCycleActionAdapter(
            Levels(), warnings.Add);

        var first = Execute(adapter, SpellLevelActionKind.Single, Id(spell));
        var repeated = Execute(adapter, SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(ServiceActionDisposition.Faulted, repeated.Disposition);
        Assert.Equal(2, logged.Count);
        Assert.NotEqual(first.Reason, repeated.Reason);
        var warning = Assert.Single(warnings);
        Assert.EndsWith(first.Reason, warning);
    }

    [Fact]
    public void ARegistryReadThatKeepsThrowingUnderAFreshReferenceIsWarnedAboutOnce()
    {
        const string cause = "injected registry outage";
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var warnings = new List<string>();
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var registry = TestRegistryResolver.For(
            () => 1,
            () => throw new InvalidOperationException(cause),
            _ => Guid.Empty);
        var adapter = new SpellLevelCycleActionAdapter(
            Levels(registry), warnings.Add);

        var first = Execute(adapter, SpellLevelActionKind.Single, Id(spell));
        var repeated = Execute(adapter, SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(ServiceActionDisposition.Faulted, repeated.Disposition);
        Assert.Equal(2, logged.Count);
        Assert.NotEqual(first.Reason, repeated.Reason);
        Assert.DoesNotContain(cause, first.Reason);
        Assert.Single(warnings);
        Assert.Equal(1, spell.masteryLevel);
    }

    [Fact]
    public void AnExpectedThrowAfterPaymentWithNoLevelFaultsAfterTheGameThrewUnderItsLogReference()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        spell.ThrowAfterLevelMutation = new InvalidOperationException("injected expected failure after payment");

        var faulted = Execute(SpellLevelActionKind.Single, Id(spell));

        Assert.Equal(ServiceActionDisposition.Faulted, faulted.Disposition);
        Assert.Equal(MonotonicDuration.FromTimeSpan(TimeSpan.FromMinutes(1)), faulted.TargetInitialBackoff);
        var entry = Assert.Single(logged);
        Assert.Contains("System.InvalidOperationException: injected expected failure after payment", entry);
        Assert.Equal(
            $"Spell Leveling paid for a level of {EntityIdentityFormatter.PlayerName(Id(spell))} and the level did not rise " +
            "after the game threw. Reference " + entry.Split(' ')[3] + " is in the suite log.",
            faulted.Reason);
    }

    [Fact]
    public void AReadThatFailsBeforeTheBatchPressFaultsUnderItsLogReferenceWithNothingPaid()
    {
        var logged = new List<string>();
        GameActionFaultLog.ConfigureLog(logged.Add);
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        UnlockLevelAll(level: 1);
        spell.levelCost.WhileAffordabilityIsRead =
            () => global::SpellManager.instance!.availableSpellRecipes.value = null!;

        var faulted = Execute(SpellLevelActionKind.All, Id(spell));

        Assert.Equal(ServiceActionDisposition.Faulted, faulted.Disposition);
        Assert.Null(faulted.TargetInitialBackoff);
        Assert.Equal(0, global::SpellManager.instance!.TryLevelAllCalls);
        var entry = Assert.Single(logged);
        Assert.Contains("available spell recipe contents are unavailable", entry);
        Assert.Equal(
            "spell-level read failed before the press. Reference " + entry.Split(' ')[3] + " is in the suite log.",
            faulted.Reason);
    }

    [Fact]
    public void AnAllThatLevelsNothingFaultsAndBacksOffWithoutBlockingTheNextPress()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        spell.levelCost.AffordableLevels = 1;
        UnlockLevelAll(level: 1);
        var levels = Levels();

        var first = Execute(SpellLevelActionKind.All, Id(spell), levels: levels);
        spell.SuppressLevelMutation = false;
        spell.levelCost.AffordableLevels = 1;
        var second = Execute(SpellLevelActionKind.All, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.True(first.BacksOffTarget);
        Assert.Equal(ServiceActionDisposition.Committed, second.Disposition);
        Assert.Equal(2, global::SpellManager.instance!.TryLevelAllCalls);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void AnUnreadableRecipeListFaultsAndTheNextPressReadsAgain()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var recipes = global::SpellManager.instance!.availableSpellRecipes.value;
        global::SpellManager.instance.availableSpellRecipes.value = null!;
        var levels = Levels();

        var faulted = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        global::SpellManager.instance.availableSpellRecipes.value = recipes;
        var committed = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, faulted.Disposition);
        Assert.Equal(ServiceActionDisposition.Committed, committed.Disposition);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void ARegistryTypeMismatchCanRecoverOnTheNextAttempt()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var levelAll = global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid];
        global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid] =
            new global::StructureSO { uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D") };
        var levels = Levels();

        var mismatched = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid] = levelAll;
        var rebound = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, mismatched.Disposition);
        Assert.Equal(ServiceActionDisposition.Committed, rebound.Disposition);
        Assert.Equal(1, spell.levelCost.PerformCalls);
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
        var levels = Levels();
        var spell = Spell(masteryLevel: 1, ready: false, unlocked: false);

        Assert.True(levels.TryReadCapability(out var locked, out _, out _));
        Assert.Equal(AutoSpellLevelCapability.Locked, locked);

        // Readiness is not what unlocks the feature — the prerequisite is.
        spell.levelingPrerequisites.available = true;
        Assert.True(levels.TryReadCapability(out var single, out _, out _));
        Assert.Equal(AutoSpellLevelCapability.Single, single);

        UnlockLevelAll(level: 1);
        Assert.True(levels.TryReadCapability(out var all, out _, out _));
        Assert.Equal(AutoSpellLevelCapability.All, all);
    }

    [Fact]
    public void Every_missing_member_disables_the_complete_spell_level_binding_set()
    {
        foreach (var missing in SpellLevelNativeBindings.ContractIds)
        {
            ResetNativeState();
            var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
            var levels = Levels(null, null, id => id != missing);

            var result = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

            Assert.True(
                result.Disposition == ServiceActionDisposition.Faulted &&
                result.Code == CommonActionResultCodes.AdapterFault &&
                levels.BindingFailure.Contains(missing, StringComparison.Ordinal),
                missing + " did not disable the set: " + result.Reason);
            Assert.Equal(1, spell.masteryLevel);
            Assert.Equal(0, spell.GetLevelCost().PerformCalls);
            Assert.False(levels.TryReadCapability(out _, out _, out _));
        }
    }

    /// <summary>
    /// The price a level was charged is only told to a strategist, so a shipped build levels a spell
    /// without binding the cost-entry members that price is read from.
    /// </summary>
    [Fact]
    public void A_shipped_build_levels_a_spell_without_the_charged_price_members()
    {
        var chargeOnly = new HashSet<string>(StringComparer.Ordinal)
        {
            "generic-level.tuple.type-action",
            "generic-level.resource.type-action",
            "generic-level.cost-entries-action",
            "generic-level.tuple-resource-action",
            "generic-level.tuple-value-action",
            "generic-level.resource-guid-action",
        };
        var spell = Spell(masteryLevel: 3, ready: true, unlocked: true);
        var levels = Levels(null, null, id => !chargeOnly.Contains(id));

        var result = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, spell.masteryLevel);
    }

    [Fact]
    public void AnUnexpectedGameThrowWhilePreflightingALevelEscapesWithoutSpending()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.DiscoveredReadFailure = new NullReferenceException("injected discovery read failure");
        var levels = Levels();

        var action = new SpellLevelCycleAction(SpellLevelActionKind.Single, Id(spell), PlannedEpoch);
        Assert.Throws<NullReferenceException>(() => levels.Submit(in action));
        Assert.Equal(1, spell.masteryLevel);
        Assert.Equal(0, spell.GetLevelCost().PerformCalls);
    }

    /// <summary>
    /// A registry read the game threw on while the boundary bound proves nothing about the build: the
    /// press faults without the exception's text, and the next one binds and levels.
    /// </summary>
    [Fact]
    public void ARegistryReadThatThrowsWhileBindingIsRetriedRatherThanBlockingTheLifecycle()
    {
        const string cause = "injected registry outage";
        var outage = true;
        var spell = Spell(masteryLevel: 3, ready: true, unlocked: true);
        var levels = Levels(TestRegistryResolver.For(
            () => 1,
            () => outage
                ? throw new InvalidOperationException(cause)
                : TypedRegistrySourceSnapshot.Ready(global::IdScriptableObject.RuntimeLookup),
            static value => ((global::IdScriptableObject)value).GetGuid()));

        var failed = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        outage = false;
        var retried = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, failed.Disposition);
        Assert.Equal(ServiceActionDisposition.Committed, retried.Disposition);
        Assert.Equal(4, spell.masteryLevel);
        Assert.DoesNotContain(cause, failed.Reason);
    }

    [Fact]
    public void TheComposedServiceUsesTheRegisteredActionAcrossLifecycleReplacementAndDisposal()
    {
        var first = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var epoch = PlannedEpoch;
        using var registry = new LifecycleGameActions();
        var composition = Compose(registry, () => epoch, static _ => { });
        var config = Configuration();
        var firstAction = new SpellLevelCycleAction(SpellLevelActionKind.Single, Id(first), epoch);

        Assert.Same(composition.Natives, registry.Find<SpellLevelGameAction>());
        Assert.Equal(ServiceActionDisposition.Committed,
            composition.Definition.TryExecute(in firstAction, in config, default).Disposition);

        // A new save has new native instances even when it reuses the stable spell identity.
        var replacement = new global::SpellRecipeSO
        {
            uuid = first.uuid,
            masteryLevel = 8,
            discovered = true,
            readyToLevel = true,
        };
        replacement.levelingPrerequisites.available = true;
        global::SpellManager.instance = new global::SpellManager();
        global::SpellManager.instance.availableSpellRecipes.value.Add(replacement);
        epoch++;
        registry.InvalidateLifecycle();
        var nextAction = new SpellLevelCycleAction(SpellLevelActionKind.Single, Id(replacement), epoch);

        Assert.True(composition.Natives.TryReadCapability(out var capability, out _, out _));
        Assert.Equal(AutoSpellLevelCapability.Single, capability);
        Assert.Equal(ServiceActionDisposition.Committed,
            composition.Definition.TryExecute(in nextAction, in config, default).Disposition);
        Assert.Equal(2, first.masteryLevel);
        Assert.Equal(9, replacement.masteryLevel);

        registry.Dispose();
        replacement.readyToLevel = true;
        var disposed = composition.Definition.TryExecute(in nextAction, in config, default);

        Assert.Equal(ServiceActionDisposition.Faulted, disposed.Disposition);
        Assert.Equal(1, replacement.levelCost.PerformCalls);
        Assert.False(composition.Natives.TryReadCapability(out _, out _, out _));
    }

    [Fact]
    public void AComposedPaidFailureUsesTheInjectedWarningCallback()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.SuppressLevelMutation = true;
        var warnings = new List<string>();
        using var registry = new LifecycleGameActions();
        var composition = Compose(registry, () => PlannedEpoch, warnings.Add);
        var config = Configuration();
        var action = new SpellLevelCycleAction(SpellLevelActionKind.Single, Id(spell), PlannedEpoch);

        var result = composition.Definition.TryExecute(in action, in config, default);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.True(result.BacksOffTarget);
        Assert.EndsWith(result.Reason, Assert.Single(warnings));
        Assert.Equal(1, spell.levelCost.PerformCalls);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static SpellLevelServiceAdapterComposition Compose(
        LifecycleGameActions registry, Func<long> epoch, Action<string> warn)
    {
        var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(), new FeatureStatusSnapshot(
            new FeatureStatusKey(PluginIds.SuiteGuid, SpellLevelFeatureContribution.FeatureId),
            "Spell Leveling", true, FeatureStatusState.NotReady,
            new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "not ready"), PlannedEpoch));
        return SpellLevelServiceAdapterComposition.Create(
            new SpellLevelFeatureDependencies(epoch, static () => true,
                new SpellLevelCapabilityState(), status, warn), registry);
    }

    private static SpellLevelGameAction Levels(
        TypedRegistryResolver? registry = null,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null,
        Func<long>? readEpoch = null,
        bool ownsActionFamily = true) =>
        new(readEpoch ?? (() => PlannedEpoch), () => ownsActionFamily,
            static () => "the spell-level action family is unavailable", registry, resolveType, includeContract);

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
    /// action resolves it before admission, so a build without it has no spell-level contract at all —
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
        SpellLevelGameAction? levels = null,
        Action<AutoSpellLevelCapability>? observeCapability = null,
        Action<SpellLevelSubmission>? observeBoundaryStatus = null,
        Func<long>? readEpoch = null)
    {
        var adapter = new SpellLevelCycleActionAdapter(
            levels ?? Levels(readEpoch: readEpoch ?? (() => nativeEpoch), ownsActionFamily: ownsActionFamily),
            static _ => { },
            observeCapability,
            observeBoundaryStatus);
        return Execute(adapter, kind, uuid, autoLevelSpells);
    }

    private static ServiceActionResult Execute(
        SpellLevelCycleActionAdapter adapter,
        SpellLevelActionKind kind,
        Guid uuid,
        bool autoLevelSpells = true)
    {
        var action = new SpellLevelCycleAction(kind, uuid, PlannedEpoch);
        var config = Configuration(autoLevelSpells);
        return adapter.TryExecute(in action, in config, default);
    }

    private static SuiteRuntimeConfiguration Configuration(bool autoLevelSpells = true) => new()
    {
        General = new SuiteGeneralConfiguration { Enabled = true },
        Safety = new SuiteSafetyConfiguration { EmergencyDisable = false },
        AutoBuy = new AutoBuyConfiguration
        {
            Mode = AutoBuyOperationMode.Active,
            AutoLevelSpells = autoLevelSpells,
        },
    };

    private static void ResetNativeState()
    {
        OrbModding.GameStubs.GameStubState.Reset();
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

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
/// End-to-end tests for the Spell Leveling boundary: the real action adapter over the real native
/// adapter over the game stubs. The boundary decides the unpublished leveling prerequisite and
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
        var levels = new SpellLevelNativeAdapter();

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
        var levels = new SpellLevelNativeAdapter();

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
        var levels = new SpellLevelNativeAdapter();

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
        var levels = new SpellLevelNativeAdapter();

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
            new SpellLevelNativeAdapter(), () => PlannedEpoch, () => true, warnings.Add);

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
        var registry = new TypedRegistryResolver(
            () => 1,
            () => throw new InvalidOperationException(cause),
            _ => Guid.Empty);
        var adapter = new SpellLevelCycleActionAdapter(
            new SpellLevelNativeAdapter(registry), () => PlannedEpoch, () => true, warnings.Add);

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
        var levels = new SpellLevelNativeAdapter();

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
    public void AnInitialisationThrowFaultsAndTheNextPressBindsAgain()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var recipes = global::SpellManager.instance!.availableSpellRecipes.value;
        global::SpellManager.instance.availableSpellRecipes.value = null!;
        var levels = new SpellLevelNativeAdapter();

        var faulted = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        global::SpellManager.instance.availableSpellRecipes.value = recipes;
        var committed = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, faulted.Disposition);
        Assert.Equal(ServiceActionDisposition.Committed, committed.Disposition);
        Assert.Equal(2, spell.masteryLevel);
    }

    [Fact]
    public void AProvenContractMismatchStaysBlockedUntilTheLifecycleIsReplaced()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        var levelAll = global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid];
        global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid] =
            new global::StructureSO { uuid = KnownEntities.UnlockLevelAllSpells.Uuid.ToString("D") };
        var levels = new SpellLevelNativeAdapter();

        var mismatched = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        global::IdScriptableObject.RuntimeLookup[KnownEntities.UnlockLevelAllSpells.Uuid] = levelAll;
        var blocked = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);
        levels.InvalidateLifecycle();
        var rebound = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Faulted, mismatched.Disposition);
        Assert.Equal(ServiceActionDisposition.Faulted, blocked.Disposition);
        Assert.Equal(mismatched.Reason, blocked.Reason);
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
        var levels = new SpellLevelNativeAdapter();
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
            var levels = new SpellLevelNativeAdapter(null, null, id => id != missing);

            var result = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

            Assert.True(
                result.Disposition == ServiceActionDisposition.Faulted &&
                result.Code == CommonActionResultCodes.AdapterFault &&
                result.Reason.Contains(missing, StringComparison.Ordinal),
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
        var levels = new SpellLevelNativeAdapter(null, null, id => !chargeOnly.Contains(id));

        var result = Execute(SpellLevelActionKind.Single, Id(spell), levels: levels);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(4, spell.masteryLevel);
    }

    [Fact]
    public void AnUnexpectedGameThrowWhilePreflightingALevelEscapesWithoutSpending()
    {
        var spell = Spell(masteryLevel: 1, ready: true, unlocked: true);
        spell.DiscoveredReadFailure = new NullReferenceException("injected discovery read failure");
        var levels = new SpellLevelNativeAdapter();

        Assert.Throws<NullReferenceException>(() => levels.Submit(SpellLevelActionKind.Single, Id(spell)));
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
        var levels = new SpellLevelNativeAdapter(new TypedRegistryResolver(
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
        Action<SpellLevelSubmission>? observeBoundaryStatus = null,
        Func<long>? readEpoch = null)
    {
        var adapter = new SpellLevelCycleActionAdapter(
            levels ?? new SpellLevelNativeAdapter(),
            readEpoch ?? (() => nativeEpoch),
            () => ownsActionFamily,
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

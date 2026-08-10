using System;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Runtime.ServiceCycle;

/// <summary>
/// End-to-end tests for the Auto Cast boundary: the real action adapter over the real native adapter
/// over the game stubs. What the worker cannot see — whether a target request is already open, whether
/// the caster is free, whether the slot still holds the spell that was planned — is decided here, so
/// this is where those rules are pinned.
/// </summary>
public sealed class AutoCastCycleActionAdapterTests : IDisposable
{
    private const long PlannedEpoch = 7;

    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Frost = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public AutoCastCycleActionAdapterTests() => ResetNativeState();

    public void Dispose() => ResetNativeState();

    [Fact]
    public void ACastCommitsAsAnExactOneFireDelta()
    {
        var spell = Equip(Ember);

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.True(result.HasNativeEvidence);
        Assert.Equal(NativeMutationOutcome.Verified, result.NativeEvidence.Outcome);
        Assert.Equal(1, spell.FireCalls);
    }

    [Fact]
    public void TheServicesOwnCastIsNotCountedAsThePlayers()
    {
        // The manual-fire counter is what arms the pause. A service that tripped its own pause would
        // stand down for a second after every cast it made.
        Equip(Ember);
        var manualBefore = AutoCastManualSignal.ManualFireEpoch;

        Assert.Equal(ServiceActionDisposition.Committed, Execute(Fire(0, Ember)).Disposition);
        Assert.Equal(manualBefore, AutoCastManualSignal.ManualFireEpoch);
    }

    [Fact]
    public void AnOpenTargetRequestRefusesBeforeAnythingIsSubmitted()
    {
        // Submitting a cast into a request somebody else opened is how a spell lands somewhere nobody
        // asked for, so this is checked before every other term.
        var spell = Equip(Ember);
        global::TargetingManager.OpenRequests = 1;

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.TargetingInProgress, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ABusyCasterIsAnExactPenaltyFreeSkipWithoutFiring()
    {
        var spell = Equip(Ember);
        global::SpellManager.NativeCanCast = false;

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Skipped, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.NativeCasterBusy, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ASpellTheGameNoLongerCallsReadyRefusesWithoutFiring()
    {
        // The snapshot said ready and the game now says otherwise. That is the staleness the boundary
        // exists to absorb, and absorbing it costs one penalty-free skip.
        var spell = Equip(Ember);
        spell.NativeCanCast = false;

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Skipped, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.SpellNotReady, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ARearrangedLoadoutRefusesRatherThanCastingWhateverIsInThePosition()
    {
        var frost = Equip(Frost);

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.SlotIdentityChanged, result.Code);
        Assert.Equal(0, frost.FireCalls);
    }

    /// <summary>
    /// The refusal names what took the slot, because that is the fact a caller replans against.
    /// </summary>
    /// <remarks>
    /// "The planned spell identity changed" leaves a caller with nothing to do but read the loadout
    /// again; naming the occupant is the whole difference between a refusal and an answer.
    /// </remarks>
    [Fact]
    public void ARearrangedLoadoutNamesTheSpellThatTookTheSlot()
    {
        Equip(Frost);

        var submission = new AutoCastNativeAdapter().Fire(0, Ember, holdFullCharge: false);

        Assert.Equal(AutoCastPreflight.SlotIdentityChanged, submission.Preflight);
        Assert.Contains("slot 1", submission.Reason);
        Assert.Contains(Frost.ToString("D"), submission.Reason);
    }

    [Fact]
    public void APositionThatIsNoLongerEquippedRefuses()
    {
        Equip(Ember);

        var result = Execute(Fire(3, Ember));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.SlotIdentityChanged, result.Code);
    }

    [Fact]
    public void APlanThatCouldNotNameItsSpellIsRefusedRatherThanTreatedAsAWildcard()
    {
        var spell = Equip(Ember);

        var result = Execute(Fire(0, Guid.Empty));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.SlotIdentityChanged, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ALifecycleEpochDriftRefusesWithoutTouchingTheGame()
    {
        var spell = Equip(Ember);

        var result = Execute(Fire(0, Ember), nativeEpoch: PlannedEpoch + 1);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.LifecycleReplaced, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void LosingTheActionFamilyRefusesWithoutTouchingTheGame()
    {
        var spell = Equip(Ember);

        var result = Execute(Fire(0, Ember), ownsActionFamily: false);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.ActionFamilyUnavailable, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ADisabledConfigurationRefusesWithoutTouchingTheGame()
    {
        var spell = Equip(Ember);

        var result = Execute(Fire(0, Ember), mode: AutoCastOperationMode.Disabled);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(CommonActionResultCodes.ServiceDisabled, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ACastPlannedJustBeforeAManualOneDoesNotLandJustAfterIt()
    {
        var spell = Equip(Ember);
        var pause = new AutoCastManualPauseState();
        AutoCastManualSignal.NotifySpellFire();

        var result = Execute(Fire(0, Ember), manualPause: pause);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.ManualPause, result.Code);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void AManualPauseNeverHoldsOntoACharge()
    {
        // Standing down is a reason to stop starting casts, not a reason to keep a charge input
        // pressed down on the player's behalf.
        var spell = Equip(Ember);
        spell.SetChargeInput("test", true);
        var pause = new AutoCastManualPauseState();
        AutoCastManualSignal.NotifySpellFire();

        var result = Execute(Release(0, Ember), manualPause: pause);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.False(spell.HoldingCharge);
    }

    [Fact]
    public void AFullChargeHoldIsTakenBeforeTheCastAndLeftHeldWhenItCommits()
    {
        var spell = Equip(Ember);

        var result = Execute(Fire(0, Ember, chargeable: true), fullCharge: true);

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.True(spell.HoldingCharge);
        Assert.Equal(1, spell.FireCalls);
    }

    [Fact]
    public void AHoldTakenForACastThatNeverLandedIsLetGoAgain()
    {
        // A charge input stuck down with nothing holding it is worse than a missed cast.
        var spell = Equip(Ember);
        spell.EmitFireSignal = false;

        var result = Execute(Fire(0, Ember, chargeable: true), fullCharge: true);

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.False(spell.HoldingCharge);
    }

    /// <summary>
    /// "This spell has no charged cast" is a claim about the spell, so the boundary only makes it
    /// once the position resolved to the spell the caller named — and it asks the live game, not a
    /// published loadout a rearrangement can have outrun.
    /// </summary>
    [Fact]
    public void AHoldOnASpellTheGameDoesNotChargeIsRefusedAsTheSpellFactItIs()
    {
        var spell = Equip(Ember);
        spell.NativeCanCharge = false;

        var result = Execute(Fire(0, Ember, chargeable: true), fullCharge: true);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.SpellNotChargeable, result.Code);
        Assert.Equal(0, spell.FireCalls);
        Assert.False(spell.HoldingCharge);
    }

    /// <summary>
    /// A slot that moved, emptied, or fell off the bar mid-cadence answers as the position fact it
    /// is, in its own words. Told instead that the spell cannot be charged, a caller keeps a false
    /// belief about a capability long after the slot it was really about has been fixed.
    /// </summary>
    [Theory]
    [InlineData("moved", 0, "now holds")]
    [InlineData("absent", 3, "is not on the bar")]
    [InlineData("empty", 0, "is empty")]
    public void AChargedFireOnASlotThatMovedSaysSoRatherThanBlamingTheSpell(
        string state,
        int slotIndex,
        string expected)
    {
        var occupant = Equip(state == "moved" ? Frost : Ember);
        if (state == "empty") occupant.NativeEmpty = true;

        var result = Execute(Fire(slotIndex, Ember, chargeable: true), fullCharge: true);
        var submission = new AutoCastNativeAdapter().Fire(slotIndex, Ember, holdFullCharge: true);

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.SlotIdentityChanged, result.Code);
        Assert.Equal(AutoCastPreflight.SlotIdentityChanged, submission.Preflight);
        Assert.Contains(expected, submission.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("charge", submission.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, occupant.FireCalls);
    }

    [Fact]
    public void AReleaseLetsGoWithoutAskingWhetherTheSpellIsStillCharging()
    {
        // Letting go is idempotent and always safe. Refusing because a stale reading disagreed is how
        // an input gets stuck down with nobody tracking it.
        var spell = Equip(Ember);
        spell.SetChargeInput("test", true);

        var result = Execute(Release(0, Ember));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.False(spell.HoldingCharge);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void AnActiveToggleUsesTheSameFireRouteAsTheUiAndCommitsWhenItStopsCasting()
    {
        var spell = Equip(Ember);
        spell.ToggledSpell = true;
        spell.NativeCasting = true;

        var result = Execute(ToggleOff(0, Ember));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(CommonActionResultCodes.Committed, result.Code);
        Assert.Equal(1, spell.FireCalls);
        Assert.False(spell.NativeCasting);
    }

    [Theory]
    [InlineData(false, true, 3080)]
    [InlineData(true, false, 3081)]
    public void ToggleOffRefusesWhenTheSlotIsNotAnActiveToggle(
        bool toggleable,
        bool casting,
        int expectedCode)
    {
        var spell = Equip(Ember);
        spell.ToggledSpell = toggleable;
        spell.NativeCasting = casting;

        var result = Execute(ToggleOff(0, Ember));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(expectedCode, result.Code.Value);
        Assert.Equal(0, spell.FireCalls);
    }

    [Fact]
    public void ToggleOffRefusesWhenThePlayersCancellationSettingDisablesTheUiPath()
    {
        var spell = Equip(Ember);
        spell.ToggledSpell = true;
        spell.NativeCasting = true;
        global::SettingsManager.CancellableSpells = false;

        var result = Execute(ToggleOff(0, Ember));

        Assert.Equal(ServiceActionDisposition.Rejected, result.Disposition);
        Assert.Equal(AutoCastActionResultCodes.CancellationDisabled, result.Code);
        Assert.Equal(0, spell.FireCalls);
        Assert.True(spell.NativeCasting);
    }

    [Fact]
    public void ToggleOffFaultsAndBlocksWhenTheNativeFireRouteLeavesTheToggleActive()
    {
        var spell = Equip(Ember);
        spell.ToggledSpell = true;
        spell.NativeCasting = true;
        spell.SuppressToggleOff = true;
        var natives = new AutoCastNativeAdapter();

        var first = Execute(ToggleOff(0, Ember), natives: natives);
        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(1, spell.FireCalls);
        Assert.True(spell.NativeCasting);

        spell.SuppressToggleOff = false;
        var second = Execute(ToggleOff(0, Ember), natives: natives);
        Assert.Equal(ServiceActionDisposition.Faulted, second.Disposition);
        Assert.Equal(1, spell.FireCalls);
        Assert.True(spell.NativeCasting);
    }

    [Fact]
    public void ACastResolvesEveryTargetRequestItOpens()
    {
        var spell = Equip(Ember);
        var target = new StructureSO();
        global::TargetingManager.AvailableTarget = target;
        spell.RequestsOnFire = 2;

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Committed, result.Disposition);
        Assert.Equal(2, global::TargetingManager.SubmittedTargets.Count);
        Assert.False(global::TargetingManager.IsTargeting());
    }

    [Fact]
    public void ACastWhoseTargetRequestNothingCanSatisfyFaultsRatherThanLeavingThePromptOpen()
    {
        var spell = Equip(Ember);
        spell.RequestsOnFire = 1;
        global::TargetingManager.AvailableTarget = null;

        var result = Execute(Fire(0, Ember));

        Assert.Equal(ServiceActionDisposition.Faulted, result.Disposition);
        Assert.Equal(CommonActionResultCodes.AdapterFault, result.Code);
    }

    [Fact]
    public void AMutationThatDoesNotMoveTheFireHookFaultsAndBlocksThatSpellUntilTheNextLifecycle()
    {
        // The audited postcondition. A native call that does not deliver means the suite no longer
        // understands the contract, so it faults and refuses to call again rather than firing on
        // every cycle forever.
        var spell = Equip(Ember);
        spell.EmitFireSignal = false;
        var natives = new AutoCastNativeAdapter();

        var first = Execute(Fire(0, Ember), natives: natives);
        Assert.Equal(ServiceActionDisposition.Faulted, first.Disposition);
        Assert.Equal(1, spell.FireCalls);

        spell.EmitFireSignal = true;
        var second = Execute(Fire(0, Ember), natives: natives);
        Assert.Equal(ServiceActionDisposition.Faulted, second.Disposition);
        Assert.Equal(1, spell.FireCalls);

        // A lifecycle boundary is the one thing that clears it, which is the whole retained-state rule.
        natives.InvalidateLifecycle();
        var third = Execute(Fire(0, Ember), natives: natives);
        Assert.Equal(ServiceActionDisposition.Committed, third.Disposition);
        Assert.Equal(2, spell.FireCalls);
    }

    [Fact]
    public void ABlockedSpellDoesNotBlockTheRestOfTheLoadout()
    {
        var ember = Equip(Ember);
        var frost = Equip(Frost);
        ember.EmitFireSignal = false;
        var natives = new AutoCastNativeAdapter();

        Assert.Equal(ServiceActionDisposition.Faulted, Execute(Fire(0, Ember), natives: natives).Disposition);
        Assert.Equal(ServiceActionDisposition.Committed, Execute(Fire(1, Frost), natives: natives).Disposition);
        Assert.Equal(1, frost.FireCalls);
    }

    // ---- Helpers -----------------------------------------------------------------------------

    private static AutoCastCycleAction Fire(int slotIndex, Guid spellId, bool chargeable = false) =>
        new(
            AutoCastActionKind.Fire,
            slotIndex,
            spellId,
            PlannedEpoch,
            new AutoCastPlanBelief(true, chargeable, 1, 1, 1));

    private static AutoCastCycleAction Release(int slotIndex, Guid spellId) =>
        new(AutoCastActionKind.ReleaseCharge, slotIndex, spellId, PlannedEpoch);

    private static AutoCastCycleAction ToggleOff(int slotIndex, Guid spellId) =>
        new(AutoCastActionKind.ToggleOff, slotIndex, spellId, PlannedEpoch);

    private static global::Spell Equip(Guid spellId)
    {
        var spell = new global::Spell(new global::SpellRecipeSO { uuid = spellId.ToString("D") });
        global::SpellManager.instance!.activeSpells.Add(spell);
        return spell;
    }

    private static ServiceActionResult Execute(
        AutoCastCycleAction action,
        long nativeEpoch = PlannedEpoch,
        bool ownsActionFamily = true,
        AutoCastOperationMode mode = AutoCastOperationMode.Active,
        bool fullCharge = false,
        AutoCastNativeAdapter? natives = null,
        AutoCastManualPauseState? manualPause = null)
    {
        var adapter = new AutoCastCycleActionAdapter(
            natives ?? new AutoCastNativeAdapter(),
            () => nativeEpoch,
            () => ownsActionFamily,
            manualPause ?? new AutoCastManualPauseState());
        var config = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = false },
            AutoCast = new AutoCastConfiguration
            {
                Mode = mode,
                FullCharge = fullCharge,
                ManualPauseSeconds = 5f,
            },
        };
        var context = new ServiceActionContext(
            new ServiceCycleIdentity(
                new ServiceId("orbautomata.auto-cast"),
                new LifecycleGeneration(1),
                new ConfigGeneration(1),
                new StrategyGeneration(1),
                new WorldGeneration(1),
                new CycleId(1)),
            new BatchId(1),
            new ActionId(1),
            0,
            new MonotonicTimestamp(1000));
        return adapter.TryExecute(in action, in config, in context);
    }

    private static void ResetNativeState()
    {
        global::SpellManager.instance = new global::SpellManager();
        global::SpellManager.NativeCanCast = true;
        global::SettingsManager.CancellableSpells = true;
        global::TargetingManager.Reset();
        global::Spell.FireSignal = AutoCastManualSignal.NotifySpellFire;
    }
}

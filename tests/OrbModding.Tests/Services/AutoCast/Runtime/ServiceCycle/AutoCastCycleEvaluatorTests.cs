using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Services.AutoCast.Runtime.ServiceCycle;

/// <summary>
/// Policy tests for the Auto Cast worker. Worlds are built directly so each term of the admission
/// ladder — occupancy, caster and spell readiness, the reserve floor and the start threshold —
/// can be exercised on its own, along with the rotation and the full-charge hold.
/// </summary>
/// <remarks>
/// Target requests remain live-only graph state and are the subject of the action-adapter tests.
/// </remarks>
public sealed class AutoCastCycleEvaluatorTests
{
    private static readonly Guid Ember = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Frost = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Gale = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Mana = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid Blood = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public void WaitsForTheNextPublicationWhenThereIsNoWork()
    {
        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));

        var actions = Plan(World(), Config(), ref state, out var wake);

        Assert.Empty(actions);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void NonOperationalConfigurationPlansNothingButStillReschedules()
    {
        var world = World(Slot(0, Ember, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(mode: AutoCastOperationMode.Disabled), ref state, out var wake));
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
        Assert.Empty(Plan(world, Config(enabled: false), ref state, out _));
        Assert.Empty(Plan(world, Config(emergencyDisabled: true), ref state, out _));
    }

    [Fact]
    public void AnEmptySlotIsNeverPlanned()
    {
        var world = World(Slot(0, Guid.Empty, occupied: false, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(), ref state, out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.Empty);
        Assert.Equal(0, metrics.EligibleSlots);
    }

    [Fact]
    public void ASlotAlreadyCastingIsNotItsTurn()
    {
        var world = World(Slot(0, Ember, castReady: true, casting: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(), ref state, out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.Busy);
    }

    [Fact]
    public void AManagerWideBusyReadingIsQuietPlanningBackpressure()
    {
        var world = World(Slot(0, Ember, castReady: true, casterAvailable: false));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(), ref state, out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.Busy);
    }

    [Fact]
    public void TheGamesOwnReadinessAnswerIsFinal()
    {
        // Everything under CanCast — cooldown, charges, attunement, the game's own affordability
        // reckoning — is the game's to decide. The planner asks once and does not second-guess it.
        var world = World(Slot(0, Ember, castReady: false));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(), ref state, out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.NotReady);
    }

    [Fact]
    public void ACastThatWouldBreakTheReserveFloorIsRefused()
    {
        var world = World(
            new[] { Slot(0, Ember, castReady: true) },
            new[] { Cost(0, WorldSpellCostKind.Immediate, Mana, 40d) },
            new[] { Resource(Mana, quantity: 100d, capacity: 1000d) });

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(absoluteReserve: "70"), ref state, out _, out var blocked));
        Assert.Equal(1, blocked.Exclusions.ReserveFloor);

        // 40 spent out of 100 leaves 60, which clears a floor of 50 and not one of 70.
        state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Single(Plan(world, Config(absoluteReserve: "50"), ref state, out _));
    }

    [Fact]
    public void DrainIsUpkeepRatherThanASpendSoTheReserveDoesNotPriceIt()
    {
        // The reserve asks what remains after paying, and an ongoing cost never finishes being paid.
        var world = World(
            new[] { Slot(0, Ember, castReady: true) },
            new[] { Cost(0, WorldSpellCostKind.Drain, Mana, 900d) },
            new[] { Resource(Mana, quantity: 100d, capacity: 1000d) });

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Single(Plan(world, Config(absoluteReserve: "50"), ref state, out _));
    }

    [Fact]
    public void AResourceBelowTheStartThresholdHoldsTheSpellBack()
    {
        var world = World(
            new[] { Slot(0, Ember, castReady: true) },
            new[] { Cost(0, WorldSpellCostKind.Immediate, Mana, 1d) },
            new[] { Resource(Mana, quantity: 300d, capacity: 1000d) });

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(startResourcePercent: 50f), ref state, out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.BelowStartThreshold);

        state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Single(Plan(world, Config(startResourcePercent: 25f), ref state, out _));
    }

    [Fact]
    public void TheStartThresholdPricesDrainToo()
    {
        // A spell that drains a resource is exactly the kind that should wait for it to fill, which
        // is why the threshold weighs both kinds where the reserve weighs only one.
        var world = World(
            new[] { Slot(0, Ember, castReady: true) },
            new[] { Cost(0, WorldSpellCostKind.Drain, Blood, 1d) },
            new[] { Resource(Blood, quantity: 100d, capacity: 1000d) });

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(startResourcePercent: 50f), ref state, out _, out var metrics));
        Assert.Equal(1, metrics.Exclusions.BelowStartThreshold);
    }

    [Fact]
    public void AResourceWithNoCeilingIsExemptFromTheStartThreshold()
    {
        // A share of an unbounded pool means nothing, so demanding one would block the spell forever.
        var world = World(
            new[] { Slot(0, Ember, castReady: true) },
            new[] { Cost(0, WorldSpellCostKind.Immediate, Mana, 1d) },
            new[] { Resource(Mana, quantity: 5d, capacity: -1d) });

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Single(Plan(world, Config(startResourcePercent: 90f), ref state, out _));
    }

    [Fact]
    public void TheLoadoutTakesTurnsRatherThanRepeatingTheFirstEligibleSlot()
    {
        var world = World(
            Slot(0, Ember, castReady: true),
            Slot(1, Frost, castReady: true),
            Slot(2, Gale, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Equal(0, Assert.Single(Plan(world, Config(), ref state, out _, out var first)).SlotIndex);
        Assert.Equal(3, first.EligibleSlots);
        Assert.Equal(2, first.Exclusions.Outranked);

        Assert.Equal(1, Assert.Single(Plan(world, Config(), ref state, out _)).SlotIndex);
        Assert.Equal(2, Assert.Single(Plan(world, Config(), ref state, out _)).SlotIndex);

        // And back round, which is what makes it a rotation rather than a queue that runs out.
        Assert.Equal(0, Assert.Single(Plan(world, Config(), ref state, out _)).SlotIndex);
    }

    [Fact]
    public void ASlotThatCannotCastCostsItselfItsTurnRatherThanEveryOtherSlotTheirs()
    {
        // The cursor moves on the plan, not on the commit. The boundary owns terms the planner cannot
        // see, so a cursor that waited for success would re-pick the same unfireable slot forever.
        var world = World(
            Slot(0, Ember, castReady: true),
            Slot(1, Frost, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Equal(0, Assert.Single(Plan(world, Config(), ref state, out _)).SlotIndex);
        Assert.Equal(1, Assert.Single(Plan(world, Config(), ref state, out _)).SlotIndex);
    }

    [Fact]
    public void TheActionNamesTheGamesOwnSlotPositionAndNotTheRowsPlaceInTheTable()
    {
        // The table omits positions it could not read, so its second row can be the game's fifth slot.
        var world = World(
            Slot(0, Ember, castReady: false),
            Slot(4, Frost, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        var action = Assert.Single(Plan(world, Config(), ref state, out _));
        Assert.Equal(4, action.SlotIndex);
        Assert.Equal(Frost, action.SpellRecipeId);
    }

    [Fact]
    public void AChannelInProgressPausesTheWholeRotationRatherThanItsOwnSlot()
    {
        // The caster is occupied, so nothing else can go either.
        var world = World(
            Slot(0, Ember, castReady: true, casting: true, channeled: true),
            Slot(1, Frost, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(), ref state, out var wake, out var metrics));
        Assert.True(metrics.ChannelBlocked);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void TheRotationStandsAsideWhileThePlayerHasTheLoadoutOnScreen()
    {
        // The game refuses to remove a spell that is not at full charges and idle, so a rotation
        // that kept casting would make every loadout edit fail.
        var world = OnScreen(
            World(Slot(0, Ember, castReady: true)),
            KnownEntities.ScreenMagic.Uuid,
            KnownEntities.MagicSpellbook.Uuid,
            KnownEntities.MagicSpellbookLoadout.Uuid);

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Empty(Plan(world, Config(), ref state, out var wake, out var metrics));
        Assert.True(metrics.LoadoutOpen);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void ALoadoutTabRememberedBehindAnotherScreenDoesNotStopTheRotation()
    {
        var world = OnScreen(
            World(Slot(0, Ember, castReady: true)),
            KnownEntities.MagicSpellbook.Uuid,
            KnownEntities.MagicSpellbookLoadout.Uuid);

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        var action = Assert.Single(Plan(world, Config(), ref state, out _, out var metrics));
        Assert.Equal(0, action.SlotIndex);
        Assert.False(metrics.LoadoutOpen);
    }

    [Fact]
    public void ThePlanCarriesTheEpochTheWorldItCameFromWasCollectedUnder()
    {
        var world = World(
            new[] { Slot(0, Ember, castReady: true) },
            Array.Empty<WorldSpellCost>(),
            Array.Empty<RawResourceSample>(),
            collectedAtEpoch: 77);

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Equal(77, Assert.Single(Plan(world, Config(), ref state, out _)).CollectedAtEpoch);
    }

    [Fact]
    public void ThePlanCarriesWhatItBelievedSoARefusalCanBeReadAgainstIt()
    {
        var world = World(
            Slot(0, Ember, castReady: true, chargeable: true, currentCharges: 2, maximumCharges: 3),
            Slot(1, Frost, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        var belief = Assert.Single(Plan(world, Config(), ref state, out _)).Belief;
        Assert.True(belief.CastReady);
        Assert.True(belief.Chargeable);
        Assert.Equal(2, belief.CurrentCharges);
        Assert.Equal(3, belief.MaximumCharges);
        Assert.Equal(2, belief.EligibleSlots);
    }

    [Fact]
    public void AChargeableSpellIsHeldAtFullChargeAndWakesOnTheNextWorld()
    {
        var world = World(Slot(0, Ember, castReady: true, chargeable: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        var action = Assert.Single(Plan(world, Config(fullCharge: true), ref state, out var wake, out var metrics));

        Assert.Equal(AutoCastActionKind.Fire, action.Kind);
        Assert.True(metrics.HoldingCharge);
        Assert.Equal(0, state.HeldChargeSlot);
        Assert.Equal(Ember, state.HeldChargeSpellId);

        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void WithTheSettingOffAChargeableSpellIsCastWithoutAHold()
    {
        var world = World(Slot(0, Ember, castReady: true, chargeable: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Single(Plan(world, Config(fullCharge: false), ref state, out var wake, out var metrics));

        Assert.False(metrics.HoldingCharge);
        Assert.Equal(AutoCastCycleState.NoHeldSlot, state.HeldChargeSlot);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void AliveHoldFreezesTheWholeRotationWhileTheSpellIsStillCharging()
    {
        var charging = World(
            Slot(0, Ember, castReady: false, chargeable: true, readyingCast: true),
            Slot(1, Frost, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Plan(World(Slot(0, Ember, castReady: true, chargeable: true)), Config(fullCharge: true), ref state, out _);

        // A second cast during a charge is exactly what the setting exists to prevent, so the other
        // eligible slot waits too.
        Assert.Empty(Plan(charging, Config(fullCharge: true), ref state, out var wake, out var metrics));
        Assert.True(metrics.HoldingCharge);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void AHoldIsLetGoOnceTheGameStopsReportingTheSpellAsCharging()
    {
        var state = HoldingEmberInSlotZero();

        var released = Assert.Single(
            Plan(World(Slot(0, Ember, castReady: true, chargeable: true)), Config(fullCharge: true), ref state, out var wake));

        Assert.Equal(AutoCastActionKind.ReleaseCharge, released.Kind);
        Assert.Equal(0, released.SlotIndex);
        Assert.Equal(Ember, released.SpellRecipeId);
        Assert.Equal(AutoCastCycleState.NoHeldSlot, state.HeldChargeSlot);
        Assert.Equal(WakePolicyKind.OnPublication, wake.Kind);
    }

    [Fact]
    public void AHoldIsLetGoWhenTheSettingIsTurnedOffUnderIt()
    {
        var state = HoldingEmberInSlotZero();
        var charging = World(Slot(0, Ember, castReady: false, chargeable: true, readyingCast: true));

        var released = Assert.Single(Plan(charging, Config(fullCharge: false), ref state, out _));
        Assert.Equal(AutoCastActionKind.ReleaseCharge, released.Kind);
    }

    [Fact]
    public void AHoldIsLetGoWhenThePositionNoLongerHoldsTheSpellItWasTakenFor()
    {
        // A rearranged loadout cannot keep the hold. The legacy engine held a native reference, and a
        // reference could not survive this either.
        var state = HoldingEmberInSlotZero();
        var rearranged = World(Slot(0, Frost, castReady: false, chargeable: true, readyingCast: true));

        var released = Assert.Single(Plan(rearranged, Config(fullCharge: true), ref state, out _));
        Assert.Equal(AutoCastActionKind.ReleaseCharge, released.Kind);
        Assert.Equal(Ember, released.SpellRecipeId);
    }

    [Fact]
    public void AHoldIsLetGoWhenTheSlotVanishesFromTheWorldEntirely()
    {
        var state = HoldingEmberInSlotZero();

        var released = Assert.Single(Plan(World(), Config(fullCharge: true), ref state, out _));
        Assert.Equal(AutoCastActionKind.ReleaseCharge, released.Kind);
        Assert.Equal(AutoCastCycleState.NoHeldSlot, state.HeldChargeSlot);
    }

    [Fact]
    public void EveryCapturedSlotIsAccountedForByExactlyOneTerm()
    {
        var world = World(
            Slot(0, Guid.Empty, occupied: false),
            Slot(1, Ember, castReady: true, casting: true),
            Slot(2, Frost, castReady: false),
            Slot(3, Gale, castReady: true),
            Slot(4, Ember, castReady: true));

        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Assert.Single(Plan(world, Config(), ref state, out _, out var metrics));

        Assert.Equal(5, metrics.CapturedSlots);
        Assert.Equal(
            metrics.CapturedSlots,
            metrics.PlannedActions + metrics.Exclusions.Total);
    }

    private static AutoCastCycleState HoldingEmberInSlotZero()
    {
        var state = AutoCastCycleState.Create(new LifecycleGeneration(1));
        Plan(
            World(Slot(0, Ember, castReady: true, chargeable: true)),
            Config(fullCharge: true),
            ref state,
            out _);
        Assert.Equal(0, state.HeldChargeSlot);
        return state;
    }

    private static WorldSpellSlot Slot(
        int slotIndex,
        Guid spellRecipeId,
        bool occupied = true,
        bool casting = false,
        bool readyingCast = false,
        bool attuning = false,
        bool channeled = false,
        bool toggled = false,
        bool chargeable = false,
        bool castReady = false,
        bool chargeAvailable = true,
        bool resourcesCovered = true,
        int currentCharges = 1,
        int maximumCharges = 1,
        double cooldownRemaining = 0d,
        bool casterAvailable = true) =>
        new(
            slotIndex,
            spellRecipeId,
            occupied,
            casting,
            readyingCast,
            attuning,
            channeled,
            toggled,
            chargeable,
            castReady,
            chargeAvailable,
            resourcesCovered,
            currentCharges,
            maximumCharges,
            new BigDouble(cooldownRemaining),
            casterAvailable);

    private static WorldSpellCost Cost(
        int slotIndex,
        WorldSpellCostKind kind,
        Guid resourceId,
        double amount) =>
        new(slotIndex, kind, resourceId, new BigDouble(amount));

    private static RawResourceSample Resource(Guid resourceId, double quantity, double capacity) =>
        WorldSamples.Resource(resourceId, quantity, capacity);

    private static GameWorldState World(params WorldSpellSlot[] slots) =>
        World(slots, Array.Empty<WorldSpellCost>(), Array.Empty<RawResourceSample>());

    private static GameWorldState World(
        WorldSpellSlot[] slots,
        WorldSpellCost[] costs,
        RawResourceSample[] resources,
        long collectedAtEpoch = 1)
    {
        var slotBuffer = new WorldSpellSlotBuffer();
        foreach (var slot in slots) slotBuffer.Append(in slot);

        var costBuffer = new WorldSpellCostBuffer();
        foreach (var cost in costs) costBuffer.Append(in cost);

        var deriver = new WorldResourceDeriver(default);
        var rows = new WorldResource[resources.Length];
        for (var index = 0; index < resources.Length; index++)
            rows[index] = deriver.Derive(in resources[index]);

        return new GameWorldState
        {
            SpellSlots = WorldSpellSlotDeriver.Build(slotBuffer),
            SpellCosts = WorldSpellCostDeriver.Build(costBuffer),
            Resources = WorldTable.Create(rows),
            CollectedAtEpoch = collectedAtEpoch,
        };
    }

    private static GameWorldState OnScreen(GameWorldState world, params Guid[] activeViews)
    {
        Guid[] route =
        {
            KnownEntities.ScreenMagic.Uuid,
            KnownEntities.MagicSpellbook.Uuid,
            KnownEntities.MagicSpellbookLoadout.Uuid,
        };
        var views = new WorldView[route.Length];
        for (var index = 0; index < route.Length; index++)
        {
            views[index] = new WorldView(
                route[index],
                Array.IndexOf(activeViews, route[index]) >= 0,
                alwaysActive: false,
                available: true);
        }
        return world with { Views = WorldTable.Create(views) };
    }

    private static SuiteRuntimeConfiguration Config(
        bool enabled = true,
        bool emergencyDisabled = false,
        AutoCastOperationMode mode = AutoCastOperationMode.Active,
        bool fullCharge = false,
        float startResourcePercent = 0f,
        string absoluteReserve = "0") =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = enabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            Reserves = new AutomataReserveConfiguration { AbsoluteReserve = absoluteReserve },
            AutoCast = new AutoCastConfiguration
            {
                Mode = mode,
                FullCharge = fullCharge,
                StartResourcePercent = startResourcePercent,
            },
        };

    private static IReadOnlyList<AutoCastCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoCastCycleState state,
        out WakePolicy wake) =>
        Plan(world, config, ref state, out wake, out _);

    private static IReadOnlyList<AutoCastCycleAction> Plan(
        GameWorldState world,
        SuiteRuntimeConfiguration config,
        ref AutoCastCycleState state,
        out WakePolicy wake,
        out AutoCastDecisionMetrics metrics)
    {
        var store = new ReusableActionStore<AutoCastCycleAction>();
        store.BeginWrite();
        var writer = new ServiceActionWriter<AutoCastCycleAction>(store);
        wake = AutoCastCycleEvaluator.Evaluate(world, in config, ref state, writer, out metrics);

        var actions = new List<AutoCastCycleAction>(store.Count);
        while (!store.IsComplete)
        {
            actions.Add(store.GetCurrent());
            store.CommitCurrentAndClear();
        }

        return actions;
    }
}

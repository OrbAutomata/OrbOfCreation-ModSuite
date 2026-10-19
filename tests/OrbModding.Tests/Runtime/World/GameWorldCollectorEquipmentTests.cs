using System;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorEquipmentTests : GameWorldCollectorTestBase
{
    [Fact]
    public void Equipment_rows_publish_the_exact_native_loadout_decision_and_usage_holdings()
    {
        var resource = new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = 80d,
            quality = new FakeModifierRecord(100d),
        };
        FakeResource.All.Add(resource);
        var type = new FakeEquipmentType
        {
            Identity = Guid.NewGuid(),
            maxTypeSlots = new FakeModifierRecord(2d),
        };
        FakeEquipmentType.All.Add(type);
        var equipment = new FakeEquipment
        {
            Identity = Guid.NewGuid(),
            isCreated = true,
            equipmentType = type,
            maximumStacks = 4,
            usageCost = new FakeCraftingResourceCostList
            {
                maximumCostTimes = new BigDouble(3),
            }.With(resource, new BigDouble(20)),
        };
        FakeEquipment.All.Add(equipment);
        FakeEquipmentManager.instance.equippedEquipment.maximum = 3;

        var collector = Collector();
        var report = collector.Collect();
        var equipmentRows = collector.Build().Equipment;
        Assert.Equal(1, equipmentRows.Count);
        var row = equipmentRows[0];

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(row.Loadout.Available, row.Loadout.UnavailableReason);
        Assert.Equal(type.Identity, row.Loadout.EquipmentTypeId);
        Assert.Equal(0, row.Loadout.EquippedStacks);
        Assert.Equal(4, row.Loadout.MaximumStacks);
        Assert.Equal(3, row.Loadout.MaximumEquipAmount);
        Assert.Equal(0, row.Loadout.MaximumUnequipAmount);
        Assert.True(row.Loadout.UsageAffordable);
        Assert.Equal(1, row.Loadout.Costs.Count);
        var cost = row.Loadout.Costs[0];
        Assert.Equal(resource.Identity, cost.ResourceId);
        Assert.Equal(0, cost.Cost.CompareTo(new BigDouble(20)));
    }

    [Fact]
    public void Player_loadouts_and_snapshot_slots_are_collected_with_named_entry_identities()
    {
        var spellRecipe = new FakeSpellRecipe { discovered = true };
        var equipment = new FakeEquipment { isCreated = true };
        var alchemy = new FakeAlchemyRecipe { discovered = true };
        FakeSpellRecipe.All.Add(spellRecipe);
        FakeEquipment.All.Add(equipment);
        FakeAlchemyRecipe.All.Add(alchemy);

        var player = new FakePlayerLoadout
        {
            isSelected = true,
            saveEquipment = true,
            saveAlchemy = true,
        };
        player.GetLabel().SetName("Boss setup");
        player.spells.Add(new FakeSpell
        {
            guidContainer = new FakeReferencedEntity { Identity = Guid.NewGuid() },
            spellReference = spellRecipe,
        });
        player.equipment.Set(equipment, 2);
        player.alchemy.Set(alchemy, 3);
        FakeLoadoutManager.instance.playerLoadouts.value.Add(player);

        var equipmentSnapshot = new FakeEquipmentSnapshot();
        var equipmentRecord = new FakeLoadoutRecord<FakeEquipment>();
        equipmentRecord.Set(equipment, 2);
        equipmentSnapshot.SaveSnapshot(equipmentRecord);
        FakeLoadoutManager.instance.equipmentLoadouts.value.Add(equipmentSnapshot);
        FakeLoadoutManager.instance.alchemyLoadouts.value.Add(new FakeAlchemySnapshot());

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLoadoutLookup.TryFindPlayer(world.PlayerLoadouts,
            player.Identity, out var published));
        Assert.Equal("Boss setup", published.Name);
        Assert.True(published.Selected);
        Assert.Equal(3, world.PlayerLoadoutEntries.Count);
        Assert.Contains(world.PlayerLoadoutEntries.AsSpan().ToArray(),
            row => row.Kind == WorldLoadoutEntryKind.Spell &&
                   row.ReferenceId == spellRecipe.Identity);
        Assert.Contains(world.SnapshotSlots.AsSpan().ToArray(),
            row => row.OwnerId == FakeLoadoutManager.instance.equipmentLoadouts.Identity &&
                   row.Slot == 0 && row.Populated);
        Assert.Contains(world.SnapshotEntries.AsSpan().ToArray(),
            row => row.EntryId == equipment.Identity && row.Quantity == 2);
    }

    /// <summary>
    /// A loadout entry the reader could not identify is a shortfall the report names. Only a slot
    /// that genuinely holds nothing stays silent.
    /// </summary>
    [Fact]
    public void A_loadout_entry_without_an_identity_is_skipped_by_name()
    {
        var unidentified = new FakeEquipment { isCreated = true, Identity = Guid.Empty };
        var player = new FakePlayerLoadout { isSelected = true, saveEquipment = true };
        player.spells.Add(new FakeSpell { empty = true });
        player.equipment.Set(unidentified, 2);
        FakeLoadoutManager.instance.playerLoadouts.value.Add(player);

        var collector = Collector();
        var report = collector.Collect().For("loadouts");
        var world = collector.Build();

        Assert.Equal(1, report.Skipped);
        Assert.Equal("a loadout's equipment entry had an empty identity", report.FirstFailure);
        Assert.True(WorldLoadoutLookup.TryFindPlayer(world.PlayerLoadouts, player.Identity, out _));
        Assert.Equal(0, world.PlayerLoadoutEntries.Count);
    }

    /// <summary>
    /// A loadout that throws after an earlier one was read withdraws the category whole, so no
    /// loadout reads as one the player does not have.
    /// </summary>
    [Fact]
    public void A_loadout_read_that_throws_part_way_leaves_no_loadouts_behind()
    {
        FakeLoadoutManager.instance.playerLoadouts.value.Add(new FakePlayerLoadout { isSelected = true });
        FakeLoadoutManager.instance.playerLoadouts.value.Add(new FakePlayerLoadout { throwsOnSpells = true });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("loadouts").Outcome);
        Assert.Equal(0, world.PlayerLoadouts.Count);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void A_cast_in_progress_blocks_switching_without_sounding_the_games_refusal(
        bool casting, bool readyingCast)
    {
        FakeLoadoutManager.instance.playerLoadouts.value.Add(new FakePlayerLoadout { isSelected = true });
        var other = new FakePlayerLoadout();
        FakeLoadoutManager.instance.playerLoadouts.value.Add(other);
        FakeLoadoutManager.instance.activeSpells.value.Add(new FakeSpell { empty = true });
        FakeLoadoutManager.instance.activeSpells.value.Add(
            new FakeSpell { casting = casting, readyingCast = readyingCast });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLoadoutLookup.TryFindPlayer(world.PlayerLoadouts, other.Identity, out var published));
        Assert.False(published.CanSwitchNow);
        Assert.Equal(0, FakeLoadoutManager.instance.RefusalFeedbacks);
    }

    /// <summary>
    /// The equipped loadout publishes one row per readable position, keyed by the game's own index.
    /// </summary>
    /// <remarks>
    /// The index is the whole point. It is what a cast is addressed by, so it has to count the holes
    /// the player leaves in the loadout — a table that renumbered its rows densely would still look
    /// right in every assertion about occupancy and would fire the wrong spell.
    /// </remarks>
    [Fact]
    public void TheEquippedLoadoutIsPublishedByThePositionACastIsAddressedBy()
    {
        var fireball = new FakeSpellRecipe { discovered = true, masteryLevel = 5 };
        FakeSpellRecipe.All.Add(fireball);
        var echo = new FakeGlyph
        {
            Identity = Guid.NewGuid(),
            discovered = true,
            augmentsSpells = true,
            maxUsages = new FakeModifierRecord(3d),
        };
        FakeGlyph.All.Add(echo);

        var water = Guid.NewGuid();
        var mana = Guid.NewGuid();

        var equipped = new FakeSpell
        {
            spellReference = fireball,
            chargeable = true,
            castReady = true,
            currentCharges = 2,
            maximumCharges = 3,
            cooldownRemaining = new BigDouble(4d, 0),
            outputLevel = 4,
            effectiveLevel = 6,
            requiredMasteryLevel = 3,
            durationSpell = true,
            usageRequirementsMet = false,
            cost = new FakeSpellCostList().With(water, 50d),
            drainCost = new FakeSpellCostList().With(mana, 7d),
            weight = new FakeSpellCostList().With(mana, 3d),
        };
        equipped.augmentGlyphs.Add(echo);
        equipped.augmentGlyphs.Add(echo);

        var loadout = new FakeSpellLoadout();
        loadout.value.Add(equipped);
        loadout.value.Add(null);
        loadout.value.Add(new FakeSpell { empty = true });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        // The hole publishes nothing at all, so two rows for three positions.
        Assert.Equal(2, world.SpellSlots.Count);

        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 0, out var first));
        Assert.True(first.Occupied);
        Assert.Equal(fireball.Identity, first.SpellRecipeId);
        Assert.True(first.Chargeable);
        Assert.True(first.CastReady);
        Assert.Equal(2, first.CurrentCharges);
        Assert.Equal(3, first.MaximumCharges);
        Assert.Equal(4d, first.CooldownRemaining.ToDouble());
        Assert.Equal(4, first.OutputLevel);
        Assert.Equal(6, first.EffectiveLevel);
        Assert.Equal(3, first.RequiredMasteryLevel);
        Assert.Equal(5, first.RecipeMasteryLevel);
        Assert.True(first.DurationSpell);
        Assert.False(first.UsageRequirementsMet);
        Assert.True(first.CancellationEnabled);
        var applied = Assert.Single(first.AugmentGlyphs.AsSpan().ToArray());
        Assert.Equal(echo.Identity, applied.GlyphId);
        Assert.Equal(2, applied.Quantity);

        // The empty position keeps its index rather than sliding down into the hole's place.
        Assert.False(WorldSpellSlotLookup.TryFind(world.SpellSlots, 1, out _));
        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 2, out var third));
        Assert.False(third.Occupied);
        Assert.Equal(Guid.Empty, third.SpellRecipeId);

        // Both prices and the weight are published against the same position, and they do not run
        // together.
        Assert.True(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 0, WorldSpellCostKind.Immediate, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(water, world.SpellCosts[start].ResourceId);
        Assert.Equal(50d, world.SpellCosts[start].Amount.ToDouble());

        Assert.True(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 0, WorldSpellCostKind.Drain, out var drainStart, out var drainCount));
        Assert.Equal(1, drainCount);
        Assert.Equal(mana, world.SpellCosts[drainStart].ResourceId);
        Assert.Equal(7d, world.SpellCosts[drainStart].Amount.ToDouble());

        Assert.True(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 0, WorldSpellCostKind.Weight, out var weightStart, out var weightCount));
        Assert.Equal(1, weightCount);
        Assert.Equal(mana, world.SpellCosts[weightStart].ResourceId);
        Assert.Equal(3d, world.SpellCosts[weightStart].Amount.ToDouble());

        // An empty position is priced at nothing rather than at whatever the last one cost.
        Assert.False(WorldSpellCostLookup.TryFindRange(
            world.SpellCosts, 2, WorldSpellCostKind.Immediate, out _, out _));
    }

    /// <summary>
    /// Every per-slot state the game distinguishes is published, one field per question.
    /// </summary>
    /// <remarks>
    /// These are the terms a planner skips on, and the game answers them separately because they mean
    /// different things — a channelling spell occupies the caster, a toggled one is already up, and an
    /// attuning one is neither. Folding them into one "busy" flag would lose the reason, and the
    /// reason is what a refusal has to be explained by.
    /// </remarks>
    [Fact]
    public void EachSlotCarriesTheGamesOwnAnswerForEveryStateItDistinguishes()
    {
        FakeSettingsManager.CancellableSpells = false;
        FakeSpellManager.NativeCanCast = false;
        var loadout = new FakeSpellLoadout();
        loadout.value.Add(new FakeSpell
        {
            spellReference = null,
            casting = true,
            readyingCast = true,
            attuning = true,
            channeled = true,
            toggled = true,
            chargeable = true,
            castReady = false,
            chargeAvailable = false,
            resourcesCovered = false,
        });
        loadout.value.Add(new FakeSpell());
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 0, out var busy));
        Assert.True(busy.Casting);
        Assert.True(busy.ReadyingCast);
        Assert.True(busy.Attuning);
        Assert.True(busy.Channeled);
        Assert.True(busy.Toggled);
        Assert.True(busy.Chargeable);
        Assert.False(busy.CastReady);
        Assert.False(busy.ChargeAvailable);
        Assert.False(busy.CanRemove);
        Assert.False(busy.ResourcesCovered);
        Assert.False(busy.CancellationEnabled);

        // An occupant with no recipe behind it still publishes a row: the slot is filled, and a
        // consumer that cannot name what is in it should see that rather than see nothing.
        Assert.True(busy.Occupied);
        Assert.False(busy.CasterAvailable);
        Assert.Equal(Guid.Empty, busy.SpellRecipeId);

        // The neighbouring slot is the negative of all of it, from the same pass.
        Assert.True(WorldSpellSlotLookup.TryFind(world.SpellSlots, 1, out var idle));
        Assert.False(idle.Casting);
        Assert.False(idle.ReadyingCast);
        Assert.False(idle.Attuning);
        Assert.False(idle.Channeled);
        Assert.False(idle.Toggled);
        Assert.False(idle.CasterAvailable);
        Assert.True(idle.CastReady);
        Assert.True(idle.CanRemove);
        Assert.False(idle.CancellationEnabled);
        Assert.Equal(1, loadout.value[0]!.CanRemoveCalls);
        Assert.Equal(1, loadout.value[1]!.CanRemoveCalls);
    }

    /// <summary>
    /// Queue occupancy is derived from the slots even when the discarded native answer disagrees.
    /// </summary>
    /// <remarks>
    /// The action boundary remains authoritative at admission. Capture publishes its one slot walk
    /// instead of asking for a second, potentially inconsistent composition.
    /// </remarks>
    [Fact]
    public void QueueOccupancyComesFromTheCapturedSlots()
    {
        var queue = new FakeActionQueue { ReportedUsedSpots = 2 };
        queue.value.Add(new FakeQueueSlot { quantity = 1 });
        queue.value.Add(new FakeQueueSlot());
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(1, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.True(row.Consistent);
    }

    /// <summary>
    /// An entry the reader's accessors were not bound against costs the whole queue reading.
    /// </summary>
    /// <remarks>
    /// Ported from the action boundary, which makes exactly this check before it reads a slot. Every
    /// accessor here is compiled against the instance type; an entry of another type is not a slot
    /// this reader can read, and half a queue is worse than a queue reported as unread.
    /// </remarks>
    [Fact]
    public void AQueueEntryOfAnotherTypeCostsTheWholeReading()
    {
        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 1 });
        queue.value.Add(new FakeForeignQueueSlot { quantity = 1 });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        var queues = report.For("action queues");
        Assert.Equal(0, queues.Sampled);
        Assert.Equal(1, queues.Skipped);
        Assert.Contains("not a plot-action instance", queues.FirstFailure, StringComparison.Ordinal);
        Assert.False(report.IsComplete);

        // Nothing partial reached the snapshot — not the queue, and not the slot that did read.
        Assert.Equal(0, world.ActionQueues.Count);
        Assert.Equal(0, world.ActionQueueSlots.Count);
    }

    /// <summary>
    /// A slot naming a pair no table holds still publishes the pair it names.
    /// </summary>
    /// <remarks>
    /// The queue is read before anything is known about what the other categories collected, and it
    /// stays that way: a slot is what the game says is in it. Dropping a slot whose plot or action
    /// is not in the snapshot would make the queue look emptier than it is, which is the one
    /// direction that makes a consumer plan another action into a queue that is full.
    /// </remarks>
    [Fact]
    public void ASlotNamingAPairNoTableHoldsIsStillPublished()
    {
        var stranger = new FakePlotNodeAction();
        var elsewhere = new FakePlotNode { Identity = Guid.NewGuid() };

        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 1, plot = elsewhere, action = stranger });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(0, world.PlotNodes.Count);
        Assert.Equal(0, world.PlotActions.Count);

        var slot = world.ActionQueueSlots[0];
        Assert.Equal(elsewhere.Identity, slot.PlotNodeId);
        Assert.Equal(stranger.Identity, slot.PlotNodeActionId);
        Assert.False(slot.Empty);
    }

    /// <summary>
    /// A hole in the list is a slot nothing is running in, which is what an empty slot is.
    /// </summary>
    [Fact]
    public void AHoleInTheQueueIsReadAsAnEmptySlot()
    {
        var queue = new FakeActionQueue();
        queue.value.Add(null);
        queue.value.Add(new FakeQueueSlot { quantity = 2 });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(1, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.True(row.Consistent);
        Assert.True(world.ActionQueueSlots[0].Empty);
    }

    /// <summary>
    /// The attribute queue publishes occupancy and the variable its maximum lives in, and nothing
    /// per-slot.
    /// </summary>
    /// <remarks>
    /// Its entries are actionables of every kind the game queues; what one of them is doing is a
    /// question nobody has asked the game, and inventing an answer would be describing a shape this
    /// suite has not read. The maximum travels as an edge because the game already publishes the
    /// number: it is an <c>IntVariable</c>, and that registry is collected whole.
    /// </remarks>
    [Fact]
    public void TheAttributeQueueCarriesOccupancyAndTheVariableItsMaximumLivesIn()
    {
        var maximum = Guid.NewGuid();
        FakeCount.All.Add(new FakeCount { Identity = maximum, value = new FakeModifierRecord(6d) });

        var queue = new FakeAttributeQueue
        {
            maxQueuedItems = new FakeQueueCapacity { Identity = maximum, Maximum = 6 },
            value = { new object(), new object() },
        };
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveActionables.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(2, row.UsedSlots);
        Assert.True(row.HasEmptySlot);
        Assert.True(row.Consistent);

        // The edge resolves into the row that already carries the number, so nothing publishes it
        // twice and the two cannot drift.
        Assert.Equal(maximum, row.MaxQueuedItemsId);
        Assert.True(WorldLookup.TryFind(world.IntVariables, row.MaxQueuedItemsId, out var capacity));
        Assert.Equal(6d, capacity.Value.ToDouble());

        Assert.Equal(0, world.ActionQueueSlots.Count);
    }

    /// <summary>
    /// An attribute queue whose read throws anything but an expected native failure faults the pass
    /// instead of costing the queue.
    /// </summary>
    [Fact]
    public void AnAttributeQueueWhoseReadThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("capacity variable was destroyed");
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveActionables.Uuid] = new FakeAttributeQueue
        {
            maxQueuedItems = new FakeQueueCapacity { GetGuidFailure = failure },
            value = { new object() },
        };

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A queue the identity registry does not hold is a fact about the save, not a shortfall.
    /// </summary>
    /// <remarks>
    /// The game registers its list variables while it initialises, so a pass that runs before that
    /// has nothing to report. Reporting it as a failed read would make every early cycle look like a
    /// broken build.
    /// </remarks>
    [Fact]
    public void AQueueTheRegistryDoesNotHoldYetIsNotAShortfall()
    {
        var report = Collector().Collect();
        var queues = report.For("action queues");

        Assert.Equal(WorldCategoryOutcome.Collected, queues.Outcome);
        Assert.Equal(0, queues.Sampled);
        Assert.True(queues.IsClean);
    }

    /// <summary>
    /// A queue type that is gone takes the queues with it and nothing else.
    /// </summary>
    [Fact]
    public void AnAbsentQueueTypeDegradesOnlyTheQueues()
    {
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid() });

        var report = Collector(("ActionableListVariable", null)).Collect();
        var queues = report.For("action queues");

        Assert.Equal(WorldCategoryOutcome.Unavailable, queues.Outcome);
        Assert.Contains("ActionableListVariable", queues.FirstFailure, StringComparison.Ordinal);
        Assert.Equal(WorldCategoryOutcome.Collected, report.For("resources").Outcome);
    }
}

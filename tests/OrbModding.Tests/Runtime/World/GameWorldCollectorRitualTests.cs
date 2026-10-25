using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorRitualTests : GameWorldCollectorTestBase
{

    /// <summary>
    /// A ritual that forces its level is fought and priced at that level, whatever its stored
    /// selection says: <c>RitualSO.GetCurrentLevel()</c> answers <c>forceLevelValue</c> there.
    /// Restoration stores 55 and runs at 70.
    /// </summary>
    [Fact]
    public void A_forced_ritual_is_priced_at_the_level_it_runs_at()
    {
        var knowledge = new ResourceSO { Quantity = new BigDouble(80) };
        ResourceSO.All.Add(knowledge);
        var restoration = new RitualSO
        {
            Identity = Guid.NewGuid(),
            discovered = true,
            selectedLevel = 55,
            forceLevel = true,
            forceLevelValue = 70,
            NativeMaximumSelectedLevel = 71,
            activationCost = new ResourceCostList()
                .With(knowledge, new BigDouble(5)),
            completionCostPerLevel = new ModifierListRef { variable = new ModifierListVariable() },
            completionCost = new ResourceCostList()
                .With(knowledge, new BigDouble(2)),
            completionCostMod = new ValueModifierRecord(new BigDouble(100d)).Dirty(),
        };
        restoration.completionCostPerLevel.variable!.value.modifiers.Add(
            new ValueModifier(ValueModifier.ValueModifierType.Raw, 1d, order: 0));
        RitualSO.All.Add(restoration);
        RitualManager.instance!.selectedRitual.ToggleValue(restoration);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Rituals, restoration.Identity, out var ritual));
        Assert.Equal(70, ritual.CurrentLevel);
        Assert.Equal(new BigDouble(72), ritual.Decision.CompletionCosts[0].Cost);
    }

    /// <summary>
    /// A ritual's two senses of "active" are different questions and the row must answer both:
    /// <c>inBattle</c> is a run under way, while <c>ritualInstances.Count &gt; 0</c> — the game's own
    /// <c>HasActiveInstances()</c> and <c>IsDurationActive()</c> — is a finished run whose reward is
    /// still ticking. Reading the save record would have surfaced neither the count nor the tiers.
    /// </summary>
    [Fact]
    public void ARitualCarriesWhetherItIsUnlockedAndWhetherItsEffectsAreRunning()
    {
        var idle = Guid.NewGuid();
        var ticking = Guid.NewGuid();
        var knowledge = new ResourceSO { Quantity = new BigDouble(80) };
        ResourceSO.All.Add(knowledge);

        RitualSO.All.Add(new RitualSO
        {
            Identity = idle,
            discovered = true,
            durationRewardBlocks = { new PersistentEffectBlock() },
            NativeMaximumSelectedLevel = 4,
            activationCost = new ResourceCostList()
                .With(knowledge, new BigDouble(11)),
        });
        var selected = new RitualSO
        {
            Identity = ticking,
            discovered = true,
            inBattle = true,
            selectedLevel = 7,
            reachedLevel = 9,
            CritLevel = 2,
            EchoLevel = 1,
            ChainLevel = 3,
            BattleTotalWeight = new BigDouble(4200d),
            wavesCompleted = 3,
            baseWaves = 5,
            wavesPerLevel = 2,
            maxWaves = 30,
            ritualInstances = new List<object> { new(), new() },
            durationRewardBlocks = { new PersistentEffectBlock() },
            currentSpoils =
            {
                new SpoilsRecordEntry(knowledge, new BigDouble(12)),
            },
            NativeMaximumSelectedLevel = 12,
            activationCost = new ResourceCostList()
                .With(knowledge, new BigDouble(5)),
            completionCostPerLevel = new ModifierListRef { variable = new ModifierListVariable() },
            completionCost = new ResourceCostList()
                .With(knowledge, new BigDouble(2)),
            completionCostMod = new ValueModifierRecord(new BigDouble(100d)).Dirty(),
        };
        selected.completionCostPerLevel.variable!.value.modifiers.Add(
            new ValueModifier(ValueModifier.ValueModifierType.Raw, 1d, order: 0));
        RitualSO.All.Add(selected);
        RitualManager.instance!.selectedRitual.ToggleValue(selected);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.Rituals, idle, out var quiet));
        Assert.True(quiet.Discovered);
        Assert.False(quiet.InBattle);

        // The list is null on this one, which is how the game leaves it before first use.
        Assert.Equal(0, quiet.ActiveInstances);
        Assert.Equal(1, quiet.DurationRewardBlocks);

        // GetActivationCost() reads the ritual's own stored cost and its own level and consults the
        // selection for none of it, so an unselected ritual is priced too — otherwise comparing two
        // of them meant selecting each in turn, which is a mutation.
        Assert.False(quiet.Decision.Selected);
        Assert.Equal(4, quiet.Decision.MaximumStartingLevel);
        Assert.Equal(1, quiet.Decision.ActivationCosts.Count);
        Assert.Equal(new BigDouble(11), quiet.Decision.ActivationCosts[0].Cost);
        Assert.True(quiet.Decision.ActivationAffordable);

        Assert.True(WorldLookup.TryFind(world.Rituals, ticking, out var running));
        Assert.True(running.Decision.Selected);
        Assert.Equal(12, running.Decision.MaximumStartingLevel);
        Assert.True(running.Decision.ActivationAffordable);
        Assert.Equal(1, running.Decision.ActivationCosts.Count);
        Assert.Equal(1, running.Decision.CompletionCosts.Count);
        Assert.Equal(new BigDouble(5), running.Decision.ActivationCosts[0].Cost);
        Assert.Equal(new BigDouble(9), running.Decision.CompletionCosts[0].Cost);
        Assert.True(running.InBattle);
        Assert.Equal(2, running.ActiveInstances);
        Assert.Equal(7, running.SelectedLevel);
        Assert.Equal(9, running.ReachedLevel);
        Assert.Equal(2, running.CritLevel);
        Assert.Equal(1, running.EchoLevel);
        Assert.Equal(3, running.ChainLevel);
        Assert.Equal(4200d, running.BattleTotalWeight.ToDouble());

        // How far the run has to go is the game's own GetRequiredWaves(), which scales the ritual's
        // base by the staged level: the published bounds alone cannot answer it.
        Assert.Equal(19, running.RequiredWaves);

        // The results screen's own two facts. A run that has ended leaves no other record of the
        // verdict End() showed or of what it banked, and neither is derivable from a wave count.
        Assert.True(running.FailedRun);
        Assert.Equal(1, running.Spoils.Count);
        Assert.Equal(knowledge.GetGuid(), running.Spoils[0].ResourceId);
        Assert.Equal(new BigDouble(12), running.Spoils[0].Quantity);
        Assert.Equal(0, quiet.Spoils.Count);
    }
}

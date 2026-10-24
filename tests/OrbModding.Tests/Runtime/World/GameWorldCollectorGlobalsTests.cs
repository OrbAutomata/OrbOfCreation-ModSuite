using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorGlobalsTests : GameWorldCollectorTestBase
{

    /// <summary>
    /// The frame-wide rate terms are read from the player once per pass and reach the derived rows.
    /// </summary>
    /// <remarks>
    /// Asserted by changing one of them and re-collecting rather than by inspecting the frame, because
    /// what matters is that the value a consumer eventually reads moved — reading the globals into a
    /// struct nothing consults would satisfy any weaker assertion.
    /// </remarks>
    [Fact]
    public void TheFrameWideRateTermsAreReadFromThePlayerAndReachTheRows()
    {
        var mana = Guid.NewGuid();
        ResourceSO.All.Add(OverflowingResource(mana));

        var collector = Collector();

        SetOverflow(200d);
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var damped));

        SetOverflow(100d);
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var blocked));

        Assert.NotEqual(damped.TrueRate, blocked.TrueRate);
    }

    /// <summary>
    /// A workbench whose weight budget did not read publishes no loadout room either: an unavailable
    /// category carries nothing, so its half-read capacity cannot pass for the player's.
    /// </summary>
    [Fact]
    public void AWorkbenchThatFailsAfterReadingItsRoomPublishesNoRoom()
    {
        var manager = new SpellManager { spellWeightVariables = null! };
        manager.activeSpells.Maximum = 3;
        manager.activeSpells.value.Add(new Spell());
        SpellManager.instance = manager;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("spell workbench").Outcome);
        Assert.Equal(0, world.SpellWorkbench.EquippedCount);
        Assert.Equal(0, world.SpellWorkbench.MaximumEquipped);
    }

    [Fact]
    public void AWorkbenchLevelDialThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        SpellManager.instance = new SpellManager();
        SetMaximumSpellOutputLevel(new IntVariable { value = null! });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
    }

    /// <summary>
    /// A player that cannot be bound still leaves unrelated rows usable, but the spell workbench
    /// now fails closed because output level is required pre-decision state for composition.
    /// </summary>
    [Fact]
    public void AnUnbindablePlayerLocalizesFailureToTheSpellWorkbench()
    {
        var mana = Guid.NewGuid();
        ResourceSO.All.Add(OverflowingResource(mana));

        var collector = Collector(("Player", null));
        var report = collector.Collect();

        Assert.False(report.IsComplete);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("spell workbench").Outcome);
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var row));
        Assert.False(BigDouble.IsNaN(row.TrueRate) || BigDouble.IsInfinity(row.TrueRate));
    }
}

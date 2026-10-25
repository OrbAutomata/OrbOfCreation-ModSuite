using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorTests : GameWorldCollectorTestBase
{
    [Fact]
    public void OnePassReadsEveryCategory()
    {
        var mana = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        var scholar = Guid.NewGuid();
        var alchemy = Guid.NewGuid();
        var theory = Guid.NewGuid();
        var grimoire = Guid.NewGuid();

        ResourceSO.All.Add(new ResourceSO
        {
            Identity = mana,
            Quantity = 60d,
            maxQuantity = new ValueModifierRecord(100d),
            rate = new ValueModifierRecord(2.5d, activeCount: 1),
            Visible = true,
        });
        StructureSO.All.Add(new StructureSO
        {
            Identity = cauldron,
            structureType = new StructureTypeSO
            {
                Identity = scholar,
            },
            Level = 12,
            Queued = 3,
            Available = true,
        });
        UpgradeSO.All.Add(new UpgradeSO { Identity = alchemy, Level = 1, maxLevel = 1, Available = false });
        ResearchSO.All.Add(new ResearchSO { Identity = theory, level = 4, maxLevel = 10, isDeveloping = true, Available = true });
        RecipeBookSO.All.Add(new RecipeBookSO { Identity = grimoire, Available = true });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        // Five primary entities, two purchase-view relations, two Scribe queues, eight complete
        // zero-candidate Scroll-target evidence rows, one frame-local challenge-decision context
        // row, the two empty snapshot-list owners, the Mana orb the resource panel pins, and the two
        // entities the keyword walk visits — the resource and the structure — which are sampled for
        // their authored keywords whether or not the fixture gave them any.
        Assert.Equal(23, report.TotalSampled);

        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var resource));
        Assert.Equal(60d, resource.Reading.Quantity.ToDouble());
        Assert.Equal(100d, resource.Reading.Capacity.ToDouble());
        Assert.Equal(2.5d, resource.TrueRate.ToDouble());
        Assert.True(resource.Reading.Visible);

        Assert.True(WorldLookup.TryFind(world.Structures, cauldron, out var structure));
        Assert.Equal(12d, structure.Reading.Level.ToDouble());
        Assert.Equal(3d, structure.Reading.QueuedLevels.ToDouble());
        Assert.True(structure.Reading.Unlocked);
        Assert.Equal(scholar, structure.Reading.StructureTypeId);

        Assert.True(WorldLookup.TryFind(world.Upgrades, alchemy, out var upgrade));
        Assert.Equal(1, upgrade.Reading.Level);
        Assert.Equal(1, upgrade.Reading.MaxLevel);
        Assert.False(upgrade.Reading.Available);

        Assert.True(WorldLookup.TryFind(world.Research, theory, out var research));
        Assert.Equal(4, research.Level);
        Assert.True(research.IsDeveloping);
        Assert.True(research.Available);

        Assert.True(WorldLookup.TryFind(world.RecipeBooks, grimoire, out var recipeBook));
        Assert.True(recipeBook.Available);
    }

    [Fact]
    public void AStructuresDisabledEffectFlagIsPublished()
    {
        var active = Guid.NewGuid();
        var disabled = Guid.NewGuid();
        StructureSO.All.Add(new StructureSO { Identity = active, disabled = false });
        StructureSO.All.Add(new StructureSO { Identity = disabled, disabled = true });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Structures, active, out var activeRow));
        Assert.False(activeRow.Reading.Disabled);
        Assert.True(WorldLookup.TryFind(world.Structures, disabled, out var disabledRow));
        Assert.True(disabledRow.Reading.Disabled);
    }

    [Fact]
    public void EveryCategoryTheGamePersistsStateForIsWalked()
    {
        // The scope claim, asserted rather than described. The union includes every mission verb
        // reader plus main's raw-fact and structural owned-math readers.
        // the suite started with, four global-variable registries, twenty-six more the game persists
        // per-entity state for, the harvest elements' own resources — which are not in the resource
        // registry and would otherwise be reachable from nothing — the structure and upgrade cost
        // lists, the authored effects, each plot's authoring and each action's completion blocks,
        // each purchasable entity's lifecycle-authored per-level conditions, the authored link from
        // each retired unlocker glyph to the Recipe Book it is the internal half of, the authored
        // link from each Recipe Book to the discovery trees it widens,
        // prerequisite links' volatile native gates, and crafting
        // recipes' separately refreshed live state and player-action decisions, which are second
        // walks of cached lifecycle bindings rather than registries of their own, the challenge
        // decision context captured from its managers in the same frame, the
        // plot-and-action pairs, which belong to neither side, the authored keyword membership of the
        // thirteen classes whose type lists no other category binds — one walk rather than thirteen,
        // because the word line is one player concept spanning all of them — and
        // the two that belong to no per-type registry at all and are reached by uuid: the action
        // queues, the equipped spell loadout, the paired Concept registries, and the current
        // targeting request, plus the two ordered consumable lists, their frame-local use gate, and
        // the nine type rosters, whose
        // modifier records were already walked but whose assets had no row of their own, and the
        // six base agromancy verbs, whose keyword rows were walked while the assets a members line
        // counts had no row to be reached at, and the game's own statistic glossary, the words it
        // prints above its numbers, which were readable only by hovering something that showed one,
        // and finally the ritual layer's own glossary — the ten authored vocabularies the combat,
        // enchanting and rune screens are written in — plus the Statistics tab's headings and the
        // authored distribution each heading carries, and the resource panel's draw order.
        // A pass that quietly stopped covering one would show
        // up only as a consumer finding nothing where there was something.
        var report = Collector().Collect();

        Assert.Equal(89, report.Categories.Length);
        Assert.True(report.IsComplete, report.Describe());

        // A few named explicitly, one per shape: a mastery track, a state machine, a lone flag, and a
        // levelled grouping type.
        foreach (var category in
                 new[] { "resources", "harvest actions", "harvest resources", "harvest lifecycle", "time runes", "challenges", "challenge decisions", "views", "purchase view relations", "resource types", "crafting recipes", "crafting recipe state", "crafting decisions", "recipe books", "modifier variables", "structure costs", "upgrade costs", "plot actions", "action queues", "spell slots", "spell workbench", "spell authored graph", "ordinary alchemy loadout", "concept instances", "loadouts", "targeting", "consumable inventory", "plot authoring", "effect blocks", "entity requirements", "recipe book glyphs", "prerequisite link states", "entity keywords", "type modifiers", "type modifier contributions", "structure types", "ritual types", "harvest types", "plot node types", "research types", "consumable families", "harvest action types", "passive ability types", "time rune types", "statistics", "status effects", "character attributes", "damage types", "character modifiers", "character actions", "character types", "enchantments", "glyph types", "rune stones", "display types", "attribute groups", "attribute group members" })
        {
            Assert.Equal(WorldCategoryOutcome.Collected, report.For(category).Outcome);
        }
    }

    /// <summary>
    /// A harvest element that throws after an earlier one was read withdraws the whole category, so
    /// no element reads as one the player cannot add.
    /// </summary>
    [Fact]
    public void A_harvest_lifecycle_read_that_throws_part_way_leaves_no_controls_behind()
    {
        HarvestElementSO.All.Add(new HarvestElementSO());
        HarvestElementSO.All.Add(new HarvestElementSO { throwsOnMaximum = true });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("harvest lifecycle").Outcome);
        Assert.Equal(0, world.HarvestElementControls.Count);
    }

    [Fact]
    public void ACategoryWithNothingToShowIsStillWalked()
    {
        // Empty is a fact about the save, not about the read, and the two must stay distinguishable.
        // A category that read cleanly and found nothing is complete; one that could not be read is
        // not, and only the latter should ever make a consumer doubt its own emptiness.
        var report = Collector().Collect();
        var glyphs = report.For("augment glyphs");

        Assert.Equal(WorldCategoryOutcome.Collected, glyphs.Outcome);
        Assert.Equal(0, glyphs.Sampled);
        Assert.True(glyphs.IsClean);
        Assert.Empty(glyphs.FirstFailure);
    }

    [Fact]
    public void TheGameTypesBindThroughTheDefaultConstructor()
    {
        // Resolution by name is how this reaches the real assemblies, and the stubs carry the same
        // member names. If a member a binder needs disappears, this is the cheap early warning; the
        // shipped assemblies are checked separately by the contract manifest.
        var collector = new GameWorldCollector();
        var report = collector.Collect();
        Assert.True(collector.IsFullyAvailable, report.Describe());
        var recipe = new SpellRecipeSO { baseLevelingCost = AuthoredSpellLevelCost() };
        Assert.Equal(recipe.GetGuid(), Guid.Parse(recipe.uuid));

        Assert.Same(typeof(AlchemyTypeSO), WorldNativeTypes.Resolve("AlchemyTypeSO"));
        var type = new AlchemyTypeSO();
        Assert.Equal(type.GetGuid(), Guid.Parse(type.uuid));
        var fromIdentity = Guid.NewGuid();
        type.Identity = fromIdentity;
        Assert.Equal(fromIdentity, type.GetGuid());
        Assert.Equal(fromIdentity, Guid.Parse(type.uuid));
        var fromString = Guid.NewGuid();
        type.uuid = fromString.ToString("D");
        Assert.Equal(fromString, type.GetGuid());
        Assert.Equal(fromString, type.Identity);
        type.SetGuid(fromIdentity);
        Assert.Equal(fromIdentity, Guid.Parse(type.uuid));
}

    /// <summary>
    /// The frame globals bind by name like a category does, so a build that renamed one of their five
    /// accessors is the same shortfall. The reader answers with neutral terms instead of refusing, so
    /// the report must say so and name the accessor, or the run logs a complete world.
    /// </summary>
    [Fact]
    public void RenamingAFrameGlobalsAccessorMakesTheReportIncompleteAndNamesIt()
    {
        var collector = Collector(("Player", typeof(PlayerMissingQualityBonus)));
        var report = collector.Collect();

        Assert.False(collector.IsFullyAvailable);
        Assert.False(report.IsComplete);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("modifier folding").Outcome);
        Assert.Contains("GetAttributeQualityBonus", report.For("modifier folding").FirstFailure);
    }

    [Fact]
    public void AMissingCategoryDegradesOnlyItself()
    {
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid() });
        StructureSO.All.Add(new StructureSO { Identity = Guid.NewGuid() });
        UpgradeSO.All.Add(new UpgradeSO { Identity = Guid.NewGuid() });

        // A type that exists but exposes none of the members research needs.
        var collector = Collector(("ResearchSO", typeof(void)));
        var report = collector.Collect();
        var world = collector.Build();

        Assert.False(collector.IsFullyAvailable);
        Assert.False(report.IsComplete);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("research").Outcome);

        // The other three are untouched by the gap.
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(1, world.Structures.Count);
        Assert.Equal(1, world.Upgrades.Count);
        Assert.Equal(0, world.Research.Count);

        var described = report.Describe();
        Assert.Contains("research", described, StringComparison.Ordinal);
        Assert.DoesNotContain("resources", described, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAbsentTypeIsReportedAsSuchRatherThanAsAnEmptyCategory()
    {
        // "The type is gone" and "the type is here but a member moved" call for different fixes, so
        // the report has to tell them apart.
        var report = Collector(("ResearchSO", null)).Collect();

        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("research").Outcome);
        Assert.Contains(
            "ResearchSO type was not found", report.For("research").FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingMemberNamesTheMemberItCouldNotBind()
    {
        // Diagnosing a game update means knowing which member moved, not just that something did.
        var report = Collector(("ResourceSO", typeof(PartialResource))).Collect();
        var resources = report.For("resources");

        Assert.Equal(WorldCategoryOutcome.Unavailable, resources.Outcome);
        Assert.Contains("IsVisible", resources.FirstFailure, StringComparison.Ordinal);

        // A member that is present must not be blamed.
        Assert.DoesNotContain("GetQuantity", resources.FirstFailure, StringComparison.Ordinal);

        // A record read is blamed on the base value inside it, because the record is usually what is
        // missing rather than the whole entity — PartialResource still has maxQuantity, just not a
        // record named quality.
        Assert.Contains("quality.baseValue", resources.FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ACategoryTheCollectorNeverWalkedAnswersAsUnavailable()
    {
        // Asking about a category that is not in the pass is a caller mistake worth an answer rather
        // than an exception — the report is a diagnostic, and one that threw would be consulted less.
        var absent = Collector().Collect().For("weather");

        Assert.Equal(WorldCategoryOutcome.Unavailable, absent.Outcome);
        Assert.Contains("not collected", absent.FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ARepeatedIdentityCostsOneRowRatherThanTheSnapshot()
    {
        // Table construction treats a duplicate as a hard authoring error, so the collector has to
        // catch it first — otherwise one bad registry entry throws away every other reading in the
        // pass.
        var shared = Guid.NewGuid();
        ResourceSO.All.Add(new ResourceSO { Identity = shared, Quantity = 1d });
        ResourceSO.All.Add(new ResourceSO { Identity = shared, Quantity = 2d });

        var collector = Collector();
        var report = collector.Collect();
        var resources = report.For("resources");

        Assert.Equal(1, resources.Sampled);
        Assert.Equal(1, resources.Skipped);
        Assert.Contains(shared.ToString(), resources.FirstFailure, StringComparison.Ordinal);
        Assert.False(report.IsComplete);

        // The first reading wins; the pass does not silently prefer the last writer.
        var world = collector.Build();
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(1d, world.Resources[0].Reading.Quantity.ToDouble());
    }

    [Fact]
    public void AnIdentityCollisionAcrossCategoriesIsAlsoRejected()
    {
        // The game keys every entity in one UUID space, so a structure sharing a resource's identity
        // means the game's own RuntimeLookup is already broken. Accepting it would let a lookup
        // return whichever row sorted first.
        var shared = Guid.NewGuid();
        ResourceSO.All.Add(new ResourceSO { Identity = shared });
        StructureSO.All.Add(new StructureSO { Identity = shared });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(0, world.Structures.Count);
        Assert.Equal(1, report.For("attributes").Skipped);
    }

    [Fact]
    public void AnUnidentifiedEntityIsSkipped()
    {
        StructureSO.All.Add(new StructureSO { Identity = Guid.Empty });
        StructureSO.All.Add(new StructureSO { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();

        Assert.Equal(1, collector.Build().Structures.Count);
        Assert.Equal(1, report.For("attributes").Skipped);
        Assert.Contains("empty identity", report.For("attributes").FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullRegistryEntryIsSkipped()
    {
        UpgradeSO.All.Add(null!);
        UpgradeSO.All.Add(new UpgradeSO { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();

        Assert.Equal(1, collector.Build().Upgrades.Count);
        Assert.Equal(1, report.For("upgrades").Skipped);
    }

    [Fact]
    public void AnExpectedNativeFailureCostsOneRowAndTheRestOfThePassPublishes()
    {
        using var faults = new OrbModding.TestSupport.CapturedGameActionFaultLog();
        var failure = new InvalidOperationException("quantity unavailable");
        ResourceSO.All.Add(new ResourceSO { QuantityFailure = failure });
        ResourceSO.All.Add(new ResourceSO { Quantity = 7d });
        StructureSO.All.Add(new StructureSO { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();
        var resources = report.For("resources");

        Assert.Equal(WorldCategoryOutcome.Collected, resources.Outcome);
        Assert.Equal(1, resources.Skipped);
        Assert.Equal("reading a ResourceSO threw", resources.Failure.Reason.Sentence);
        faults.AssertLogged(resources.Failure.Reason, failure);
        Assert.Contains("Reference " + resources.Failure.Reason.Reference, resources.FirstFailure);
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(7d, world.Resources[0].Reading.Quantity.ToDouble());
        Assert.Equal(1, world.Structures.Count);
    }

    [Fact]
    public void AnUnexpectedThrowInABinderCategoryFaultsThePass()
    {
        var failure = new NullReferenceException("resource was destroyed");
        ResourceSO.All.Add(new ResourceSO { QuantityFailure = failure });
        ResourceSO.All.Add(new ResourceSO { Quantity = 7d });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void TheUnusedNativeRateAnswerIsNeverInvoked()
    {
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid(), ThrowOnRate = true });
        ResourceSO.All.Add(new ResourceSO { Identity = Guid.NewGuid(), Quantity = 7d });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(2, world.Resources.Count);
        Assert.Equal(0, report.For("resources").Skipped);
    }

    [Fact]
    public void CollectingTwiceReplacesTheReadingRatherThanAppendingToIt()
    {
        // The buffers are reused across collections. A missed reset would grow the snapshot every
        // cycle and immediately trip the duplicate check in table construction.
        var mana = Guid.NewGuid();
        ResourceSO.All.Add(new ResourceSO { Identity = mana, Quantity = 1d });

        var harvest = new PlotNodeActionSO();
        PlotNodeActionSO.All.Add(harvest);
        PlotNodeSO.All.Add(new PlotNodeSO().With(PlotNodePhases.Idle, 1).Offering(harvest));

        var collector = Collector();

        collector.Collect();
        Assert.Equal(1, collector.Build().Resources.Count);

        ResourceSO.All[0].Quantity = 99d;
        collector.Collect();

        var world = collector.Build();
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(99d, world.Resources[0].Reading.Quantity.ToDouble());

        // The pair table has no identity to collide on, and its own reader folds a repeat into the
        // row already there, so a missed reset would not even change the row count — it would quietly
        // report the plot offering the action once more on every cycle it survived.
        Assert.Equal(1, world.PlotActions.Count);
        Assert.True(WorldPlotActionLookup.TryFind(
            world.PlotActions, PlotNodeSO.All[0].Identity, harvest.Identity, out var pair));
        Assert.Equal(1, pair.Reading.OfferedCount);
    }

    [Fact]
    public void APassBecomesASnapshotEveryConsumerCanLookUp()
    {
        // The end-to-end shape: main thread grabs, worker derives, consumers binary-search. This is
        // the substitution the whole design exists to make possible.
        var mana = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        ResourceSO.All.Add(new ResourceSO
        {
            Identity = mana,
            Quantity = 60d,
            maxQuantity = new ValueModifierRecord(100d),
        });
        StructureSO.All.Add(new StructureSO { Identity = cauldron, Level = 4, Queued = 1 });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var resource));
        Assert.True(resource.IsCapped);
        Assert.Equal(40d, resource.Headroom.ToDouble());

        Assert.True(WorldLookup.TryFind(world.Structures, cauldron, out var structure));
        Assert.Equal(5d, structure.CommittedLevel.ToDouble());

        Assert.False(WorldLookup.TryFind(world.Upgrades, Guid.NewGuid(), out _));
    }

    [Fact]
    public void APassGrowsPastItsInitialBufferWithoutLosingReadings()
    {
        var identities = new Guid[200];
        for (var index = 0; index < identities.Length; index++)
        {
            identities[index] = Guid.NewGuid();
            StructureSO.All.Add(new StructureSO { Identity = identities[index], Level = index });
        }

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(200, world.Structures.Count);
        Assert.Equal(200, report.For("attributes").Sampled);

        // Rows are ordered by identity rather than by registry position, so every reading is checked
        // through the lookup that consumers actually use.
        for (var index = 0; index < identities.Length; index++)
        {
            Assert.True(WorldLookup.TryFind(world.Structures, identities[index], out var structure));
            Assert.Equal(index, (int)structure.Reading.Level.ToDouble());
        }
    }

    /// <summary>
    /// The tick's fixed timestep is read during collection, not during derivation:
    /// <c>Time.fixedDeltaTime</c> is main-thread-only and Build is the half that may run anywhere.
    /// </summary>
    [Fact]
    public void TheSnapshotCarriesTheTimestepReadAtCollectionTime()
    {
        var reads = 0;
        var collector = new GameWorldCollector(
            WorldNativeTypes.Resolve,
            () =>
            {
                reads++;
                return 0.02d;
            });

        Assert.Equal(0, reads);

        collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, reads);
        Assert.Equal(0.02d, world.FixedDeltaTime);
    }
}

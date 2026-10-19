using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// The collector is the one place the suite touches the game for world state, so its failure
/// behaviour matters more than its happy path. These tests pin that a shortfall is always visible: a
/// category that cannot be read says so, an entity that cannot be read costs one row, and nothing
/// ever reports a clean pass it did not achieve.
/// </summary>
/// <remarks>
/// Most tests drive purpose-built types rather than the shared game stubs. Those types spell out the
/// exact member shape each binder requires, which makes <c>WorldCollectorFakes.cs</c> a readable
/// statement of the contract, and they keep their registries private so no other test class can be
/// perturbed by one running here. One test does bind against the stubs, to prove the by-name
/// resolution path works end to end; the real member names are validated separately against the
/// shipped assemblies by the native contract manifest.
/// </remarks>
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

        FakeResource.All.Add(new FakeResource
        {
            Identity = mana,
            Quantity = 60d,
            maxQuantity = new FakeModifierRecord(100d),
            rate = new FakeModifierRecord(2.5d, activeCount: 1),
            Visible = true,
        });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            structureType = new FakeStructureType
            {
                Identity = scholar,
                structures = FakeStructure.All,
            },
            Level = 12,
            Queued = 3,
            Available = true,
        });
        FakeUpgrade.All.Add(new FakeUpgrade { Identity = alchemy, Level = 1, maxLevel = 1, Available = false });
        FakeResearch.All.Add(new FakeResearch { Identity = theory, level = 4, isDeveloping = true, Available = true });
        FakeRecipeBook.All.Add(new FakeRecipeBook { Identity = grimoire, Available = true });

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
        FakeStructure.All.Add(new FakeStructure { Identity = active, disabled = false });
        FakeStructure.All.Add(new FakeStructure { Identity = disabled, disabled = true });

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
        FakeHarvestElement.All.Add(new FakeHarvestElement());
        FakeHarvestElement.All.Add(new FakeHarvestElement { throwsOnMaximum = true });

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
    public void AStateEnumTravelsAsItsUnderlyingInteger()
    {
        // The suite deliberately does not mirror the game's enums. A copied enum would keep compiling
        // against a build that inserted a member in the middle, and every comparison against it would
        // silently start meaning something else. The integer is what the game persists.
        var challenge = Guid.NewGuid();
        FakeChallenge.All.Add(new FakeChallenge
        {
            Identity = challenge,
            level = 3,
            state = FakeState.Done,
            hasBeenSeen = true,
            rewardQueued = true,
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.Challenges, challenge, out var row));
        Assert.Equal((int)FakeState.Done, row.State);
        Assert.Equal(3, row.Level);
        Assert.True(row.Seen);
        Assert.True(row.RewardQueued);
    }

    [Fact]
    public void AnUncappedResourceKeepsTheGamesNegativeSentinel()
    {
        // The collector must not normalise the game's "no ceiling" marker into something friendlier.
        // Negative is what HasMaxQuantity() tests for, and the deriver reads the same convention.
        FakeResource.All.Add(new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = 1e9d,
            maxQuantity = new FakeModifierRecord(-1d),
        });

        var collector = Collector();
        collector.Collect();

        var resource = collector.Build().Resources[0];
        Assert.True(resource.Reading.Capacity.ToDouble() < 0d);
        Assert.False(resource.IsCapped);
    }

    [Fact]
    public void CollectingNeverAsksTheGameToRecalculateACachedValue()
    {
        // The load-bearing property of the whole collector. ValueModifierRecord.GetValue() is not a
        // read: on a dirty record it runs an allocating pass over both modifier dictionaries and
        // writes calculatedValue, calculationDirty, observedId, and updateObservers. Calling it would
        // make the game recompute and re-stamp its own observable at whatever point in the frame the
        // suite's pump happens to run, which is a mutation the suite has no business performing
        // outside the action boundary.
        //
        // The collector reproduces GetValue() from the record's own fields instead — its memo when it
        // is clean, its base value and modifier sets when it is dirty — which asks the game nothing
        // and still answers what GetValue() would have.
        var record = new FakeModifierRecord(250d);
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        var report = collector.Collect();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(250d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void CollectingIgnoresACacheTheGameHasNotFilled()
    {
        // The cycle-1 defect, at the level it was introduced. calculatedValue is [NonSerialized], so
        // a record comes back from a save at zero, and one that is dirty with it is one the game will
        // recompute the moment anything asks. Publishing that zero is what priced 180 structures at
        // nothing.
        var record = new FakeModifierRecord(250d).WithStaleCache(0d).Dirty();
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        Assert.Equal(250d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    /// <summary>
    /// The other half of the same rule, and the one that cost every structure price. A clean record's
    /// memo is not a stale number the game is about to replace — nothing will dirty it, so nothing
    /// will recompute it, and the memo is what the game charges from for the rest of the session.
    /// Publishing a recomputation instead would be publishing a number the game never uses.
    /// </summary>
    [Fact]
    public void CollectingReadsTheMemoTheGameWillNotRecompute()
    {
        var record = new FakeModifierRecord(250d).WithStaleCache(0d);
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        Assert.False(record.IsCalculationDirty);
        Assert.Equal(0d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
    }

    [Fact]
    public void CollectingFoldsTheModifiersOnARecordRatherThanReadingItsValue()
    {
        var record = new FakeModifierRecord(100d).WithStaleCache(999d).Dirty();
        record.passiveModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.Raw, new BigDouble(50d));
        record.activeModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.MultiStacking, new BigDouble(2d), order: 1);
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), maxQuantity = record });

        var collector = Collector();
        collector.Collect();

        // (100 + 50) * 2 = 300 — both sets, lowest order first, and nothing from the cache.
        Assert.Equal(300d, collector.Build().Resources[0].Reading.Capacity.ToDouble());
        Assert.Equal(0, record.GetValueCalls);
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
    }

    /// <summary>
    /// The frame globals bind by name like a category does, so a build that renamed one of their five
    /// accessors is the same shortfall. The reader answers with neutral terms instead of refusing, so
    /// the report must say so and name the accessor, or the run logs a complete world.
    /// </summary>
    [Fact]
    public void RenamingAFrameGlobalsAccessorMakesTheReportIncompleteAndNamesIt()
    {
        var collector = Collector(("Player", typeof(FakePlayerGlobalsMissingTheQualityBonus)));
        var report = collector.Collect();

        Assert.False(collector.IsFullyAvailable);
        Assert.False(report.IsComplete);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("modifier folding").Outcome);
        Assert.Contains("GetAttributeQualityBonus", report.For("modifier folding").FirstFailure);
    }

    [Fact]
    public void AnUpgradeWhoseRewardHasNoFreePedestalPublishesItsDestinationAsFull()
    {
        var pedestals = new FakeViewList { room = false };
        var aspect = new FakeUpgrade { Identity = Guid.NewGuid() };
        aspect.viewListAdditions.Add(new FakeViewListTuple { list = pedestals });
        var space = new FakeUpgrade { Identity = Guid.NewGuid() };
        FakeUpgrade.All.Add(aspect);
        FakeUpgrade.All.Add(space);

        var collector = Collector();
        var report = collector.Collect();
        var full = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(full.Upgrades, aspect.Identity, out var aspectRow));
        Assert.True(aspectRow.Reading.RewardDestinationFull);
        Assert.True(WorldLookup.TryFind(full.Upgrades, space.Identity, out var spaceRow));
        Assert.False(spaceRow.Reading.RewardDestinationFull);

        pedestals.room = true;
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Upgrades, aspect.Identity, out aspectRow));
        Assert.False(aspectRow.Reading.RewardDestinationFull);
    }

    [Fact]
    public void AMissingCategoryDegradesOnlyItself()
    {
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid() });
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.NewGuid() });
        FakeUpgrade.All.Add(new FakeUpgrade { Identity = Guid.NewGuid() });

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
        FakeResource.All.Add(new FakeResource { Identity = shared, Quantity = 1d });
        FakeResource.All.Add(new FakeResource { Identity = shared, Quantity = 2d });

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
        FakeResource.All.Add(new FakeResource { Identity = shared });
        FakeStructure.All.Add(new FakeStructure { Identity = shared });

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
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.Empty });
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();

        Assert.Equal(1, collector.Build().Structures.Count);
        Assert.Equal(1, report.For("attributes").Skipped);
        Assert.Contains("empty identity", report.For("attributes").FirstFailure, StringComparison.Ordinal);
    }

    [Fact]
    public void ANullRegistryEntryIsSkipped()
    {
        FakeUpgrade.All.Add(null!);
        FakeUpgrade.All.Add(new FakeUpgrade { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();

        Assert.Equal(1, collector.Build().Upgrades.Count);
        Assert.Equal(1, report.For("upgrades").Skipped);
    }

    [Fact]
    public void AnExpectedNativeFailureCostsOneRowAndTheRestOfThePassPublishes()
    {
        FakeResource.All.Add(new FakeResource { QuantityFailure = new InvalidOperationException("quantity unavailable") });
        FakeResource.All.Add(new FakeResource { Quantity = 7d });
        FakeStructure.All.Add(new FakeStructure { Identity = Guid.NewGuid() });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();
        var resources = report.For("resources");

        Assert.Equal(WorldCategoryOutcome.Collected, resources.Outcome);
        Assert.Equal(1, resources.Skipped);
        Assert.Equal("reading a ResourceSO threw: quantity unavailable", resources.FirstFailure);
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(7d, world.Resources[0].Reading.Quantity.ToDouble());
        Assert.Equal(1, world.Structures.Count);
    }

    [Fact]
    public void AnUnexpectedThrowInABinderCategoryFaultsThePass()
    {
        var failure = new NullReferenceException("resource was destroyed");
        FakeResource.All.Add(new FakeResource { QuantityFailure = failure });
        FakeResource.All.Add(new FakeResource { Quantity = 7d });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void TheUnusedNativeRateAnswerIsNeverInvoked()
    {
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), ThrowOnRate = true });
        FakeResource.All.Add(new FakeResource { Identity = Guid.NewGuid(), Quantity = 7d });

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
        FakeResource.All.Add(new FakeResource { Identity = mana, Quantity = 1d });

        var harvest = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(harvest);
        FakePlotNode.All.Add(new FakePlotNode().With(FakePlotPhase.Idle, 1).Offering(harvest));

        var collector = Collector();

        collector.Collect();
        Assert.Equal(1, collector.Build().Resources.Count);

        FakeResource.All[0].Quantity = 99d;
        collector.Collect();

        var world = collector.Build();
        Assert.Equal(1, world.Resources.Count);
        Assert.Equal(99d, world.Resources[0].Reading.Quantity.ToDouble());

        // The pair table has no identity to collide on, and its own reader folds a repeat into the
        // row already there, so a missed reset would not even change the row count — it would quietly
        // report the plot offering the action once more on every cycle it survived.
        Assert.Equal(1, world.PlotActions.Count);
        Assert.True(WorldPlotActionLookup.TryFind(
            world.PlotActions, FakePlotNode.All[0].Identity, harvest.Identity, out var pair));
        Assert.Equal(1, pair.Reading.OfferedCount);
    }

    [Fact]
    public void APassBecomesASnapshotEveryConsumerCanLookUp()
    {
        // The end-to-end shape: main thread grabs, worker derives, consumers binary-search. This is
        // the substitution the whole design exists to make possible.
        var mana = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource
        {
            Identity = mana,
            Quantity = 60d,
            maxQuantity = new FakeModifierRecord(100d),
        });
        FakeStructure.All.Add(new FakeStructure { Identity = cauldron, Level = 4, Queued = 1 });

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
            FakeStructure.All.Add(new FakeStructure { Identity = identities[index], Level = index });
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
    /// A cost entry whose resource throws something the game is not known to throw faults the pass
    /// rather than costing its structure's row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAStructureCostFaultsThePass()
    {
        var failure = new NullReferenceException("cost resource was destroyed");
        var structure = new FakeStructure();
        structure.baseCost.costs.Add(new FakeCostEntry
        {
            resource = new FakeReferencedEntity { GetGuidFailure = failure },
            valueBig = new BigDouble(100d),
        });
        FakeStructure.All.Add(structure);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// An upgrade cost entry whose resource throws unexpectedly faults the pass rather than costing
    /// its upgrade's row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAnUpgradeCostFaultsThePass()
    {
        var failure = new NullReferenceException("cost resource was destroyed");
        var upgrade = new FakeUpgrade();
        upgrade.resourceCost.costs.Add(new FakeCostEntry
        {
            resource = new FakeReferencedEntity { GetGuidFailure = failure },
            valueBig = new BigDouble(50d),
        });
        FakeUpgrade.All.Add(upgrade);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A type's subtype that throws unexpectedly faults the pass rather than costing the type's
    /// modifier row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingATypeModifierRowFaultsThePass()
    {
        var failure = new NullReferenceException("subtype was destroyed");
        FakeStructureType.All.Add(new FakeStructureType
        {
            subTypes = { new FakeStructureType { GetGuidFailure = failure } },
        });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A modifier on a type whose source throws unexpectedly faults the pass rather than costing the
    /// type's contribution row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingATypeModifierContributionFaultsThePass()
    {
        var failure = new NullReferenceException("modifier source was destroyed");
        var type = new FakeStructureType();
        type.structurePower.activeModifiers[Guid.NewGuid()] = new FakeValueModifier(
            FakeModifierKind.Raw, 1d, 0, new global::IdScriptableObject { GetGuidFailure = failure });
        FakeStructureType.All.Add(type);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A keyword on an entity that throws unexpectedly faults the pass rather than costing the
    /// entity's keyword rows.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingAnEntityKeywordFaultsThePass()
    {
        var failure = new NullReferenceException("keyword was destroyed");
        FakeStructure.All.Add(new FakeStructure
        {
            structureSubTypes = { new FakeStructureType { GetGuidFailure = failure } },
        });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// The whole point of collecting the rate terms is that the ported chain can then run off the
    /// snapshot rather than calling <c>GetTrueRate()</c> on the Unity thread. That needs every
    /// argument, including the ones the game keeps private and the ones it only exposes as
    /// <c>HasActiveElements()</c>, which is an active-modifier count compared against zero.
    /// </summary>
    [Fact]
    public void AResourceCarriesEveryArgumentThePortedRateChainReads()
    {
        var mana = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = mana,
            rate = new FakeModifierRecord(12d, activeCount: 2),
            rateSplash = new FakeModifierRecord(3d, activeCount: 1),
            rateMaxPercent = new FakeModifierRecord(4d),
            rateInterestPercent = new FakeModifierRecord(5d),
            rateMissingPercent = new FakeModifierRecord(6d),
            rateLifetimePercent = new FakeModifierRecord(7d),
            lossPercent = new FakeModifierRecord(8d),
            displayRate = new FakeModifierRecord(9d),
            calcRarityValue = new BigDouble(2.5d),
            baseLoss = 0.5d,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Resources, mana, out var row));

        var inputs = row.Reading.RateInputs;
        Assert.Equal(12d, inputs.Rate.ToDouble());
        Assert.Equal(3d, inputs.RateSplash.ToDouble());
        Assert.Equal(4d, inputs.RateMaxPercent.ToDouble());
        Assert.Equal(5d, inputs.RateInterestPercent.ToDouble());
        Assert.Equal(6d, inputs.RateMissingPercent.ToDouble());
        Assert.Equal(7d, inputs.RateLifetimePercent.ToDouble());
        Assert.Equal(8d, inputs.LossPercent.ToDouble());
        Assert.Equal(9d, inputs.DisplayRate.ToDouble());
        Assert.Equal(2.5d, inputs.CalcRarityValue.ToDouble());
        Assert.Equal(0.5d, inputs.BaseLoss);

        // HasActiveElements() is this count against zero, and the chain branches on it — a term with
        // no active modifiers drops out rather than contributing zero.
        Assert.Equal(2, inputs.RateModifiers);
        Assert.Equal(1, inputs.RateSplashModifiers);
        Assert.Equal(0, inputs.RateMaxPercentModifiers);
        Assert.Equal(0, inputs.RateLifetimePercentModifiers);
    }

    /// <summary>
    /// The five grouping types shared one row while each was read for two numbers. They are not one
    /// shape: a spell type alone carries twenty-two cached records, so the shared row could only stay
    /// shared by continuing to leave them out.
    /// </summary>
    [Fact]
    public void EachGroupingTypeCarriesItsOwnState()
    {
        var spell = new FakeSpellType { typeLevel = 6, typeXp = new BigDouble(1200d) };
        spell.power = new FakeModifierRecord(250d);
        spell.critEffectMod = new FakeModifierRecord(180d);
        FakeSpellType.All.Add(spell);

        var resourceType = new FakeResourceType { level = 3, freeLevels = 1, ignoreAudit = true };
        FakeResourceType.All.Add(resourceType);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.SpellTypes, spell.Identity, out var type));
        Assert.Equal(6, type.TypeLevel);
        Assert.Equal(1200d, type.TypeXp.ToDouble());
        Assert.Equal(250d, type.Power.ToDouble());
        Assert.Equal(180d, type.CritEffectMod.ToDouble());

        Assert.True(WorldLookup.TryFind(world.ResourceTypes, resourceType.Identity, out var kind));
        Assert.Equal(3, kind.Level);
        Assert.Equal(1, kind.FreeLevels);
        Assert.True(kind.IgnoreAudit);
    }

    /// <summary>
    /// A harvest element owns a resource outright: it creates one with
    /// <c>ScriptableObject.CreateInstance</c>, never registers it, and marks it excluded from
    /// globals. The resource registry therefore cannot see it, and reading it through its owner is
    /// the only path — with the same member list every other resource is read with, so the two
    /// populations cannot drift apart.
    /// </summary>
    [Fact]
    public void AResourceAnElementOwnsIsReadThroughTheElement()
    {
        var element = new FakeHarvestElement();
        element.Resource.Quantity = new BigDouble(42d);
        element.Resource.maxQuantity = new FakeModifierRecord(100d);
        element.Resource.quality = new FakeModifierRecord(200d);
        FakeHarvestElement.All.Add(element);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(1, world.HarvestResources.Count);

        var row = world.HarvestResources[0];
        Assert.Equal(element.Identity, row.ElementId);
        Assert.Equal(42d, row.Resource.Reading.Quantity.ToDouble());

        // Derived exactly as any other resource row is — the quality scaling included.
        Assert.Equal(84d, row.Resource.TrueQuantity.ToDouble());
        Assert.True(row.Resource.IsCapped);

        // Its identity is the resource's own, not the element's: identity is claimed once across
        // every category, and the element has already claimed its own.
        Assert.NotEqual(element.Identity, row.EntityId);
        Assert.True(WorldLookup.TryFind(world.HarvestElements, element.Identity, out _));

        // And it stays out of the resource table, because the game keeps it out of the registry.
        Assert.Equal(0, world.Resources.Count);
    }

    /// <summary>
    /// The base agromancy verbs are a registry of their own, and every scalar the class stores is on
    /// the row: <c>HarvestActionSO.GetScalingInfo()</c> loads exactly <c>power</c>, <c>speed</c> and
    /// <c>costMod</c>, so a row that carried fewer would leave a bonus on this verb unreadable.
    /// </summary>
    [Fact]
    public void AHarvestActionPublishesTheThreeRecordsThatScaleIt()
    {
        var action = new FakeHarvestAction
        {
            power = new FakeModifierRecord(140d),
            speed = new FakeModifierRecord(110d),
            costMod = new FakeModifierRecord(90d),
        };
        FakeHarvestAction.All.Add(action);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(1, world.HarvestActions.Count);
        Assert.True(WorldLookup.TryFind(world.HarvestActions, action.Identity, out var row));
        Assert.Equal(140d, row.Power.ToDouble());
        Assert.Equal(110d, row.Speed.ToDouble());
        Assert.Equal(90d, row.CostMod.ToDouble());
    }

    [Fact]
    public void Harvest_element_and_action_controls_publish_active_counts_and_native_costs()
    {
        var resource = new FakeResource
        {
            Identity = Guid.NewGuid(),
            Quantity = new BigDouble(20),
            maxQuantity = new FakeModifierRecord(100d),
            Visible = true,
        };
        FakeResource.All.Add(resource);
        var element = new FakeHarvestElement { masteryLevel = 3, MaximumAdditional = 7 };
        element.Resource.maxQuantity = new FakeModifierRecord(100d);
        element.usageCost.costs.Add(new FakeCraftingResourceTuple
        {
            resource = resource,
            valueBig = new BigDouble(4),
        });
        var action = new FakeHarvestAction { NextDrainPercent = new BigDouble(150) };
        action.DrainCost.costs.Add(new FakeCraftingResourceTuple
        {
            resource = resource,
            valueBig = new BigDouble(3),
        });
        var prototype = new FakeHarvestActionInstance
        {
            Element = element,
            Action = action,
            Maximum = 4,
        };
        element.ActionInstances.Add(prototype);
        FakeHarvestElement.All.Add(element);
        WorldCategoryFakes.ActiveHarvestElements.SetStacks(element, 2);
        WorldCategoryFakes.ActiveHarvestActions.value.Add(new FakeHarvestActionInstance
        {
            Element = element,
            Action = action,
            Maximum = 4,
            instances = 1,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        var elementControl = Assert.Single(world.HarvestElementControls.AsSpan().ToArray());
        Assert.Equal(2, elementControl.Active);
        Assert.Equal(7, elementControl.MaximumAdditional);
        Assert.True(elementControl.AddAvailable);
        var actionControl = Assert.Single(world.HarvestActionControls.AsSpan().ToArray());
        Assert.Equal(action.Identity, actionControl.ActionId);
        Assert.Equal(1, actionControl.Active);
        Assert.Equal(4, actionControl.Maximum);
        Assert.True(actionControl.AddAvailable);
        Assert.Equal(2, world.HarvestLifecycleCosts.Count);
        Assert.Contains(world.HarvestLifecycleCosts.AsSpan().ToArray(), cost =>
            cost.Kind == WorldHarvestLifecycleCostKind.ElementUsage &&
            cost.Amount == new BigDouble(4));
        Assert.Contains(world.HarvestLifecycleCosts.AsSpan().ToArray(), cost =>
            cost.Kind == WorldHarvestLifecycleCostKind.NextActionDrain &&
            cost.Amount == new BigDouble(4.5));
    }

    /// <summary>
    /// A single-valued reference travels as the identity of what it points at, never as the object.
    /// The alchemy type's selected level is the case that motivated the rule: the game holds the
    /// choice in an <c>IntVariable</c> the global registry already collects, so building the row from
    /// the type's own fields found a reference, called it unpublishable, and dropped the edge — which
    /// left the snapshot unable to say which level was selected at all.
    /// </summary>
    [Fact]
    public void AReferenceToAnotherEntityTravelsAsThatEntitysIdentity()
    {
        var chosen = new FakeReferencedEntity();
        var rerolls = new FakeReferencedEntity();
        var choice = Guid.NewGuid();
        var levelling = new FakeReferencedEntity();

        var typeWithChoice = new FakeAlchemyType { selectedLevel = chosen };
        var typeWithout = new FakeAlchemyType();
        FakeAlchemyType.All.Add(typeWithChoice);
        FakeAlchemyType.All.Add(typeWithout);

        FakeDiscoveryTree.All.Add(new FakeDiscoveryTree
        {
            selectedChoiceId = new FakeGuidContainer(choice),
            overrideDiscoveryRerolls = rerolls,
        });

        var experience = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource
        {
            Identity = experience,
            appliedLevels = 14L,
            levelVariable = levelling,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, typeWithChoice.Identity, out var chose));
        Assert.Equal(chosen.Identity, chose.SelectedLevelId);

        // A type with no such choice reads as empty, which is the suite's own "no entity" and cannot
        // be mistaken for an edge — WorldTable refuses to admit a row keyed on it.
        Assert.True(WorldLookup.TryFind(world.AlchemyTypes, typeWithout.Identity, out var didNot));
        Assert.Equal(Guid.Empty, didNot.SelectedLevelId);

        Assert.Equal(1, world.DiscoveryTrees.Count);
        var tree = world.DiscoveryTrees[0];
        Assert.Equal(choice, tree.SelectedChoiceId);
        Assert.Equal(rerolls.Identity, tree.OverrideRerollsId);
        Assert.Equal(Guid.Empty, tree.OverrideChoicesId);

        Assert.True(WorldLookup.TryFind(world.Resources, experience, out var resource));
        Assert.Equal(14L, resource.Reading.AppliedLevels);
        Assert.Equal(levelling.Identity, resource.Reading.LevelVariableId);
    }

    [Fact]
    public void APinnedResourceThatThrowsUnexpectedlyFaultsTheWorldPass()
    {
        var pinned = (FakeSpellWeightList)FakeIdRegistry.RuntimeLookup[KnownEntities.ResourceBarCore.Uuid];
        pinned.value.Add(new FakeResource { GetGuidFailure = new NullReferenceException("pinned") });

        Assert.Throws<NullReferenceException>(() => Collector().Collect());
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
            name => Defaults.TryGetValue(name, out var type) ? type : null,
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

    /// <summary>
    /// A ritual that forces its level is fought and priced at that level, whatever its stored
    /// selection says: <c>RitualSO.GetCurrentLevel()</c> answers <c>forceLevelValue</c> there.
    /// Restoration stores 55 and runs at 70.
    /// </summary>
    [Fact]
    public void A_forced_ritual_is_priced_at_the_level_it_runs_at()
    {
        var knowledge = new FakeResource { Quantity = new BigDouble(80) };
        FakeResource.All.Add(knowledge);
        var restoration = new FakeRitual
        {
            Identity = Guid.NewGuid(),
            discovered = true,
            selectedLevel = 55,
            forceLevel = true,
            forceLevelValue = 70,
            maximumSelectedLevel = 71,
            activationCost = new FakeCraftingResourceCostList()
                .With(knowledge, new BigDouble(5)),
            completionCost = new FakeCraftingResourceCostList()
                .With(knowledge, new BigDouble(2)),
            completionCostMod = new ValueModifierRecord(new BigDouble(100d)).Dirty(),
        };
        restoration.completionCostPerLevel.variable!.value.modifiers.Add(
            new FakeValueModifier(FakeModifierKind.Raw, 1d, order: 0));
        FakeRitual.All.Add(restoration);
        FakeRitualManager.instance.selectedRitual.value = restoration;

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
        var knowledge = new FakeResource { Quantity = new BigDouble(80) };
        FakeResource.All.Add(knowledge);

        FakeRitual.All.Add(new FakeRitual
        {
            Identity = idle,
            discovered = true,
            durationRewardBlocks = { new FakeEffectBlock() },
            maximumSelectedLevel = 4,
            activationCost = new FakeCraftingResourceCostList()
                .With(knowledge, new BigDouble(11)),
        });
        var selected = new FakeRitual
        {
            Identity = ticking,
            discovered = true,
            inBattle = true,
            selectedLevel = 7,
            reachedLevel = 9,
            critLevel = 2,
            echoLevel = 1,
            chainLevel = 3,
            battleTotalWeight = new BigDouble(4200d),
            wavesCompleted = 3,
            baseWaves = 5,
            wavesPerLevel = 2,
            maxWaves = 30,
            ritualInstances = new List<object> { new(), new() },
            durationRewardBlocks = { new FakeEffectBlock() },
            currentSpoils =
            {
                new FakeSpoilsRecordEntry(knowledge, new BigDouble(12)),
            },
            maximumSelectedLevel = 12,
            activationCost = new FakeCraftingResourceCostList()
                .With(knowledge, new BigDouble(5)),
            completionCost = new FakeCraftingResourceCostList()
                .With(knowledge, new BigDouble(2)),
            completionCostMod = new ValueModifierRecord(new BigDouble(100d)).Dirty(),
        };
        selected.completionCostPerLevel.variable!.value.modifiers.Add(
            new FakeValueModifier(FakeModifierKind.Raw, 1d, order: 0));
        FakeRitual.All.Add(selected);
        FakeRitualManager.instance.selectedRitual.value = selected;

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

    /// <summary>
    /// Stock is the case that proves the point of D17: <c>ConsumableSO</c> keeps it in a private
    /// cached int, while the save record stores a list of per-level counts and rebuilds the total on
    /// load. Building the row from the save record left every item count out of the snapshot.
    /// </summary>
    [Fact]
    public void AConsumableCarriesItsStockEvenThoughTheSaveRecordDoesNot()
    {
        var potion = Guid.NewGuid();

        FakeConsumable.All.Add(new FakeConsumable
        {
            Identity = potion,
            visible = true,
            quantity = 5,
            queuedQuantity = 2,
            gainedSince = 3,
            maxCreatedLv = 4,
            maximumCarryLoad = 12,
            prepSpeed = new ValueModifierRecord(new BigDouble(150d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Consumables, potion, out var row));
        Assert.True(row.Visible);
        Assert.Equal(5, row.Quantity);
        Assert.Equal(2, row.QueuedQuantity);
        Assert.Equal(3, row.GainedSince);
        Assert.Equal(4, row.MaxCreatedLevel);
        Assert.Equal(12, row.MaximumCarryLoad);
        Assert.Equal(150d, row.Modifiers.PrepSpeed.ToDouble());
    }

    /// <summary>
    /// A consumable whose read throws anything but an expected native failure faults the pass instead
    /// of costing one consumable.
    /// </summary>
    [Fact]
    public void AConsumableWhoseReadThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("consumable was destroyed");
        FakeConsumable.All.Add(new FakeConsumable { GetGuidFailure = failure });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// An inventory slot whose read throws anything but an expected native failure faults the pass
    /// instead of costing the inventory.
    /// </summary>
    [Fact]
    public void AnInventorySlotWhoseReadThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("slot consumable was destroyed");
        FakeConsumableInventory._instance.allConsumables.value.Add(
            new FakeConsumable { GetGuidFailure = failure });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    [Fact]
    public void ConsumableInventoryPublishesBothOrderedListsAndFrameLocalUseAdmission()
    {
        var first = new FakeConsumable { Identity = Guid.NewGuid(), visible = true, quantity = 2 };
        var second = new FakeConsumable { Identity = Guid.NewGuid(), visible = true, quantity = 1 };
        FakeConsumable.All.Add(first);
        FakeConsumable.All.Add(second);
        FakeConsumableInventory._instance.allConsumables.value.Add(first);
        FakeConsumableInventory._instance.allConsumables.value.Add(null);
        FakeConsumableInventory._instance.allConsumables.value.Add(second);
        FakeConsumableInventory._instance.allConsumables.Maximum = 12;
        FakeConsumableInventory._instance.hotBar.value.Add(second);
        FakeConsumableInventory._instance.hotBar.Maximum = 4;
        FakeConsumableInventory.CanUse = false;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.False(world.ConsumableInventory.CanUse);
        Assert.Equal(12, world.ConsumableInventory.InventoryMaximum);
        Assert.Equal(4, world.ConsumableInventory.HotbarMaximum);
        var slots = world.ConsumableInventory.Slots.AsSpan().ToArray();
        Assert.Equal(4, slots.Length);
        Assert.Equal(
            new[] { first.Identity, Guid.Empty, second.Identity, second.Identity },
            slots.Select(slot => slot.ConsumableId));
        Assert.Equal(
            new[]
            {
                WorldConsumableListKind.Inventory,
                WorldConsumableListKind.Inventory,
                WorldConsumableListKind.Inventory,
                WorldConsumableListKind.Hotbar,
            },
            slots.Select(slot => slot.List));
        Assert.Equal(4, report.For("consumable inventory").Sampled);
    }

    [Fact]
    public void AConsumablePublishesEveryNativeFamilyCostUsageAndCount()
    {
        var item = Guid.NewGuid();
        var extraFamily = Guid.NewGuid();
        var toxicity = KnownEntities.PotionToxicity.Uuid;
        var durationResource = Guid.NewGuid();
        var consumable = new FakeConsumable { Identity = item, quantity = 1 };
        consumable.consumableTypes.Add(
            new FakeConsumableType { Identity = KnownEntities.ConsumableScrollType.Uuid });
        consumable.consumableTypes.Add(new FakeConsumableType { Identity = extraFamily });
        consumable.consumeCost.costs.Add(new FakeConsumableCost(toxicity, 2d));
        consumable.usageCost.costs.Add(new FakeConsumableCost(durationResource, 3d));
        var usage = new FakeConsumableUsage
        {
            baseSi = new FakeConsumableScalingInfo { Level = 7 },
            en = true,
            dr = new BigDouble(11d),
            maxDr = new BigDouble(12d),
        };
        consumable.consumableUsages.Add(usage);
        consumable.consumableCounts.Add(new FakeConsumableCount
        {
            Level = 7,
            Quantity = 4,
            fr = 3,
        });
        FakeConsumable.All.Add(consumable);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldConsumableTypeLookup.TryFindRange(
            world.ConsumableTypes, item, out var typeStart, out var typeCount));
        Assert.Equal(2, typeCount);
        var publishedTypes = new HashSet<Guid>();
        for (var index = 0; index < typeCount; index++)
            publishedTypes.Add(world.ConsumableTypes[typeStart + index].TypeId);
        Assert.Contains(KnownEntities.ConsumableScrollType.Uuid, publishedTypes);
        Assert.Contains(extraFamily, publishedTypes);

        Assert.True(WorldConsumableCostLookup.TryFindRange(
            world.ConsumableCosts,
            item,
            WorldConsumableCostKind.Consume,
            out var consumeStart,
            out var consumeCount));
        Assert.Equal(1, consumeCount);
        Assert.Equal(toxicity, world.ConsumableCosts[consumeStart].ResourceId);
        Assert.Equal(2d, world.ConsumableCosts[consumeStart].Amount.ToDouble());

        Assert.True(WorldConsumableUsageLookup.TryFindRange(
            world.ConsumableUsages, item, out var usageStart, out var usageCount));
        Assert.Equal(1, usageCount);
        Assert.Equal(usage.Identity, world.ConsumableUsages[usageStart].UsageId);
        Assert.Equal(7, world.ConsumableUsages[usageStart].Level);
        Assert.True(world.ConsumableUsages[usageStart].Engaged);

        Assert.True(WorldConsumableCountLookup.TryFindRange(
            world.ConsumableCounts, item, out var countStart, out var countCount));
        Assert.Equal(1, countCount);
        Assert.Equal(7, world.ConsumableCounts[countStart].Level);
        Assert.Equal(4, world.ConsumableCounts[countStart].Quantity);
        Assert.Equal(3, world.ConsumableCounts[countStart].FreeQuantity);
    }

    /// <summary>
    /// Whether an item can be fired is composed from the four terms the world already holds, not
    /// asked of the game.
    /// </summary>
    /// <remarks>
    /// <c>ConsumableSO.CanFire()</c> is
    /// <c>!IsOnCooldown() &amp;&amp; HasEnoughUsage() &amp;&amp; HasEnoughCost() &amp;&amp; quantity &gt; 0</c>, and each
    /// case here holds three of those terms and breaks the fourth, so a composition that dropped
    /// any one of them would publish the wrong verdict for exactly one case.
    /// </remarks>
    [Theory]
    [InlineData(1, 0d, 500d, 500d, true)]
    [InlineData(0, 0d, 500d, 500d, false)]
    [InlineData(1, 4d, 500d, 500d, false)]
    [InlineData(1, 0d, 1d, 500d, false)]
    [InlineData(1, 0d, 500d, 1d, false)]
    public void WhetherAnItemCanFireIsComposedFromTheTermsTheWorldAlreadyHolds(
        int quantity,
        double cooldown,
        double consumeStock,
        double usageStock,
        bool expected)
    {
        var item = Guid.NewGuid();
        var consumeResource = Guid.NewGuid();
        var usageResource = Guid.NewGuid();
        FakeResource.All.Add(new FakeResource
        {
            Identity = consumeResource,
            Quantity = new BigDouble(consumeStock),
        });
        FakeResource.All.Add(new FakeResource
        {
            Identity = usageResource,
            Quantity = new BigDouble(usageStock),
        });
        var consumable = new FakeConsumable
        {
            Identity = item,
            quantity = quantity,
            currentCooldown = new BigDouble(cooldown),
        };
        consumable.consumeCost.costs.Add(new FakeConsumableCost(consumeResource, 250d));
        consumable.usageCost.costs.Add(new FakeConsumableCost(usageResource, 250d));
        FakeConsumable.All.Add(consumable);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.Consumables, item, out var published));
        Assert.Equal(expected, published.CanFire);
    }

    /// <summary>
    /// A cost or family with no identity is unreadable, not absent: the whole consumable is skipped
    /// by name rather than published with a shorter price than the game charges.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AConsumableRelationWithoutAnIdentitySkipsTheWholeItem(bool cost)
    {
        var item = Guid.NewGuid();
        var consumable = new FakeConsumable { Identity = item, quantity = 1 };
        consumable.consumableTypes.Add(
            new FakeConsumableType { Identity = cost ? Guid.NewGuid() : Guid.Empty });
        consumable.consumeCost.costs.Add(new FakeConsumableCost(KnownEntities.PotionToxicity.Uuid, 2d));
        consumable.consumeCost.costs.Add(new FakeConsumableCost(cost ? Guid.Empty : Guid.NewGuid(), 5d));
        FakeConsumable.All.Add(consumable);

        var collector = Collector();
        var report = collector.Collect().For("consumables");
        var world = collector.Build();

        Assert.Equal(1, report.Skipped);
        Assert.Equal(
            cost
                ? "a consumable cost at position 1 was null or unidentified"
                : "a consumable type at position 0 was null or unidentified",
            report.FirstFailure);
        Assert.False(WorldLookup.TryFind(world.Consumables, item, out _));
        Assert.Equal(0, world.ConsumableCosts.Count);
    }

    [Fact]
    public void AnUnreadableConsumableRelationSkipsTheWholeItem()
    {
        var item = Guid.NewGuid();
        FakeConsumable.All.Add(new FakeConsumable
        {
            Identity = item,
            quantity = 1,
            consumableTypes = null!,
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.False(report.IsComplete);
        Assert.Contains("type list was null", report.For("consumables").FirstFailure);
        Assert.False(WorldLookup.TryFind(world.Consumables, item, out _));
        Assert.Equal(0, world.ConsumableTypes.Count);
        Assert.Equal(0, world.ConsumableCosts.Count);
        Assert.Equal(0, world.ConsumableUsages.Count);
        Assert.Equal(0, world.ConsumableCounts.Count);
    }

    /// <summary>
    /// A structure's price is computed from what was collected, not asked of the game.
    /// </summary>
    /// <remarks>
    /// Every term is distinct and away from parity, so a chain that dropped one moves the answer
    /// instead of landing on a multiplier of one that was going to be one anyway. Derived by hand from
    /// the decompiled original, not from the port:
    /// <list type="number">
    /// <item>attribute: <c>100 × 200% = 200</c></item>
    /// <item>per-quantity <c>Raw 0.5</c>, scaled by cost scaling <c>300%</c> and two committed levels,
    /// is <c>Raw 3</c>, and Raw adds: <c>200 + 3 = 203</c></item>
    /// <item>the multiplier is <c>Max(120, 100 / (1 + 0.5 × 2)) × ((150 × 200%) as percent)</c>
    /// = <c>Max(120, 50) × 3 = 360</c>, whose percent is <c>3.6</c></item>
    /// <item><c>203 × 3.6 = 730.8</c>, which RoundToTwoSigsEarly leaves alone above 100</item>
    /// </list>
    /// </remarks>
    [Fact]
    public void AStructuresPriceIsComputedFromWhatWasCollected()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakeCount.All.Add(new FakeCount
        {
            Identity = KnownEntities.BulkDevelopment.Uuid,
            value = new FakeModifierRecord(3d),
        });
        FakePlayerGlobals.SetStructureCost(200d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0.5d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            Level = 1,
            Queued = 1,
            passiveCostMod = new FakeModifierRecord(120d),
            activeCostMod = new FakeModifierRecord(150d),
            costScalingMod = new FakeModifierRecord(300d),
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out var count));
        Assert.Equal(1, count);

        var row = world.PurchaseCosts[start];
        Assert.Equal(water, row.ResourceId);
        Assert.Equal(100d, row.BaseExactAmount.ToDouble(), 6);
        Assert.Equal(730.8d, row.EffectiveExactAmount.ToDouble(), 6);
        Assert.Equal(730.8d, row.Amount.ToDouble(), 6);
        Assert.Equal(3, row.ExactGroupedLevels);
        Assert.Equal(2208.6d, row.ExactGroupedAmount.ToDouble(), 6);
        Assert.True(row.AffordabilityEvaluated);
        Assert.Equal(500d, row.AvailableAmount.ToDouble(), 6);
        Assert.Equal(730.8d, row.CombinedEffectiveAmount.ToDouble(), 6);
        Assert.False(row.ResourceAffordable);
        Assert.Equal("insufficient_quantity", row.ResourceAffordabilityReasonCode);
        Assert.False(row.Affordable);
        Assert.Equal("insufficient_quantity", row.AffordabilityReasonCode);

        var sources = row.ModifierSources.AsSpan().ToArray();
        Assert.Equal(10, sources.Length);
        Assert.Equal(
            new[]
            {
                "resource.attribute_cost_modifier",
                "resource.quality",
                "player.attribute_quality_bonus",
                "resource.effective_attribute_cost",
                "structure.cost_per_quantity",
                "structure.cost_scaling",
                "structure.passive_cost",
                "structure.active_cost",
                "player.structure_cost",
                "structure.committed_quantity",
            },
            sources.Select(source => source.Name).ToArray());
        var perQuantity = sources.Single(source => source.Name == "structure.cost_per_quantity");
        Assert.Equal(modifier.Identity, perQuantity.SourceId);
        Assert.Equal("ValueModifierVariable", perQuantity.SourceNativeType);
        Assert.True(perQuantity.HasModifierType);
        Assert.Equal((int)FakeModifierKind.Raw, perQuantity.ModifierType);
        Assert.Equal(0.5d, perQuantity.Value.ToDouble(), 6);
    }

    /// <summary>
    /// A structure whose per-quantity modifier does not resolve publishes no price at all.
    /// </summary>
    /// <remarks>
    /// The alternative — pricing it without the modifier — produces a number that is confidently too
    /// low, and Auto Buy cannot tell a cheap structure from a mispriced one. An absent price it can
    /// see and refuse to act on.
    /// </remarks>
    [Fact]
    public void AStructureWithNoResolvableScalingModifierIsNotPriced()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        // Reading the cost list still succeeded — the shortfall is in what could be derived from it,
        // which is why this is not a collection failure.
        Assert.True(report.IsComplete, report.Describe());
        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// A price paid in several resources is several rows, and the lookup returns all of them.
    /// </summary>
    /// <remarks>
    /// The range is the whole point of the table's shape: a binary search that landed anywhere inside
    /// an entity's rows and walked forward would report a partial price, and a consumer checking
    /// affordability against a partial price buys things it cannot pay for.
    /// </remarks>
    [Fact]
    public void AMultiResourcePriceIsReturnedWhole()
    {
        var water = Guid.NewGuid();
        var mana = Guid.NewGuid();
        var stone = Guid.NewGuid();
        var perQuantity = Guid.NewGuid();

        foreach (var resource in new[] { water, mana, stone })
            FakeResource.All.Add(new FakeResource { Identity = resource, Quantity = 500d });

        var modifier = new FakeModifierVariable
        {
            Identity = perQuantity,
            value = new FakeValueModifier(FakeModifierKind.Raw, 0d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);

        // Two structures, so the search has to find the right one's first row rather than the table's.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = first,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d), (mana, 200d), (stone, 300d)),
        });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = second,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 400d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, first, out var start, out var count));
        Assert.Equal(3, count);

        var total = 0d;
        for (var index = start; index < start + count; index++)
        {
            Assert.Equal(first, world.PurchaseCosts[index].EntityId);
            total += world.PurchaseCosts[index].Amount.ToDouble();
        }

        Assert.Equal(600d, total, 6);

        for (var index = start; index < start + count; index++)
        {
            Assert.True(world.PurchaseCosts[index].AffordabilityEvaluated);
            Assert.True(world.PurchaseCosts[index].ResourceAffordable);
            Assert.True(world.PurchaseCosts[index].Affordable);
            Assert.Equal("affordable", world.PurchaseCosts[index].AffordabilityReasonCode);
        }

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, second, out _, out var otherCount));
        Assert.Equal(1, otherCount);
    }

    /// <summary>
    /// A resource whose attribute-cost modifier has never been calculated takes the price with it.
    /// </summary>
    /// <remarks>
    /// The game authors that modifier at 100 and reaches it through an accessor that recalculates on
    /// demand; collection reads the cached field and does not. So a zero means "not yet calculated",
    /// and pricing on it would multiply the whole entity to nothing — the one error direction that
    /// makes Auto Buy buy something it cannot afford. See W5.
    /// </remarks>
    [Fact]
    public void AnUncalculatedAttributeModifierWithholdsThePriceRatherThanZeroingIt()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new FakeModifierRecord(0d),
        });

        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 100d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// The attribute-cost modifier is the game's quotient, so a quality bonus makes a structure
    /// cheaper.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>GetAttributeCostMod()</c> is <c>attributeCostMod / Pow(quality.AsPercent(), bonus)</c>.
    /// Here that is <c>200 / Pow(4, 2) = 12.5</c>, whose percent is <c>0.125</c>, against the
    /// <c>2</c> the numerator alone would have given.
    /// </para>
    /// <para>
    /// The rest is the fixture from the test above, and the discount does <em>not</em> just divide
    /// its answer: it lands on the base cost before the per-quantity modifier adds to it. So
    /// <c>100 × 0.125 = 12.5</c>, then Raw adds its 3 for <c>15.5</c>, then the multiplier of 3.6
    /// gives <c>55.8</c> — and that one is under 100, where RoundToTwoSigsEarly does its only work,
    /// snapping it to <c>56</c>.
    /// </para>
    /// </remarks>
    [Fact]
    public void AQualityBonusDiscountsAStructuresPriceTheWayTheGameDiscountsIt()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakePlayerGlobals.SetStructureCost(200d);
        FakePlayerGlobals.SetAttributeQualityBonus(2d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(400d),
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = PerQuantity();
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out _));
        Assert.Equal(56d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// With no bonus earned, the quality discount is one and the price is the undiscounted one.
    /// </summary>
    /// <remarks>
    /// The exponent, not the quality, is what switches the discount off: <c>Pow(4, 0) = 1</c> whatever
    /// the quality is. This pins the neutral case on the term that actually carries it, so a reading
    /// that dropped the exponent and divided by the quality itself would fail here rather than only
    /// on a developed save.
    /// </remarks>
    [Fact]
    public void WithoutTheQualityBonusAStructurePricesAsThoughTheDiscountWereAbsent()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakePlayerGlobals.SetStructureCost(200d);
        FakePlayerGlobals.SetAttributeQualityBonus(0d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(400d),
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = PerQuantity();
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out var start, out _));
        Assert.Equal(730.8d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A resource whose quality has never been calculated takes the price with it, like its
    /// attribute-cost modifier does.
    /// </summary>
    /// <remarks>
    /// Quality is the base of the power the discount divides by, so a zero divides the price by zero
    /// and publishes an infinity — the same W5 refusal as an uncalculated attribute modifier, from the
    /// other end of the quotient. An infinite price is not the safe direction either: it is a number
    /// no consumer can compare against and every downstream sum turns into a NaN.
    /// </remarks>
    [Fact]
    public void AnUncalculatedQualityWithholdsThePriceRatherThanSendingItToInfinity()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();

        FakePlayerGlobals.SetAttributeQualityBonus(2d);
        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(0d),
            attributeCostMod = new FakeModifierRecord(200d),
        });

        var modifier = PerQuantity();
        FakeModifierVariable.All.Add(modifier);
        FakeStructure.All.Add(PricedCauldron(cauldron, water, modifier));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.False(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, cauldron, out _, out _));
    }

    /// <summary>
    /// The overpricing this fixed has a signature: the error is one factor per resource, the same for
    /// every structure priced in that resource, whatever their levels.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Live spin 6 published every one of the 342 structure cost rows too high and not one of the 402
    /// upgrade rows, because only the structure chain calls <c>AdjustAsAttribute</c>. The diagnosis
    /// that stuck first read the gap as <c>costPerQuantity^committed</c> — but the two spins that
    /// measured it disagree with that and agree with this: on the same resource, Rapid Gathering and
    /// Transmute Power were over by <c>1.8107e133</c> and <c>1.8173e133</c> despite differing by 208
    /// committed levels, and a level-driven fault would have separated them by <c>1.25^208</c>.
    /// </para>
    /// <para>
    /// So this asserts the shape rather than a number: two structures at different levels, one
    /// resource, one bonus. Without the divisor both prices are wrong; with it both are right. What
    /// makes it a regression test is the ratio — it is identical across the two, which is the
    /// fingerprint that named the term.
    /// </para>
    /// </remarks>
    [Fact]
    public void TheQualityDiscountIsOneFactorPerResourceRatherThanOnePerLevel()
    {
        var water = Guid.NewGuid();
        var shallow = Guid.NewGuid();
        var deep = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            quality = new FakeModifierRecord(1000d),
            attributeCostMod = new FakeModifierRecord(100d),
        });

        // Multiplicative, so the levels genuinely price apart — 1.25²⁰⁸ between them — while still
        // cancelling exactly out of each structure's own before-and-after ratio.
        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.MultiStacking, 1.25d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);

        FakeStructure Structure(Guid identity, int level) => new()
        {
            Identity = identity,
            Level = level,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 1e12d)),
        };

        FakeStructure.All.Add(Structure(shallow, level: 1));
        FakeStructure.All.Add(Structure(deep, level: 209));

        var collector = Collector();

        FakePlayerGlobals.SetAttributeQualityBonus(0d);
        collector.Collect();
        var undiscounted = collector.Build();
        var shallowBefore = PriceOf(undiscounted, shallow);
        var deepBefore = PriceOf(undiscounted, deep);

        FakePlayerGlobals.SetAttributeQualityBonus(3d);
        collector.Collect();
        var discounted = collector.Build();
        var shallowAfter = PriceOf(discounted, shallow);
        var deepAfter = PriceOf(discounted, deep);

        // The two are 1.25²⁰⁸ apart, so a fault that scaled with level could not show one ratio.
        Assert.NotEqual(shallowBefore, deepBefore);

        Assert.Equal(shallowBefore / shallowAfter, deepBefore / deepAfter, 6);

        // Pow(1000%, 3) = 10³, the one factor the resource contributes however deep the structure is.
        Assert.Equal(1000d, shallowBefore / shallowAfter, 6);
    }

    /// <summary>The published price of an entity's single row.</summary>
    private static double PriceOf(GameWorldState world, Guid entityId)
    {
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, entityId, out var start, out _));
        return world.PurchaseCosts[start].Amount.ToDouble();
    }

    /// <summary>The per-quantity modifier the priced-structure fixtures share: <c>Raw 0.5</c>.</summary>
    private static FakeModifierVariable PerQuantity() =>
        new()
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0.5d, order: 0),
        };

    /// <summary>
    /// The fixture from <see cref="AStructuresPriceIsComputedFromWhatWasCollected"/>, whose hand-derived
    /// price is 730.8 before any quality discount.
    /// </summary>
    private static FakeStructure PricedCauldron(Guid identity, Guid resource, FakeModifierVariable modifier) =>
        new()
        {
            Identity = identity,
            Level = 1,
            Queued = 1,
            passiveCostMod = new FakeModifierRecord(120d),
            activeCostMod = new FakeModifierRecord(150d),
            costScalingMod = new FakeModifierRecord(300d),
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((resource, 100d)),
        };

    /// <summary>
    /// An upgrade prices off the list it grows by, with the exponents kept apart from the modifiers.
    /// </summary>
    /// <remarks>
    /// Every term is deliberately off parity, because a fixture built from defaults tests almost
    /// nothing about a multiplicative chain — the defaults are the identity and every wire crosses
    /// unobserved. Derived by hand: three committed levels price level four, so both lists scale by
    /// three; the stacking modifier becomes 2³ = 8 and the raw exponent becomes 3; the exponent
    /// strengthens the modifier to 8⁽¹⁺³⁾ = 4096 before it touches the cost; 50 × 4096 = 204800,
    /// snapped to two significant digits at 200000. A reader that dropped the exponent list would
    /// publish 400 instead.
    /// </remarks>
    [Fact]
    public void AnUpgradePricesOffTheModifierListItGrowsBy()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            Level = 2,
            queuedLevels = 1,
            resourceCost = CostOf((water, 50d)),
            resourceCostModPerLevel = LevelModifiers(
                new[] { new FakeValueModifier(FakeModifierKind.MultiStacking, 2d, order: 0) },
                new[] { new FakeValueModifier(FakeModifierKind.Raw, 1d, order: 0) }),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(water, world.PurchaseCosts[start].ResourceId);
        Assert.Equal(50d, world.PurchaseCosts[start].BaseExactAmount.ToDouble(), 6);
        Assert.Equal(200000d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
        Assert.Equal(
            new[]
            {
                "upgrade.priced_level",
                "upgrade.resource_cost_per_level.modifier",
                "upgrade.resource_cost_per_level.exponent",
            },
            world.PurchaseCosts[start].ModifierSources.AsSpan().ToArray()
                .Select(source => source.Name)
                .ToArray());
    }

    /// <summary>
    /// Duplicate authored rows paid in one resource are one affordability obligation. This is the
    /// same stricter-than-native aggregation Auto Buy uses, not two independent checks which would
    /// each pass while their combined payment fails.
    /// </summary>
    [Fact]
    public void DuplicateResourceCostsUseTheSharedExactCombinerForAffordability()
    {
        var water = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);
        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 100d });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((water, 40d), (water, 70d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(
            world.PurchaseCosts, cauldron, out var start, out var count));
        Assert.Equal(2, count);
        for (var index = start; index < start + count; index++)
        {
            var row = world.PurchaseCosts[index];
            Assert.Equal(110d, row.CombinedEffectiveAmount.ToDouble(), 6);
            Assert.Equal(100d, row.AvailableAmount.ToDouble(), 6);
            Assert.False(row.ResourceAffordable);
            Assert.False(row.Affordable);
            Assert.Equal("insufficient_quantity", row.AffordabilityReasonCode);
        }
    }

    [Fact]
    public void BandwidthPurchaseAffordabilityUsesHeadroomRatherThanHoldings()
    {
        var bandwidth = Guid.NewGuid();
        var cauldron = Guid.NewGuid();
        var modifier = new FakeModifierVariable
        {
            Identity = Guid.NewGuid(),
            value = new FakeValueModifier(FakeModifierKind.Raw, 0d, order: 0),
        };
        FakeModifierVariable.All.Add(modifier);
        FakeResource.All.Add(new FakeResource
        {
            Identity = bandwidth,
            Quantity = 80d,
            bandwidthResource = true,
            maxQuantity = new FakeModifierRecord(100d),
        });
        FakeStructure.All.Add(new FakeStructure
        {
            Identity = cauldron,
            costPerQuantity = new FakeModifierRef { variable = modifier },
            baseCost = CostOf((bandwidth, 30d)),
        });

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPurchaseCostLookup.TryFindRange(
            world.PurchaseCosts, cauldron, out var start, out _));
        var row = world.PurchaseCosts[start];
        Assert.Equal(80d, world.Resources.AsSpan().ToArray()
            .Single(resource => resource.EntityId == bandwidth).TrueQuantity.ToDouble(), 6);
        Assert.Equal(20d, row.AvailableAmount.ToDouble(), 6);
        Assert.Equal(30d, row.CombinedEffectiveAmount.ToDouble(), 6);
        Assert.False(row.ResourceAffordable);
        Assert.False(row.Affordable);
        Assert.Equal("insufficient_bandwidth", row.ResourceAffordabilityReasonCode);
        Assert.Equal("insufficient_bandwidth", row.AffordabilityReasonCode);
    }

    /// <summary>
    /// Two per-level modifiers at different orders are applied in sequence, not merged.
    /// </summary>
    /// <remarks>
    /// The order field is the one part of a modifier that is silently substitutable: a reader that
    /// never bound it, or bound it to a constant, still produces a plausible number. Here it changes
    /// the answer. Three committed levels scale the stacking modifier to 2³ = 8 and the raw one to 30;
    /// at separate orders that is 50 × 8 + 30 = 430, and at the same order the raw one merges in first
    /// for (50 + 30) × 8 = 640.
    /// </remarks>
    [Fact]
    public void PerLevelModifiersAtDifferentOrdersAreAppliedInSequence()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            Level = 3,
            resourceCost = CostOf((water, 50d)),
            resourceCostModPerLevel = LevelModifiers(new[]
            {
                new FakeValueModifier(FakeModifierKind.MultiStacking, 2d, order: 0),
                new FakeValueModifier(FakeModifierKind.Raw, 10d, order: 1),
            }),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(430d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A finite upgrade keeps quoting the last level it can buy rather than one past the ceiling.
    /// </summary>
    /// <remarks>
    /// The cap is the game's, and it is easy to leave out because nothing else about an upgrade
    /// mentions it: <c>min(level + queued, maxLevel - 1)</c>. Both upgrades here sit at five committed
    /// levels and differ only in whether a ceiling applies — the bounded one prices level three at
    /// 50 × 2² and the unbounded one prices level six at 50 × 2⁵, so an implementation that ignored
    /// the cap would quote the maxed-out upgrade eight times too much.
    /// </remarks>
    [Fact]
    public void AFiniteUpgradeStopsRepricingOnceItsCeilingIsReached()
    {
        var water = Guid.NewGuid();
        var bounded = Guid.NewGuid();
        var unbounded = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        foreach (var (identity, maxLevel) in new[] { (bounded, 3), (unbounded, 0) })
        {
            FakeUpgrade.All.Add(new FakeUpgrade
            {
                Identity = identity,
                Level = 5,
                maxLevel = maxLevel,
                resourceCost = CostOf((water, 50d)),
                resourceCostModPerLevel = LevelModifiers(
                    new[] { new FakeValueModifier(FakeModifierKind.MultiStacking, 2d, order: 0) }),
            });
        }

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, bounded, out var cappedRow, out _));
        Assert.Equal(200d, world.PurchaseCosts[cappedRow].Amount.ToDouble(), 6);

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, unbounded, out var openRow, out _));
        Assert.Equal(1600d, world.PurchaseCosts[openRow].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade with nothing to grow by still publishes a price, rounded.
    /// </summary>
    /// <remarks>
    /// A flat-cost upgrade is ordinary in this game, not a degraded reading: <c>SetToLevel</c> simply
    /// skips its scaling branch. The rounding still runs, which is what 137 landing at 140 shows —
    /// treating an empty list as "could not price this" would withhold every flat upgrade, and
    /// returning the authored value untouched would disagree with the game by three.
    /// </remarks>
    [Fact]
    public void AnUpgradeWithNothingToGrowByStillPublishesARoundedPrice()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource { Identity = water, Quantity = 500d });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            Level = 4,
            resourceCost = CostOf((water, 137d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(140d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// An upgrade prices even when the resource's attribute-cost modifier reads zero.
    /// </summary>
    /// <remarks>
    /// That modifier withholds a <em>structure's</em> price, because the structure chain multiplies by
    /// it and a zero would make the structure free. The upgrade chain never touches it, so making it a
    /// shared precondition would withhold upgrade prices for a reading that says nothing about them.
    /// </remarks>
    [Fact]
    public void AnUpgradeDoesNotWaitOnTheAttributeModifierAStructureNeeds()
    {
        var water = Guid.NewGuid();
        var insight = Guid.NewGuid();

        FakeResource.All.Add(new FakeResource
        {
            Identity = water,
            Quantity = 500d,
            attributeCostMod = new FakeModifierRecord(0d),
        });
        FakeUpgrade.All.Add(new FakeUpgrade
        {
            Identity = insight,
            resourceCost = CostOf((water, 400d)),
        });

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPurchaseCostLookup.TryFindRange(world.PurchaseCosts, insight, out var start, out _));
        Assert.Equal(400d, world.PurchaseCosts[start].Amount.ToDouble(), 6);
    }

    /// <summary>
    /// A plot node's quantities are summed out of its phase instances, not asked for.
    /// </summary>
    /// <remarks>
    /// The game's own <c>GetQuantity()</c> and <c>GetTotalQuantity()</c> are closed to collection:
    /// both reach the instances through <c>GetPhaseInstance()</c>, which lazily builds a cache and
    /// creates a missing instance on the way past — a write, from a pass whose contract is that it
    /// does not write. Three idle, four growing and five resting is twelve in total and three idle,
    /// and with two claimed by a main-phase action and one by an any-phase one the any-phase claim is
    /// absorbed by the nine busy, leaving 3 - 2 - 0 = 1. The three counts are deliberately distinct:
    /// equal ones let a reader that ignores an instance's phase agree with one that does not.
    /// </remarks>
    [Fact]
    public void APlotNodesQuantitiesAreSummedFromItsPhaseInstances()
    {
        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = node,
            actionQuantityUsageMain = new FakeModifierRecord(2d),
            actionQuantityUsageAny = new FakeModifierRecord(1d),
        }
            .With(FakePlotPhase.Idle, 3)
            .With(FakePlotPhase.Growing, 4)
            .With(FakePlotPhase.Resting, 5));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.PlotNodes, node, out var row));
        Assert.Equal(3, row.Reading.IdleQuantity);
        Assert.Equal(12, row.Reading.TotalQuantity);
        Assert.Equal(1, row.RemainingQuantity);
    }

    /// <summary>
    /// A phase instance the node does not author is not part of its total.
    /// </summary>
    /// <remarks>
    /// The game totals by walking <c>phaseInfos</c> — the authored phases — and asking for each
    /// one's instance, so an instance for a phase that is not authored contributes nothing. Summing
    /// the instance list instead would be the obvious implementation and would count it, putting the
    /// suite's total quietly out of step with every calculation the game bases on its own.
    /// </remarks>
    [Fact]
    public void AnInstanceForAPhaseTheNodeDoesNotAuthorIsNotCounted()
    {
        var node = Guid.NewGuid();
        var plot = new FakePlotNode { Identity = node }.With(FakePlotPhase.Idle, 3);
        plot.phaseInstances.Add(new FakePhaseInstance
        {
            phase = FakePlotPhase.Resting,
            timers = new FakeTimerList { q = 5 },
        });
        FakePlotNode.All.Add(plot);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldLookup.TryFind(world.PlotNodes, node, out var row));
        Assert.Equal(3, row.Reading.TotalQuantity);
    }

    /// <summary>
    /// A structure publishes every word its tooltip shows, not only its primary type.
    /// </summary>
    /// <remarks>
    /// This is the first of the two bindings the keyword census found <em>lossy</em> rather than
    /// merely narrow: the tooltip prints <c>GetAllTypes()</c>, which is the subtypes prepended with
    /// the primary type, while the suite bound the primary type alone. On the pinned build that cost
    /// 138 of 180 structures one or two of their displayed words.
    /// </remarks>
    [Fact]
    public void AStructurePublishesItsPrimaryTypeAndEverySubtype()
    {
        var primary = new FakeStructureType();
        var firstSub = new FakeStructureType();
        var secondSub = new FakeStructureType();
        var structure = new FakeStructure
        {
            structureType = primary,
            structureSubTypes = { firstSub, secondSub },
            Available = true,
        };
        FakeStructure.All.Add(structure);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldEntityKeywordLookup.TryFind(
            world.EntityKeywords, structure.Identity, out var start, out var count));
        Assert.Equal(3, count);
        var rows = new List<(WorldKeywordSource Source, int Ordinal, Guid Keyword)>();
        for (var index = start; index < start + count; index++)
        {
            var row = world.EntityKeywords[index];
            Assert.Equal(WorldKeywordOwnerKind.Structure, row.OwnerKind);
            rows.Add((row.Source, row.Ordinal, row.KeywordId));
        }

        Assert.Equal(
            new[]
            {
                (WorldKeywordSource.PrimaryType, 0, primary.Identity),
                (WorldKeywordSource.TypeList, 0, firstSub.Identity),
                (WorldKeywordSource.TypeList, 1, secondSub.Identity),
            },
            rows);
    }

    /// <summary>
    /// An alchemy recipe publishes both of its authored types, not only the core one.
    /// </summary>
    /// <remarks>
    /// The second lossy binding. <c>GetCoreType()</c> is <c>alchemyTypes.Last()</c>, and every recipe
    /// on the pinned build authors exactly two types, so binding the core type alone dropped one word
    /// from all 125 of them. The core-type binding stays where it is — it is a real game concept with
    /// its own consumers — and the full list is published alongside it.
    /// </remarks>
    [Fact]
    public void AnAlchemyRecipePublishesEveryAuthoredTypeNotOnlyItsCore()
    {
        var first = new FakeAlchemyType();
        var core = new FakeAlchemyType();
        FakeAlchemyType.All.AddRange(new[] { first, core });
        var recipe = new FakeAlchemyRecipe
        {
            alchemyTypes = { first, core },
            coreType = core,
        };
        FakeAlchemyRecipe.All.Add(recipe);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldEntityKeywordLookup.TryFind(
            world.EntityKeywords, recipe.Identity, out var start, out var count));
        Assert.Equal(2, count);
        Assert.Equal(first.Identity, world.EntityKeywords[start].KeywordId);
        Assert.Equal(core.Identity, world.EntityKeywords[start + 1].KeywordId);
        Assert.True(WorldLookup.TryFind(world.AlchemyRecipes, recipe.Identity, out var published));
        Assert.Equal(core.Identity, published.CoreTypeId);
    }

    /// <summary>
    /// A class whose type list nothing bound before is published whole.
    /// </summary>
    /// <remarks>
    /// Rituals stand in for the eleven classes the suite could not see any keyword of at all. The
    /// prior census reported that rituals carry no types; they carry <c>ritualTypes</c>, and every
    /// ritual on the pinned build has one.
    /// </remarks>
    [Fact]
    public void APreviouslyUnboundClassPublishesItsAuthoredKeywords()
    {
        var strength = new FakeRitualType();
        var ritual = new FakeRitual { ritualTypes = { strength } };
        FakeRitual.All.Add(ritual);

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldEntityKeywordLookup.TryFind(
            world.EntityKeywords, ritual.Identity, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(WorldKeywordOwnerKind.Ritual, world.EntityKeywords[start].OwnerKind);
        Assert.Equal(strength.Identity, world.EntityKeywords[start].KeywordId);
    }

    /// <summary>
    /// One unreadable member withholds the whole keyword category and names what failed.
    /// </summary>
    /// <remarks>
    /// A keyword table that is silently short one class reads exactly like a table whose entities
    /// genuinely have no keywords, and the census proved that telling those two apart is the whole
    /// question. So the category fails closed rather than publishing a partial vocabulary.
    /// </remarks>
    [Fact]
    public void OneUnreadableTypeListWithholdsEveryKeyword()
    {
        var collector = Collector(("RitualSO", typeof(FakeKeywordlessRitual)));

        var report = collector.Collect();

        var keywords = report.For("entity keywords");
        Assert.Equal(WorldCategoryOutcome.Unavailable, keywords.Outcome);
        Assert.Contains("ritualTypes", keywords.FirstFailure);
        Assert.Empty(collector.Build().EntityKeywords.AsSpan().ToArray());
    }

    /// <summary>
    /// A pair exists for every action a plot offers and every action it is running, and the two are
    /// the same row when they are the same action.
    /// </summary>
    /// <remarks>
    /// The pair is what the readiness question is asked about, and it is asked of plots that offer an
    /// action they are not running and of plots running one they no longer offer. A table keyed on
    /// either side alone loses one of those two. Both sides are counted rather than flagged, so a
    /// consumer that needs the pair to be unambiguous can see that it is not.
    /// </remarks>
    [Fact]
    public void EachPlotAndActionPairIsPublishedOnce()
    {
        var offered = new FakePlotNodeAction();
        var alsoOffered = new FakePlotNodeAction();
        var retired = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { offered, alsoOffered, retired });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 7)
            .Offering(offered)
            .Offering(alsoOffered)
            .Offering(offered)
            .Running(offered)
            .Running(retired));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.Equal(3, world.PlotActions.Count);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, offered.Identity, out var both));
        Assert.Equal(2, both.Reading.OfferedCount);
        Assert.Equal(1, both.Reading.InstanceCount);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, alsoOffered.Identity, out var idle));
        Assert.Equal(1, idle.Reading.OfferedCount);
        Assert.Equal(0, idle.Reading.InstanceCount);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, retired.Identity, out var orphan));
        Assert.Equal(0, orphan.Reading.OfferedCount);
        Assert.Equal(1, orphan.Reading.InstanceCount);
    }

    /// <summary>
    /// An instance whose reference has not been resolved yet still names its action.
    /// </summary>
    /// <remarks>
    /// <c>IdObjectRef</c> holds a serialized string and a guid it memoises the parse into, and the
    /// guid is empty until something asks. Straight off a save load nothing has, so a reader that
    /// trusted the memoised field alone would see a plot with no running actions at exactly the
    /// moment a consumer most wants to know what is already running.
    /// </remarks>
    [Fact]
    public void AnInstanceIsFoundThroughItsSerializedReferenceWhenTheGuidIsNotMemoisedYet()
    {
        var action = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(action);

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 4)
            .RunningUnresolved(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.Equal(1, row.Reading.InstanceCount);
    }

    /// <summary>
    /// What one run costs is the authored cost divided by the plot's size, and how many runs fit
    /// depends on which of the plot's two remainders the action draws on.
    /// </summary>
    /// <remarks>
    /// Seven idle and five resting is twelve; one claimed by a main-phase action leaves six idle and
    /// eleven in total. A cost of seven against a size of 200% floors to three — rounding would give
    /// four — so the idle-paying action fits twice and the any-state one three times. Every number
    /// here is chosen so that swapping either remainder, dropping the size division, or rounding
    /// instead of flooring gives a different answer.
    /// </remarks>
    [Fact]
    public void AnActionsCostScalesByPlotSizeAndItsRunsByTheRemainderItDrawsOn()
    {
        var scaled = new FakePlotNodeAction { elementCost = 7, useSizeModForCost = true };
        var anyState = new FakePlotNodeAction { elementCost = 3, useAnyStateForCost = true };
        var idlePaying = new FakePlotNodeAction { elementCost = 3 };
        FakePlotNodeAction.All.AddRange(new[] { scaled, anyState, idlePaying });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = node,
            sizeMod = new FakeModifierRecord(200d),
            actionQuantityUsageMain = new FakeModifierRecord(1d),
        }
            .With(FakePlotPhase.Idle, 7)
            .With(FakePlotPhase.Resting, 5)
            .Offering(scaled)
            .Offering(anyState)
            .Offering(idlePaying));

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, scaled.Identity, out var sized));
        Assert.True(sized.ElementCostKnown);
        Assert.Equal(3, sized.ElementCost);
        Assert.True(sized.HasEnoughForOneInstance);
        Assert.Equal(2, sized.MaximumRemainingInstances);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, anyState.Identity, out var any));
        Assert.Equal(3, any.MaximumRemainingInstances);

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, idlePaying.Identity, out var idle));
        Assert.Equal(2, idle.MaximumRemainingInstances);
    }

    /// <summary>
    /// An action that takes its size from other nodes publishes no cost at all.
    /// </summary>
    /// <remarks>
    /// The game multiplies such a cost by a product over those nodes' next-size percentages, which
    /// this suite has not ported. Publishing the unscaled cost instead would be too cheap in the one
    /// direction that matters: a consumer would start a run the plot cannot pay for.
    /// </remarks>
    [Fact]
    public void AnActionThatTakesItsSizeFromOtherNodesPublishesNoCost()
    {
        var action = new FakePlotNodeAction { elementCost = 4, useSizeModForCost = true };
        action.sizeModNodes.Add(new FakePlotNode());
        FakePlotNodeAction.All.Add(action);

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node, sizeMod = new FakeModifierRecord(100d) }
            .With(FakePlotPhase.Idle, 9)
            .Offering(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.False(row.ElementCostKnown);
        Assert.False(row.HasEnoughForOneInstance);
        Assert.Equal(0, row.MaximumRemainingInstances);
    }

    /// <summary>
    /// Whether the game has confirmed an action's prerequisites is read from the latch it already set.
    /// </summary>
    /// <remarks>
    /// <c>Prerequisites.Container.Check()</c> stamps a game id and, on success, sets and leaves
    /// <c>available</c>. Collection reads the field. What that proves is one-directional: a set latch
    /// is a verdict the game reached, and an unset one is either a prerequisite the game found unmet
    /// or one it has not looked at, with nothing readable telling the two apart. The reading is named
    /// for that difference, and refuses to act either way.
    /// </remarks>
    [Fact]
    public void AnActionsPrerequisitesAreReadFromTheLatchRatherThanAsked()
    {
        var confirmed = new FakePlotNodeAction();
        confirmed.prerequisites.available = true;
        var unconfirmed = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { confirmed, unconfirmed });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 2)
            .Offering(confirmed)
            .Offering(unconfirmed));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(
            WorldPlotActionLookup.TryFind(world.PlotActions, node, confirmed.Identity, out var allowed));
        Assert.True(allowed.Reading.PrerequisitesConfirmed);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.NativeLatchedTrue,
            allowed.Reading.PrerequisiteEvidence);

        Assert.True(
            WorldPlotActionLookup.TryFind(world.PlotActions, node, unconfirmed.Identity, out var blocked));
        Assert.False(blocked.Reading.PrerequisitesConfirmed);
        Assert.Equal(
            PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation,
            blocked.Reading.PrerequisiteEvidence);
    }

    /// <summary>
    /// A plot with nothing left to give still publishes its pairs, with nothing available on them.
    /// </summary>
    /// <remarks>
    /// The game's own remainder is allowed to go negative — its two usage terms are subtracted
    /// asymmetrically — and dividing a negative remainder by a cost is where a faithful port turns
    /// into a consumer being told it may run an action a negative number of times.
    /// </remarks>
    [Fact]
    public void AnOverclaimedPlotOffersNoRunsRatherThanNegativeOnes()
    {
        var action = new FakePlotNodeAction { elementCost = 2 };
        FakePlotNodeAction.All.Add(action);

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = node,
            actionQuantityUsageMain = new FakeModifierRecord(5d),
        }
            .With(FakePlotPhase.Idle, 2)
            .Offering(action));

        var collector = Collector();
        collector.Collect();
        var world = collector.Build();

        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, action.Identity, out var row));
        Assert.Equal(2, row.ElementCost);
        Assert.False(row.HasEnoughForOneInstance);
        Assert.Equal(0, row.MaximumRemainingInstances);
    }

    /// <summary>
    /// Each of a plot's action instances is a row of its own, so "which one" is answerable.
    /// </summary>
    /// <remarks>
    /// The pair's count says a plot is running an action; it cannot say which of two instances, how
    /// much either is running, or whether either is under way. That is the question the action
    /// boundary walks the plot's list to answer on every submission, and these rows are that walk
    /// taken once. An instance whose reference resolves to nothing keeps a row keyed on no action:
    /// the plot really is holding it, and a table that dropped it would report the plot as holding
    /// one fewer.
    /// </remarks>
    [Fact]
    public void EachOfAPlotsActionInstancesIsARowOfItsOwn()
    {
        var collecting = new FakePlotNodeAction();
        var retired = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { collecting, retired });

        var node = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 9)
            .Offering(collecting)
            .Running(collecting, quantity: 3, engaged: true)
            .Running(collecting, quantity: 0)
            .Running(retired, quantity: 2)
            .RunningSomethingUnknown());

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldPlotActionLookup.TryFind(world.PlotActions, node, collecting.Identity, out var pair));
        Assert.Equal(2, pair.Reading.InstanceCount);

        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, collecting.Identity, out var start, out var count));
        Assert.Equal(2, count);

        var running = world.PlotActionInstances[start];
        Assert.Equal(0, running.Ordinal);
        Assert.Equal(3, running.Quantity);
        Assert.True(running.Engaged);
        Assert.False(running.Empty);
        Assert.True(running.ReferenceResolved);

        var idle = world.PlotActionInstances[start + 1];
        Assert.Equal(1, idle.Ordinal);
        Assert.Equal(0, idle.Quantity);
        Assert.False(idle.Engaged);
        Assert.True(idle.Empty);

        // An instance of an action the plot no longer offers is still the plot's instance.
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, retired.Identity, out var orphanStart, out var orphanCount));
        Assert.Equal(1, orphanCount);
        Assert.Equal(2, world.PlotActionInstances[orphanStart].Ordinal);

        // The unresolvable one is keyed on no action, which is the only honest key for it.
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, node, Guid.Empty, out var unknownStart, out var unknownCount));
        Assert.Equal(1, unknownCount);
        Assert.False(world.PlotActionInstances[unknownStart].ReferenceResolved);
        Assert.Equal(3, world.PlotActionInstances[unknownStart].Ordinal);

        // And it is not a pair: a pair with no action on one side is not one.
        Assert.False(WorldPlotActionLookup.TryFind(world.PlotActions, node, Guid.Empty, out _));
    }

    /// <summary>
    /// A plot action publishes the drain its diamond's hover prints: every authored entry, in the
    /// authored order, scaled by the plot's own drain modifier.
    /// </summary>
    /// <remarks>
    /// The modifier is the plot's, so the drain is keyed by the pair rather than by the action, and
    /// it is read once per pair however many instances the plot holds.
    /// </remarks>
    [Fact]
    public void APlotActionPublishesTheDrainItsDiamondPrints()
    {
        var space = new FakeResource();
        var scroll = new FakeResource();
        var create = new FakePlotNodeAction();
        create.actionDrain.costs.Add(new FakeCraftingResourceTuple { resource = space, valueBig = new BigDouble(40) });
        create.actionDrain.costs.Add(new FakeCraftingResourceTuple { resource = scroll, valueBig = new BigDouble(0.5) });
        var destroy = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { create, destroy });

        var node = Guid.NewGuid();
        var plot = new FakePlotNode { Identity = node }
            .With(FakePlotPhase.Idle, 1)
            .Offering(create)
            .Offering(destroy)
            .Running(create, quantity: 0)
            .Running(create, quantity: 2)
            .Running(destroy, quantity: 0);
        plot.GetActionInstances()[0].DrainCostMod = new BigDouble(150);
        FakePlotNode.All.Add(plot);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        var drains = WorldPlotActionDrainDeriver.For(world.PlotActionDrains, node, create.Identity).ToArray();
        Assert.Equal(2, drains.Length);
        Assert.Equal(space.Identity, drains[0].ResourceId);
        Assert.Equal(new BigDouble(60), drains[0].Amount);
        Assert.Equal(scroll.Identity, drains[1].ResourceId);
        Assert.Equal(new BigDouble(0.75), drains[1].Amount);
        Assert.Equal(0, WorldPlotActionDrainDeriver.For(world.PlotActionDrains, node, destroy.Identity).Length);
        Assert.Equal(2, world.PlotActionDrains.Count);
    }

    /// <summary>
    /// One plot's instances stay that plot's, and a second pass publishes each of them once.
    /// </summary>
    /// <remarks>
    /// The instance buffer is reused like every other, and it has no identity check behind it to
    /// catch a missed reset — a plot's instances would simply appear twice, which reads exactly like
    /// a plot running the action twice.
    /// </remarks>
    [Fact]
    public void CollectingTwicePublishesEachInstanceOnce()
    {
        var action = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(action);

        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        FakePlotNode.All.Add(new FakePlotNode { Identity = first }
            .With(FakePlotPhase.Idle, 4)
            .Offering(action)
            .Running(action, quantity: 1));
        FakePlotNode.All.Add(new FakePlotNode { Identity = second }
            .With(FakePlotPhase.Idle, 4)
            .Offering(action)
            .Running(action, quantity: 5));

        var collector = Collector();
        collector.Collect();
        collector.Collect();
        var world = collector.Build();

        Assert.Equal(2, world.PlotActionInstances.Count);
        Assert.True(WorldPlotActionInstanceLookup.TryFindRange(
            world.PlotActionInstances, second, action.Identity, out var start, out var count));
        Assert.Equal(1, count);
        Assert.Equal(5, world.PlotActionInstances[start].Quantity);
    }

    /// <summary>
    /// A plot whose authoring read throws anything but an expected native failure faults the pass
    /// instead of costing one plot.
    /// </summary>
    [Fact]
    public void APlotWhoseAuthoringThrowsUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("autoAction was destroyed");
        FakePlotNode.All.Add(new FakePlotNode
        {
            Identity = Guid.NewGuid(),
            autoAction = new FakePlotNodeAction { GetGuidFailure = failure },
        }.With(FakePlotPhase.Idle, 4));

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A plot whose actions read throws anything but an expected native failure faults the pass
    /// instead of costing one plot.
    /// </summary>
    [Fact]
    public void APlotWhoseActionsThrowUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("offered action was destroyed");
        FakePlotNode.All.Add(new FakePlotNode { Identity = Guid.NewGuid() }
            .With(FakePlotPhase.Idle, 4)
            .Offering(new FakePlotNodeAction { GetGuidFailure = failure }));

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// An action whose effect blocks throw anything but an expected native failure faults the pass
    /// instead of costing one action.
    /// </summary>
    [Fact]
    public void AnActionWhoseEffectsThrowUnexpectedlyFaultsThePass()
    {
        var failure = new NullReferenceException("scaling weight was destroyed");
        var block = new FakeEffectBlock();
        block.effectMods.Add(new FakeScalingWeightMod
        {
            scalingWeightRef = { scalingWeight = new FakeScalingWeight { GetGuidFailure = failure } },
        });
        FakePlotNodeAction.All.Add(new FakePlotNodeAction { completeEffects = { block } });

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// A queue whose walk throws part-way publishes none of its slots: the slots already read would
    /// otherwise stand without their queue and undercount what is running.
    /// </summary>
    [Fact]
    public void APlotActionQueueThatThrowsMidWalkLeavesNoSlotsBehind()
    {
        var collecting = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(collecting);
        var orchard = new FakePlotNode { Identity = Guid.NewGuid() }.With(FakePlotPhase.Idle, 4);
        FakePlotNode.All.Add(orchard);

        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 3, plot = orchard, action = collecting });
        queue.value.Add(new FakeQueueSlot { quantity = 1, plot = orchard, action = collecting, throwsOnEngaged = true });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.Equal(1, report.For("action queues").Skipped);
        Assert.StartsWith("reading the plot-action queue threw: ", report.For("action queues").FirstFailure);
        Assert.False(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out _));
        Assert.Equal(0, world.ActionQueueSlots.Count);
    }

    /// <summary>
    /// A plot-action queue walk that throws anything but an expected native failure faults the pass
    /// instead of costing the queue.
    /// </summary>
    [Fact]
    public void APlotActionQueueWhoseWalkThrowsUnexpectedlyFaultsThePass()
    {
        var collecting = new FakePlotNodeAction();
        FakePlotNodeAction.All.Add(collecting);
        var orchard = new FakePlotNode { Identity = Guid.NewGuid() }.With(FakePlotPhase.Idle, 4);
        FakePlotNode.All.Add(orchard);

        var failure = new NullReferenceException("slot was destroyed");
        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 3, plot = orchard, action = collecting });
        queue.value.Add(new FakeQueueSlot
        {
            quantity = 1,
            plot = orchard,
            action = collecting,
            throwsOnEngaged = true,
            engagedFailure = failure,
        });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

    /// <summary>
    /// The plot-action queue publishes its occupancy and what sits in each of its slots.
    /// </summary>
    /// <remarks>
    /// This is the reading Auto Harvest takes one slot at a time at its action boundary, taken once
    /// for every consumer instead. The empty slot still gets a row: "position two is free" and
    /// "position two was not read" are different facts, and a table that only held occupants could
    /// not tell them apart.
    /// </remarks>
    [Fact]
    public void ThePlotActionQueueCarriesItsOccupancyAndWhatSitsInEachSlot()
    {
        var collecting = new FakePlotNodeAction();
        var growing = new FakePlotNodeAction();
        FakePlotNodeAction.All.AddRange(new[] { collecting, growing });

        var orchard = new FakePlotNode { Identity = Guid.NewGuid() }.With(FakePlotPhase.Idle, 4);
        var hoard = new FakePlotNode { Identity = Guid.NewGuid() }.With(FakePlotPhase.Idle, 4);
        FakePlotNode.All.AddRange(new[] { orchard, hoard });

        var queue = new FakeActionQueue();
        queue.value.Add(new FakeQueueSlot { quantity = 3, engaged = true, plot = orchard, action = collecting });
        queue.value.Add(new FakeQueueSlot());
        queue.value.Add(new FakeQueueSlot { quantity = 1, plot = hoard, action = growing });
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActivePlotNodeActions.Uuid] = queue;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());
        Assert.True(WorldLookup.TryFind(world.ActionQueues, queue.Identity, out var row));
        Assert.Equal(3, row.SlotCount);
        Assert.Equal(2, row.UsedSlots);
        Assert.Equal(1, row.EmptySlots);
        Assert.True(row.HasEmptySlot);
        Assert.True(row.Consistent);

        // Its capacity is the length of its own list, so there is no variable to point at.
        Assert.Equal(Guid.Empty, row.MaxQueuedItemsId);

        Assert.True(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, queue.Identity, out var start, out var count));
        Assert.Equal(3, count);

        var running = world.ActionQueueSlots[start];
        Assert.Equal(0, running.Index);
        Assert.False(running.Empty);
        Assert.Equal(orchard.Identity, running.PlotNodeId);
        Assert.Equal(collecting.Identity, running.PlotNodeActionId);
        Assert.Equal(3, running.Quantity);
        Assert.True(running.Engaged);

        var free = world.ActionQueueSlots[start + 1];
        Assert.True(free.Empty);
        Assert.Equal(Guid.Empty, free.PlotNodeId);
        Assert.Equal(0, free.Quantity);
        Assert.False(free.Engaged);

        var queued = world.ActionQueueSlots[start + 2];
        Assert.Equal(2, queued.Index);
        Assert.Equal(hoard.Identity, queued.PlotNodeId);
        Assert.Equal(growing.Identity, queued.PlotNodeActionId);
        Assert.False(queued.Engaged);

        Assert.False(WorldActionQueueSlotLookup.TryFindRange(
            world.ActionQueueSlots, Guid.NewGuid(), out _, out _));
    }

    private static FakeCostList CostOf(params (Guid Resource, double Amount)[] entries)
    {
        var list = new FakeCostList();
        foreach (var (resource, amount) in entries) list.costs.Add(new FakeCostEntry(resource, amount));
        return list;
    }

    private static FakeModifierListRef LevelModifiers(
        FakeValueModifier[] modifiers,
        FakeValueModifier[]? exponents = null)
    {
        var list = new FakeModifierList();
        list.modifiers.AddRange(modifiers);
        if (exponents is not null) list.exponents.AddRange(exponents);
        return new FakeModifierListRef { variable = list };
    }

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
        FakeResource.All.Add(OverflowingResource(mana));

        var collector = Collector();

        FakePlayerGlobals.SetOverflow(200d);
        collector.Collect();
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var damped));

        FakePlayerGlobals.SetOverflow(100d);
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
        var manager = new FakeSpellManager { spellWeightVariables = null! };
        manager.activeSpells.maximum = 3;
        manager.activeSpells.value.Add(new FakeSpell());
        FakeSpellManager.instance = manager;

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
        FakeSpellManager.instance = new FakeSpellManager();
        FakePlayerGlobals.SetMaximumSpellOutputLevel(new FakeCount { value = null! });

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
        FakeResource.All.Add(OverflowingResource(mana));

        var collector = Collector(("Player", null));
        var report = collector.Collect();

        Assert.False(report.IsComplete);
        Assert.Equal(WorldCategoryOutcome.Unavailable, report.For("spell workbench").Outcome);
        Assert.True(WorldLookup.TryFind(collector.Build().Resources, mana, out var row));
        Assert.False(BigDouble.IsNaN(row.TrueRate) || BigDouble.IsInfinity(row.TrueRate));
    }

    /// <summary>Over its cap with an active rate, which is the branch the frame-wide terms feed.</summary>
    private static FakeResource OverflowingResource(Guid identity) =>
        new()
        {
            Identity = identity,
            Quantity = new BigDouble(150d),
            maxQuantity = new FakeModifierRecord(100d),
            rate = new FakeModifierRecord(10d, activeCount: 1),
            lifetimeQuantity = new BigDouble(400d),
            Visible = true,
        };

    /// <summary>
    /// A type asset publishes one row per modifier record it owns, carrying how loaded each is.
    /// </summary>
    /// <remarks>
    /// The row set is the class's whole record list rather than the records that happen to carry a
    /// modifier, because "this type has no bonus on its power" and "this type has no power record"
    /// are different facts and only one of them is a reading.
    /// </remarks>
    [Fact]
    public void ATypeAssetPublishesEveryOneOfItsModifierRecordsWithItsLoad()
    {
        var type = new FakeStructureType();
        type.structurePower.activeModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.MultiStacking, 0.1d, 0);
        type.structurePower.activeModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.MultiStacking, 0.25d, 0);
        type.bonusLevels.passiveModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.Raw, 2d, 0);
        FakeStructureType.All.Add(type);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        var rows = TypeModifiersOf(world, type.Identity);
        Assert.Equal(13, rows.Count);
        Assert.All(rows, row =>
            Assert.Equal(WorldTypeModifierOwnerKind.StructureType, row.OwnerKind));

        var power = Assert.Single(rows, row => row.Property == "structurePower");
        Assert.Equal(2, power.ActiveCount);
        Assert.Equal(0, power.PassiveCount);

        var bonusLevels = Assert.Single(rows, row => row.Property == "bonusLevels");
        Assert.Equal(0, bonusLevels.ActiveCount);
        Assert.Equal(1, bonusLevels.PassiveCount);
    }

    /// <summary>
    /// Every modifier on a type record publishes its own magnitude and the source that put it there.
    /// </summary>
    /// <remarks>
    /// This is the reading a distributor's total is folded from off-thread. Capture takes the
    /// entries and stops: the fold is arithmetic, and arithmetic on the Unity thread is the thing the
    /// capture boundary exists to refuse.
    /// </remarks>
    [Fact]
    public void EveryModifierOnATypeRecordPublishesItsSourceAndItsMagnitude()
    {
        var modifier = Guid.NewGuid();
        var sourceId = Guid.NewGuid();
        var source = new global::ChallengeSO();
        source.SetGuid(sourceId);

        var type = new FakeRitualType();
        type.power.passiveModifiers[modifier] =
            new FakeValueModifier(FakeModifierKind.MultiStacking, 0.4d, order: 2, reference: source);
        FakeRitualType.All.Add(type);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        var contribution = Assert.Single(
            ContributionsOf(world, type.Identity), row => row.Property == "power");
        Assert.Equal(modifier, contribution.Contribution.ModifierId);
        Assert.Equal(sourceId, contribution.Contribution.SourceId);
        Assert.Equal((int)FakeModifierKind.MultiStacking, contribution.Contribution.ModifierType);
        Assert.Equal(0.4d, contribution.Contribution.Amount.ToDouble());
        Assert.Equal(2, contribution.Contribution.Order);
        Assert.True(contribution.Contribution.Passive);
    }

    /// <summary>
    /// A record carrying nothing publishes its row with a zero load and no contributions.
    /// </summary>
    /// <remarks>
    /// A distributor holds no value of its own, so an empty one is indistinguishable from an absent
    /// one unless the empty one is published. A consumer reading the counts must be able to tell "no
    /// bonus" from "not read", which is the same reason the category withholds rather than shortens
    /// when a member does not bind.
    /// </remarks>
    [Fact]
    public void ARecordCarryingNoModifiersStillPublishesItsRow()
    {
        var type = new FakeTimeRuneType();
        FakeTimeRuneType.All.Add(type);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        var rows = TypeModifiersOf(world, type.Identity);
        Assert.Equal(5, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(0, row.ActiveCount);
            Assert.Equal(0, row.PassiveCount);
        });
        Assert.Empty(ContributionsOf(world, type.Identity));
    }

    /// <summary>
    /// A parent structure type publishes the types it confers its records on.
    /// </summary>
    /// <remarks>
    /// <c>RegisterSubType</c> wires the parent's thirteen records into the child's thirteen, so a
    /// bonus on the parent reaches the children's members. Without this edge, "a type-wide bonus is
    /// bounded by that type's own membership" is simply false for structures.
    /// </remarks>
    [Fact]
    public void AParentStructureTypePublishesTheTypesItConfersItsRecordsOn()
    {
        var arcanist = new FakeStructureType();
        var flameweaver = new FakeStructureType();
        var stormshaper = new FakeStructureType();
        var primal = new FakeStructureType { subTypes = { arcanist, flameweaver, stormshaper } };
        FakeStructureType.All.Add(primal);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        var edges = new List<(int Ordinal, Guid SubType)>();
        for (var index = 0; index < world.TypeSubtypes.Count; index++)
        {
            var row = world.TypeSubtypes[index];
            Assert.Equal(primal.Identity, row.TypeId);
            edges.Add((row.Ordinal, row.SubTypeId));
        }

        Assert.Equal(
            new[]
            {
                (0, arcanist.Identity),
                (1, flameweaver.Identity),
                (2, stormshaper.Identity),
            },
            edges);
    }

    /// <summary>
    /// An equipped spell publishes the spell types it currently resonates with.
    /// </summary>
    /// <remarks>
    /// The authored recipe list is not the set the game multiplies over: glyphs rewrite
    /// <c>augmentedSpellTypes</c> on the live spell, and only that field can answer what a spell
    /// resonates with right now.
    /// </remarks>
    [Fact]
    public void AnEquippedSpellPublishesTheTypesItCurrentlyResonatesWith()
    {
        var cantrip = new FakeSpellType();
        var arcane = new FakeSpellType();
        var equipped = new FakeSpell { augmentedSpellTypes = { cantrip, arcane } };

        var loadout = new FakeSpellLoadout();
        loadout.value.Add(equipped);
        FakeIdRegistry.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        var rows = new List<(int Slot, int Ordinal, Guid SpellType)>();
        for (var index = 0; index < world.SpellSlotTypes.Count; index++)
        {
            var row = world.SpellSlotTypes[index];
            rows.Add((row.SlotIndex, row.Ordinal, row.SpellTypeId));
        }

        Assert.Equal(
            new[] { (0, 0, cantrip.Identity), (0, 1, arcane.Identity) },
            rows);
    }

    /// <summary>
    /// One unreadable record withholds both type-modifier tables rather than shortening either.
    /// </summary>
    /// <remarks>
    /// A modifier table that is silently short a taxonomy reads exactly like a taxonomy whose types
    /// carry no modifiers, and a strategist would act on the second reading. The reason names the
    /// member that failed, so the build difference is diagnosable rather than merely fatal.
    /// </remarks>
    [Fact]
    public void ARecordThatDoesNotBindWithholdsBothTypeModifierTables()
    {
        var collector = Collector(("RitualTypeSO", typeof(FakeIdRegistry)));
        var report = collector.Collect();
        var world = collector.Build();

        Assert.False(report.IsComplete);
        foreach (var category in new[] { "type modifiers", "type modifier contributions" })
        {
            var outcome = report.For(category);
            Assert.Equal(WorldCategoryOutcome.Unavailable, outcome.Outcome);
            Assert.Contains("RitualTypeSO.power", outcome.FirstFailure, StringComparison.Ordinal);
        }

        Assert.Equal(0, world.TypeModifiers.Count);
        Assert.Equal(0, world.TypeModifierContributions.Count);
    }

    /// <summary>
    /// A type's own total and the member value it already sits inside are published side by side, and
    /// nothing multiplies them together.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The whole design turns on this. <c>StructureTypeSO.RegisterStructure</c> wires
    /// <c>structurePower</c> into <c>StructureSO.power</c>, so a modifier landing on the type is
    /// pushed into the member and the member's own record already carries it. A consumer reading both
    /// and multiplying would square the bonus, and every number on the way would stay plausible.
    /// </para>
    /// <para>
    /// So the assertion is not that the total is right — it is that the product never appears. The
    /// sweep is reflective rather than a written-out list of members, because a magnitude added to a
    /// derived row later has to inherit this rule rather than escape it by having been written after
    /// the test.
    /// </para>
    /// </remarks>
    [Fact]
    public void NoDerivationMultipliesATypeTotalIntoTheMemberValueItAlreadySitsIn()
    {
        var type = new FakeStructureType();
        type.structurePower.activeModifiers[Guid.NewGuid()] =
            new FakeValueModifier(FakeModifierKind.Raw, 50d, 0);
        FakeStructureType.All.Add(type);

        var structure = new FakeStructure
        {
            structureType = type,
            power = new FakeModifierRecord(250d),
        };
        FakeStructure.All.Add(structure);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        // The type's own total: Adjust(100) over one Raw +50.
        Assert.True(WorldTypeModifierTotalLookup.TryFindProperty(
            world.TypeModifierTotals, type.Identity, "structurePower", out var total));
        Assert.Equal(150d, total.DistributedTotalPercent.ToDouble(), 9);
        Assert.Equal(1.5d, total.DistributedTotalMultiplier.ToDouble(), 9);

        // The member value, published exactly as captured and untouched by any of it.
        Assert.True(WorldLookup.TryFind(world.Structures, structure.Identity, out var published));
        Assert.Equal(250d, published.Reading.Modifiers.Power.ToDouble(), 9);

        // What the keyword is worth is the type's total across its members, not a product with them.
        Assert.True(WorldKeywordModifierLookup.TryFind(
            world.KeywordModifiers, type.Identity, out var start, out var count));
        var keyword = Assert.Single(
            Enumerable.Range(start, count)
                .Select(index => world.KeywordModifiers[index])
                .Where(row => row.Property == "structurePower"));
        Assert.Equal(150d, keyword.DistributedTotalPercent.ToDouble(), 9);
        Assert.Equal(1, keyword.MemberCount);

        var derived = DerivedMagnitudes(world).ToArray();
        Assert.NotEmpty(derived);
        Assert.DoesNotContain(375d, derived);    // the member value times the total as a multiplier
        Assert.DoesNotContain(37500d, derived);  // the member value times the total as a percent
    }

    private static IEnumerable<double> DerivedMagnitudes(GameWorldState world) =>
        Magnitudes(world.TypeModifierTotals)
            .Concat(Magnitudes(world.KeywordModifiers))
            .Concat(Magnitudes(world.SpellTypeResonance));

    private static IEnumerable<double> Magnitudes<TRow>(PublicationTable<TRow> table)
        where TRow : struct
    {
        var magnitudes = typeof(TRow)
            .GetProperties(
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic)
            .Where(property => property.PropertyType == typeof(BigDouble))
            .ToArray();

        for (var index = 0; index < table.Count; index++)
        {
            object row = table[index];
            foreach (var magnitude in magnitudes)
            {
                yield return ((BigDouble)magnitude.GetValue(row)!).ToDouble();
            }
        }
    }

    private static List<WorldTypeModifier> TypeModifiersOf(GameWorldState world, Guid typeId)
    {
        var rows = new List<WorldTypeModifier>();
        for (var index = 0; index < world.TypeModifiers.Count; index++)
        {
            var row = world.TypeModifiers[index];
            if (row.TypeId == typeId) rows.Add(row);
        }

        return rows;
    }

    private static List<WorldTypeModifierContribution> ContributionsOf(
        GameWorldState world,
        Guid typeId)
    {
        var rows = new List<WorldTypeModifierContribution>();
        for (var index = 0; index < world.TypeModifierContributions.Count; index++)
        {
            var row = world.TypeModifierContributions[index];
            if (row.TypeId == typeId) rows.Add(row);
        }

        return rows;
    }
}

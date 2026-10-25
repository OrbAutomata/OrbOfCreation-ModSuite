using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public sealed class GameWorldCollectorTypeModifierTests : GameWorldCollectorTestBase
{

    /// <summary>
    /// A type's subtype that throws unexpectedly faults the pass rather than costing the type's
    /// modifier row.
    /// </summary>
    [Fact]
    public void AnUnexpectedThrowReadingATypeModifierRowFaultsThePass()
    {
        var failure = new NullReferenceException("subtype was destroyed");
        StructureTypeSO.All.Add(new StructureTypeSO
        {
            subTypes = { new StructureTypeSO { GetGuidFailure = failure } },
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
        var type = new StructureTypeSO();
        type.structurePower.activeModifiers[Guid.NewGuid()] = new ValueModifier(
            ValueModifier.ValueModifierType.Raw, 1d, 0, new global::IdScriptableObject { GetGuidFailure = failure });
        StructureTypeSO.All.Add(type);

        var thrown = Assert.Throws<NullReferenceException>(() => Collector().Collect());

        Assert.Same(failure, thrown);
    }

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
        var type = new StructureTypeSO();
        type.structurePower.activeModifiers[Guid.NewGuid()] =
            new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 0.1d, 0);
        type.structurePower.activeModifiers[Guid.NewGuid()] =
            new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 0.25d, 0);
        type.bonusLevels.passiveModifiers[Guid.NewGuid()] =
            new ValueModifier(ValueModifier.ValueModifierType.Raw, 2d, 0);
        StructureTypeSO.All.Add(type);

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

        var type = new RitualTypeSO();
        type.power.passiveModifiers[modifier] =
            new ValueModifier(ValueModifier.ValueModifierType.MultiStacking, 0.4d, order: 2, reference: source);
        RitualTypeSO.All.Add(type);

        var collector = Collector();
        var report = collector.Collect();
        var world = collector.Build();

        Assert.True(report.IsComplete, report.Describe());

        var contribution = Assert.Single(
            ContributionsOf(world, type.Identity), row => row.Property == "power");
        Assert.Equal(modifier, contribution.Contribution.ModifierId);
        Assert.Equal(sourceId, contribution.Contribution.SourceId);
        Assert.Equal((int)ValueModifier.ValueModifierType.MultiStacking, contribution.Contribution.ModifierType);
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
        var type = new TimeRuneTypeSO();
        TimeRuneTypeSO.All.Add(type);

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
        var arcanist = new StructureTypeSO();
        var flameweaver = new StructureTypeSO();
        var stormshaper = new StructureTypeSO();
        var primal = new StructureTypeSO { subTypes = { arcanist, flameweaver, stormshaper } };
        StructureTypeSO.All.Add(primal);

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
        var cantrip = new SpellTypeSO();
        var arcane = new SpellTypeSO();
        var equipped = new Spell { augmentedSpellTypes = { cantrip, arcane } };

        var loadout = new SpellListVariable();
        loadout.value.Add(equipped);
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = loadout;

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
        var collector = Collector(("RitualTypeSO", typeof(IdScriptableObject)));
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
        var type = new StructureTypeSO();
        type.structurePower.activeModifiers[Guid.NewGuid()] =
            new ValueModifier(ValueModifier.ValueModifierType.Raw, 50d, 0);
        StructureTypeSO.All.Add(type);

        var structure = new StructureSO
        {
            structureType = type,
            power = new ValueModifierRecord(250d),
        };
        StructureSO.All.Add(structure);

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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Pins the arithmetic the capture path deliberately left undone: what a type's own record adds up
/// to, what a keyword is worth across everything it reaches, and the one type layer the game itself
/// multiplies into a member's number.
/// </summary>
/// <remarks>
/// The fixtures are hand-computed rather than golden. A fold that silently changed shape would still
/// agree with a number this suite produced; it cannot agree with one worked out from
/// <c>ValueModifier.Adjust</c> by hand, which is why every expectation below is written next to the
/// steps that produce it.
/// </remarks>
public sealed class WorldTypeModifierDerivationTests
{
    private const string Merging = "MergingModifierRecord";
    private const string Ordered = "OrderedMultiplierRecord";
    private const string Value = "ValueModifierRecord";
    private const string Plain = "ModifierRecord";

    /// <summary>
    /// A distributor's own total is <c>Adjust(100)</c> over its entries, merged by order before any
    /// of it applies.
    /// </summary>
    /// <remarks>
    /// Worked by hand from the seed the game passes. Order 0 first: the Raw modifier takes 100 to
    /// 120, then the two MultiDiminishing modifiers <em>merge</em> into one 0.75 and multiply once —
    /// 120 × 1.75 = 210. Order 1 then multiplies by 2, giving 420. Applying the two diminishing
    /// modifiers in sequence instead would give 120 × 1.5 × 1.25 = 225, a plausible number and the
    /// wrong one, so it is asserted against.
    /// </remarks>
    [Fact]
    public void ADistributorTotalsToAdjustOneHundredOverItsEntries()
    {
        var type = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((type, "structurePower", Ordered)),
            Contributions(
                (type, "structurePower", Kind.Raw, 20d, 0),
                (type, "structurePower", Kind.MultiDiminishing, 0.5d, 0),
                (type, "structurePower", Kind.MultiDiminishing, 0.25d, 0),
                (type, "structurePower", Kind.MultiStacking, 2d, 1)));

        var total = Assert.Single(totals.AsSpan().ToArray());
        Assert.Equal("structurePower", total.Property);
        Assert.Equal(4, total.ContributionCount);
        Assert.Equal(420d, total.DistributedTotalPercent.ToDouble(), 9);
        Assert.NotEqual(450d, total.DistributedTotalPercent.ToDouble(), 9);
        Assert.Equal(4.2d, total.DistributedTotalMultiplier.ToDouble(), 9);
    }

    /// <summary>
    /// A record that carries a value of its own is folded where it is published, and gets no second
    /// answer here.
    /// </summary>
    [Fact]
    public void ARecordHoldingItsOwnValueGetsNoDerivedTotal()
    {
        var type = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((type, "cooldownSpeed", Value), (type, "power", Value)),
            Contributions(
                (type, "cooldownSpeed", Kind.Raw, 15d, 0),
                (type, "power", Kind.MultiStacking, 3d, 0)));

        Assert.Equal(0, totals.Count);
    }

    /// <summary>
    /// A distributor nothing has landed on totals to a flat hundred percent, and says so with a row.
    /// </summary>
    /// <remarks>
    /// "This bonus is currently worth nothing" and "nobody derived this bonus" are different
    /// readings, and only one of them is an answer.
    /// </remarks>
    [Fact]
    public void ADistributorCarryingNothingStillTotalsToAFlatHundred()
    {
        var type = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((type, "bonusLevels", Merging)),
            Contributions());

        var total = Assert.Single(totals.AsSpan().ToArray());
        Assert.Equal(0, total.ContributionCount);
        Assert.Equal(100d, total.DistributedTotalPercent.ToDouble(), 9);
        Assert.Equal(1d, total.DistributedTotalMultiplier.ToDouble(), 9);
    }

    /// <summary>
    /// The one record class that holds no value and distributes to nothing still totals, and names
    /// the class that says so.
    /// </summary>
    /// <remarks>
    /// <c>ResearchTypeSO.levelRequirementAdjust</c> is a plain <c>ModifierRecord</c>.
    /// <c>ResearchTypeSO.RegisterResearch</c> wires only <c>power</c> and <c>maxLevelCap</c> into its
    /// members, so this total is the one on the table that is <em>not</em> already inside a member
    /// value. <c>RecordNativeType</c> is what a consumer reads to tell the two apart.
    /// </remarks>
    [Fact]
    public void APullOnlyRecordTotalsAndNamesTheClassThatSaysSo()
    {
        var type = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((type, "levelRequirementAdjust", Plain)),
            Contributions((type, "levelRequirementAdjust", Kind.Raw, -30d, 0)));

        var total = Assert.Single(totals.AsSpan().ToArray());
        Assert.Equal(Plain, total.RecordNativeType);
        Assert.Equal(70d, total.DistributedTotalPercent.ToDouble(), 9);
    }

    /// <summary>Entries never cross from one record to the next.</summary>
    [Fact]
    public void EachRecordFoldsOnlyItsOwnEntries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records(
                (first, "power", Ordered),
                (first, "speed", Ordered),
                (second, "power", Ordered)),
            Contributions(
                (first, "power", Kind.Raw, 10d, 0),
                (first, "speed", Kind.Raw, 20d, 0),
                (second, "power", Kind.Raw, 30d, 0)));

        Assert.Equal(3, totals.Count);
        Assert.Equal(110d, Total(totals, first, "power").DistributedTotalPercent.ToDouble(), 9);
        Assert.Equal(120d, Total(totals, first, "speed").DistributedTotalPercent.ToDouble(), 9);
        Assert.Equal(130d, Total(totals, second, "power").DistributedTotalPercent.ToDouble(), 9);
    }

    /// <summary>
    /// A bonus on a parent structure type reaches the members of every type below it.
    /// </summary>
    /// <remarks>
    /// <c>StructureTypeSO.Initialize()</c> wires each parent's thirteen records into each child's
    /// thirteen, so the set a type-wide bonus covers is the transitive closure over that edge rather
    /// than the parent's own membership. The audited build authors exactly one such chain —
    /// <c>PrimalStructures → [Arcanist, Flameweaver, Stormshaper]</c> — and without it the claim that
    /// a type bonus is bounded by its own members is simply wrong for structures.
    /// </remarks>
    [Fact]
    public void AKeywordReachesEveryMemberBelowItInTheSubtypeChain()
    {
        var primal = Guid.NewGuid();
        var arcanist = Guid.NewGuid();
        var flameweaver = Guid.NewGuid();

        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((primal, "structurePower", Ordered)),
            Contributions((primal, "structurePower", Kind.Raw, 80d, 0)));

        var keywords = WorldKeywordModifierDeriver.Build(
            totals,
            Keywords(
                (Guid.NewGuid(), WorldKeywordOwnerKind.Structure, primal),
                (Guid.NewGuid(), WorldKeywordOwnerKind.Structure, arcanist),
                (Guid.NewGuid(), WorldKeywordOwnerKind.Structure, arcanist),
                (Guid.NewGuid(), WorldKeywordOwnerKind.Structure, flameweaver)),
            Subtypes((primal, arcanist), (primal, flameweaver)));

        var row = Assert.Single(keywords.AsSpan().ToArray());
        Assert.Equal(primal, row.KeywordId);
        Assert.Equal(WorldKeywordOwnerKind.Structure, row.MemberKind);
        Assert.Equal("structurePower", row.Property);
        Assert.Equal(4, row.MemberCount);
        Assert.Equal(180d, row.DistributedTotalPercent.ToDouble(), 9);
    }

    /// <summary>A member named by two rungs of a chain is still one member.</summary>
    [Fact]
    public void AKeywordCountsAMemberOnceHoweverManyRungsNameIt()
    {
        var parent = Guid.NewGuid();
        var child = Guid.NewGuid();
        var both = Guid.NewGuid();

        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((parent, "structureSpeed", Ordered)),
            Contributions());

        var keywords = WorldKeywordModifierDeriver.Build(
            totals,
            Keywords(
                (both, WorldKeywordOwnerKind.Structure, parent),
                (both, WorldKeywordOwnerKind.Structure, child)),
            Subtypes((parent, child)));

        Assert.Equal(1, Assert.Single(keywords.AsSpan().ToArray()).MemberCount);
    }

    /// <summary>Members of different classes are counted apart, because they are different things.</summary>
    [Fact]
    public void AKeywordSplitsItsCountByTheKindOfMemberItReaches()
    {
        var type = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((type, "power", Ordered)),
            Contributions((type, "power", Kind.Raw, 25d, 0)));

        var keywords = WorldKeywordModifierDeriver.Build(
            totals,
            Keywords(
                (Guid.NewGuid(), WorldKeywordOwnerKind.HarvestElement, type),
                (Guid.NewGuid(), WorldKeywordOwnerKind.HarvestElement, type),
                (Guid.NewGuid(), WorldKeywordOwnerKind.HarvestAction, type)),
            Subtypes());

        var rows = keywords.AsSpan().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(WorldKeywordOwnerKind.HarvestElement, rows[0].MemberKind);
        Assert.Equal(2, rows[0].MemberCount);
        Assert.Equal(WorldKeywordOwnerKind.HarvestAction, rows[1].MemberKind);
        Assert.Equal(1, rows[1].MemberCount);
        Assert.All(rows, row => Assert.Equal(125d, row.DistributedTotalPercent.ToDouble(), 9));
    }

    /// <summary>A type nothing wears is not a keyword, and gets no row.</summary>
    [Fact]
    public void ATypeNoEntityWearsHasNoKeywordRow()
    {
        var type = Guid.NewGuid();
        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((type, "power", Ordered)),
            Contributions((type, "power", Kind.Raw, 25d, 0)));

        var keywords = WorldKeywordModifierDeriver.Build(
            totals,
            Keywords((Guid.NewGuid(), WorldKeywordOwnerKind.Structure, Guid.NewGuid())),
            Subtypes());

        Assert.Equal(0, keywords.Count);
    }

    /// <summary>
    /// With no resonance in play, the type layer is the plain product of every type's percent.
    /// </summary>
    /// <remarks>
    /// <c>GetResonantPercent</c> tests <c>Utils.Approx(elementalRes, One)</c> first, and takes the
    /// unexponentiated closure when it holds. Worked by hand: both resonances are 100, so as percents
    /// they are 1 and their product is 1; the powers are 150 and 200, so as percents 1.5 and 2, and
    /// the layer is 3.
    /// </remarks>
    [Fact]
    public void TheSpellTypeLayerIsThePlainProductWhenNothingResonates()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var recipe = Guid.NewGuid();

        var resonance = WorldSpellTypeResonanceDeriver.Build(
            Slots((0, recipe)),
            SlotTypes((0, first), (0, second)),
            NoRelations(),
            SpellTypes(
                SpellType(first, power: 150d, costMod: 50d, cooldownSpeed: 100d),
                SpellType(second, power: 200d, costMod: 200d, cooldownSpeed: 400d)));

        var row = Assert.Single(resonance.AsSpan().ToArray());
        Assert.Equal(0, row.SlotIndex);
        Assert.Equal(recipe, row.SpellRecipeId);
        Assert.Equal(2, row.TypeCount);
        Assert.False(row.ElementalExponentApplied);
        Assert.Equal(1d, row.ElementalResonancePercent.ToDouble(), 9);
        Assert.Equal(3d, row.TypePowerPercent.ToDouble(), 9);
        Assert.Equal(1d, row.TypeCostPercent.ToDouble(), 9);
        Assert.Equal(4d, row.TypeCooldownSpeedPercent.ToDouble(), 9);
    }

    /// <summary>
    /// A resonance that is not one raises the elemental types' percents to it, and leaves the others
    /// alone.
    /// </summary>
    /// <remarks>
    /// Worked by hand: resonances 200 and 100 are percents 2 and 1, so the exponent is 2 and the
    /// branch turns on. The elemental type's power percent 1.5 becomes 1.5² = 2.25; the
    /// non-elemental type's 2 stays 2; the layer is 4.5. Applying the exponent to both would give
    /// 2.25 × 4 = 9.
    /// </remarks>
    [Fact]
    public void AnElementalResonanceRaisesOnlyTheElementalTypes()
    {
        var elemental = Guid.NewGuid();
        var ordinary = Guid.NewGuid();

        var resonance = WorldSpellTypeResonanceDeriver.Build(
            Slots((0, Guid.NewGuid())),
            SlotTypes((0, elemental), (0, ordinary)),
            NoRelations(),
            SpellTypes(
                SpellType(elemental, power: 150d, elementalResonance: 200d, isElemental: true),
                SpellType(ordinary, power: 200d, elementalResonance: 100d)));

        var row = Assert.Single(resonance.AsSpan().ToArray());
        Assert.True(row.ElementalExponentApplied);
        Assert.Equal(2d, row.ElementalResonancePercent.ToDouble(), 9);
        Assert.Equal(4.5d, row.TypePowerPercent.ToDouble(), 6);
        Assert.NotEqual(9d, row.TypePowerPercent.ToDouble(), 6);
    }

    /// <summary>
    /// The effective set is a concatenation, so a type named by both halves multiplies twice.
    /// </summary>
    /// <remarks>
    /// <c>Spell.GetAllSpellTypes()</c> is <c>GetNotSpellTypes().Concat(augmentedSpellTypes)</c> and
    /// never distinctifies. <c>notSpellTypes</c> is empty on all sixty-five recipes on the audited
    /// build, so today the two readings agree — which is exactly why reproducing it as a set union
    /// would go unnoticed until an authored list stopped being empty.
    /// </remarks>
    [Fact]
    public void TheEffectiveTypeSetConcatenatesRatherThanUnions()
    {
        var type = Guid.NewGuid();
        var recipe = Guid.NewGuid();

        var resonance = WorldSpellTypeResonanceDeriver.Build(
            Slots((0, recipe)),
            SlotTypes((0, type)),
            NotSpellTypes((recipe, type)),
            SpellTypes(SpellType(type, power: 300d)));

        var row = Assert.Single(resonance.AsSpan().ToArray());
        Assert.Equal(2, row.TypeCount);
        Assert.Equal(9d, row.TypePowerPercent.ToDouble(), 9);
    }

    /// <summary>
    /// A slot naming a type the world did not publish gets no product at all.
    /// </summary>
    /// <remarks>
    /// A product short one factor is a smaller number that still reads like an answer, and the
    /// consumer this exists for is arithmetic. Fail closed, and let the absence say so.
    /// </remarks>
    [Fact]
    public void ASlotNamingAnUnpublishedTypePublishesNoProduct()
    {
        var known = Guid.NewGuid();

        var resonance = WorldSpellTypeResonanceDeriver.Build(
            Slots((0, Guid.NewGuid()), (1, Guid.NewGuid())),
            SlotTypes((0, known), (1, Guid.NewGuid())),
            NoRelations(),
            SpellTypes(SpellType(known, power: 200d)));

        var row = Assert.Single(resonance.AsSpan().ToArray());
        Assert.Equal(0, row.SlotIndex);
    }

    /// <summary>An empty slot has no type layer, because it has no spell.</summary>
    [Fact]
    public void AnEmptySlotHasNoTypeLayer()
    {
        var resonance = WorldSpellTypeResonanceDeriver.Build(
            Empty(new WorldSpellSlot(
                0, Guid.Empty, occupied: false, casting: false, readyingCast: false, attuning: false,
                channeled: false, toggled: false, chargeable: false, castReady: false,
                chargeAvailable: false, resourcesCovered: false, currentCharges: 0,
                maximumCharges: 0, cooldownRemaining: default)),
            SlotTypes(),
            NoRelations(),
            SpellTypes());

        Assert.Equal(0, resonance.Count);
    }

    /// <summary>
    /// For every pair a <c>Register*</c> method wires, the type's total and the member's value are
    /// published under different names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the design's headline rule, encoded rather than documented and hoped for. The thirteen
    /// pairs are <c>StructureTypeSO.RegisterStructure</c>'s, read off the audited assembly, and nine
    /// of them name the type record and the member record <em>identically</em> — which is why
    /// <c>Property</c> cannot carry the distinction and the magnitude's own name has to.
    /// </para>
    /// <para>
    /// A derived total is only ever reachable as <c>DistributedTotal…</c>. A consumer that wrote
    /// <c>total.Power</c> would not compile, which is the cheapest possible form of this check.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("structurePower", "Power")]
    [InlineData("structurePowerScaling", "PowerScaling")]
    [InlineData("structureSpeed", "Speed")]
    [InlineData("passiveCostMod", "PassiveCostMod")]
    [InlineData("activeCostMod", "ActiveCostMod")]
    [InlineData("costScalingMod", "CostScalingMod")]
    [InlineData("attributeRankEffectMod", "AttributeRankEffectMod")]
    [InlineData("drainCostMod", "DrainCostMod")]
    [InlineData("buildSpeedMod", "BuildSpeed")]
    [InlineData("echoBuildRating", "EchoBuildRating")]
    [InlineData("powerBuildRating", "PowerBuildRating")]
    [InlineData("bonusLevels", "BonusLevels")]
    [InlineData("effectLevels", "EffectLevels")]
    public void ATypeTotalNeverSharesANameWithTheMemberValueItSitsIn(
        string typeRecord, string memberValue)
    {
        Assert.NotNull(typeof(RawStructureModifiers).GetProperty(memberValue, Members));

        Assert.Null(typeof(WorldTypeModifierTotal).GetProperty(memberValue, Members));
        Assert.Null(typeof(WorldKeywordModifier).GetProperty(memberValue, Members));

        var totals = WorldTypeModifierTotalDeriver.Build(
            Records((Guid.NewGuid(), typeRecord, Ordered)),
            Contributions());

        var magnitudes = typeof(WorldTypeModifierTotal)
            .GetProperties(Members)
            .Where(property => property.PropertyType == typeof(BigDouble))
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            new[] { "DistributedTotalMultiplier", "DistributedTotalPercent" }, magnitudes);
        Assert.Equal(typeRecord, Assert.Single(totals.AsSpan().ToArray()).Property);
    }

    private const BindingFlags Members =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private enum Kind
    {
        Raw = 0,
        MultiDiminishing = 1,
        MultiStacking = 2,
        Reduction = 3,
        Exponent = 4,
    }

    private static WorldTypeModifierTotal Total(
        PublicationTable<WorldTypeModifierTotal> totals, Guid typeId, string property)
    {
        Assert.True(
            WorldTypeModifierTotalLookup.TryFindProperty(totals, typeId, property, out var total));
        return total;
    }

    private static PublicationTable<WorldTypeModifier> Records(
        params (Guid TypeId, string Property, string RecordNativeType)[] records)
    {
        var rows = records
            .Select(record => new WorldTypeModifier(
                record.TypeId, WorldTypeModifierOwnerKind.StructureType, record.Property,
                record.RecordNativeType, 0, 0))
            .ToArray();
        Array.Sort(rows, static (left, right) =>
        {
            var type = left.TypeId.CompareTo(right.TypeId);
            return type != 0 ? type : string.CompareOrdinal(left.Property, right.Property);
        });
        return rows.Length == 0
            ? PublicationTable<WorldTypeModifier>.Empty
            : PublicationTable<WorldTypeModifier>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldTypeModifierContribution> Contributions(
        params (Guid TypeId, string Property, Kind Kind, double Amount, int Order)[] entries)
    {
        var rows = entries
            .Select(entry => new WorldTypeModifierContribution(
                entry.TypeId,
                entry.Property,
                new WorldResearchRequirementAdjustment(
                    Guid.NewGuid(), Guid.NewGuid(), "UpgradeSO", (int)entry.Kind,
                    new BigDouble(entry.Amount), entry.Order, passive: false)))
            .ToArray();
        Array.Sort(rows, static (left, right) =>
        {
            var type = left.TypeId.CompareTo(right.TypeId);
            if (type != 0) return type;
            var property = string.CompareOrdinal(left.Property, right.Property);
            return property != 0
                ? property
                : left.Contribution.ModifierId.CompareTo(right.Contribution.ModifierId);
        });
        return rows.Length == 0
            ? PublicationTable<WorldTypeModifierContribution>.Empty
            : PublicationTable<WorldTypeModifierContribution>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldEntityKeyword> Keywords(
        params (Guid OwnerId, WorldKeywordOwnerKind Kind, Guid KeywordId)[] keywords)
    {
        var rows = keywords
            .Select(keyword => new WorldEntityKeyword(
                keyword.OwnerId, keyword.Kind, WorldKeywordSource.PrimaryType, 0, keyword.KeywordId))
            .ToArray();
        Array.Sort(rows, static (left, right) => left.OwnerId.CompareTo(right.OwnerId));
        return rows.Length == 0
            ? PublicationTable<WorldEntityKeyword>.Empty
            : PublicationTable<WorldEntityKeyword>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldTypeSubtype> Subtypes(
        params (Guid Parent, Guid Child)[] edges)
    {
        var rows = edges
            .Select((edge, index) => new WorldTypeSubtype(edge.Parent, index, edge.Child))
            .ToArray();
        return rows.Length == 0
            ? PublicationTable<WorldTypeSubtype>.Empty
            : PublicationTable<WorldTypeSubtype>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldSpellSlot> Slots(params (int Index, Guid Recipe)[] slots)
    {
        var rows = slots
            .Select(slot => new WorldSpellSlot(
                slot.Index, slot.Recipe, occupied: true, casting: false, readyingCast: false,
                attuning: false, channeled: false, toggled: false, chargeable: false,
                castReady: true, chargeAvailable: true, resourcesCovered: true, currentCharges: 1,
                maximumCharges: 1, cooldownRemaining: default))
            .ToArray();
        return PublicationTable<WorldSpellSlot>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldSpellSlot> Empty(WorldSpellSlot slot) =>
        PublicationTable<WorldSpellSlot>.Create(new[] { slot }, 1);

    private static PublicationTable<WorldSpellSlotType> SlotTypes(
        params (int Slot, Guid TypeId)[] types)
    {
        var rows = types
            .Select((type, index) => new WorldSpellSlotType(type.Slot, index, type.TypeId))
            .ToArray();
        return rows.Length == 0
            ? PublicationTable<WorldSpellSlotType>.Empty
            : PublicationTable<WorldSpellSlotType>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldSpellRelation> NoRelations() =>
        PublicationTable<WorldSpellRelation>.Empty;

    private static PublicationTable<WorldSpellRelation> NotSpellTypes(
        params (Guid Recipe, Guid TypeId)[] relations)
    {
        var rows = relations
            .Select((relation, index) => new WorldSpellRelation(
                relation.Recipe, WorldSpellRelationKind.NotSpellType, index, relation.TypeId))
            .ToArray();
        return PublicationTable<WorldSpellRelation>.Create(rows, rows.Length);
    }

    private static PublicationTable<WorldSpellType> SpellTypes(params WorldSpellType[] types)
    {
        var rows = types.ToArray();
        Array.Sort(rows, static (left, right) => left.SpellTypeId.CompareTo(right.SpellTypeId));
        return rows.Length == 0
            ? PublicationTable<WorldSpellType>.Empty
            : PublicationTable<WorldSpellType>.Create(rows, rows.Length);
    }

    private static WorldSpellType SpellType(
        Guid id,
        double power = 100d,
        double costMod = 100d,
        double cooldownSpeed = 100d,
        double elementalResonance = 100d,
        bool isElemental = false) =>
        new(
            id, 0, default, 0d, 0d, false, isElemental, false, false, true, false,
            default, new BigDouble(power), new BigDouble(cooldownSpeed), default,
            new BigDouble(costMod), default, default, new BigDouble(elementalResonance), default,
            default, default, default, default, default, default, default, default, default,
            default, default, default, default);
}

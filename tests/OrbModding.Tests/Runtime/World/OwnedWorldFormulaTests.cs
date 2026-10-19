using System;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class OwnedWorldFormulaTests
{
    private static readonly Guid Recipe = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceA = Guid.Parse("20000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceB = Guid.Parse("20000000-0000-0000-0000-000000000002");

    [Fact]
    public void MasteryLevelOneCopiesRowsIndependentlyThenRoundsEachToTwoSignificantDigits()
    {
        var world = MasteryWorld(
            masteryLevel: 0,
            new RawMasteryCost(Recipe, 0, ResourceA, new BigDouble(1234)),
            new RawMasteryCost(Recipe, 1, ResourceA, new BigDouble(1350)));

        Assert.Equal(2, world.Count);
        Assert.Equal(1200, world[0].Amount.ToDouble());
        Assert.Equal(1400, world[1].Amount.ToDouble());
        Assert.Equal(0, world[0].Position);
        Assert.Equal(1, world[1].Position);
    }

    [Fact]
    public void MasteryRowsApplyThePerLevelProgramIndependently()
    {
        var world = MasteryWorld(
            masteryLevel: 2,
            new RawMasteryCost(Recipe, 0, ResourceA, new BigDouble(10)),
            new RawMasteryCost(Recipe, 1, ResourceB, new BigDouble(20)),
            perLevelRaw: 5);

        Assert.Equal(20, world[0].Amount.ToDouble());
        Assert.Equal(30, world[1].Amount.ToDouble());
    }

    [Fact]
    public void MasteryScalingRefusesARecordShapedStandardInsteadOfUsingUnscaledCosts()
    {
        var world = MasteryWorld(
            masteryLevel: 1,
            new RawMasteryCost(Recipe, 0, ResourceA, new BigDouble(10)),
            standardIsRecord: true);

        Assert.Equal(0, world.Count);
    }

    [Fact]
    public void OrdinaryAffordabilityDividesByQualityAndIgnoresReservation()
    {
        var resource = Resource(quantity: 4, capacity: 100, quality: 200, bandwidth: false);

        Assert.True(OwnedMasteryCostMath.HasAmount(in resource, new BigDouble(8)));
        Assert.False(OwnedMasteryCostMath.HasAmount(in resource, new BigDouble(8.01)));
    }

    [Fact]
    public void BandwidthAffordabilitySnapFloorsHeadroomAndAmount()
    {
        var resource = Resource(quantity: 5.9995, capacity: 10.999, quality: 1, bandwidth: true);

        Assert.True(OwnedMasteryCostMath.HasAmount(in resource, new BigDouble(4.999)));
        Assert.False(OwnedMasteryCostMath.HasAmount(in resource, new BigDouble(6.001)));
    }

    [Fact]
    public void AMissingMasteryCostListFailsClosed()
    {
        var rows = MasteryWorld(masteryLevel: 0);
        var raw = SpellSample(0);
        var published = new WorldSpellRecipeDeriver(rows).Derive(in raw);

        Assert.False(published.MasteryLevelAffordable);
    }

    [Fact]
    public void ConceptDrainCoversRarityBlacklistPrerequisiteLevelAndOverdriveBranches()
    {
        var normal = ConceptWorld(
            selectedLevel: 3,
            requirementsMet: false,
            costUsesRarity: true,
            rarity: 2,
            freeSlots: 2,
            costScaleRaw: 1,
            levelRaw: 0.5,
            prerequisiteRaw: 1,
            overdriveSpeedPercent: 200);
        Assert.True(OwnedConceptDrainMath.TryComputeModifier(normal, Recipe, 3, out var normalValue));

        var blacklisted = ConceptWorld(
            selectedLevel: 3,
            requirementsMet: true,
            costUsesRarity: false,
            rarity: 2,
            freeSlots: 2,
            costScaleRaw: 1,
            levelRaw: 0.5,
            prerequisiteRaw: 1,
            overdriveSpeedPercent: 100);
        Assert.True(OwnedConceptDrainMath.TryComputeModifier(
            blacklisted, Recipe, 3, out var blacklistedValue));

        // normal: base 100 * requirement-cost 2 * requirement-speed 2 * level 2,
        // rarity-adjusted cost scaling 7, and one overdrive at 2x => 11200.
        Assert.Equal(11200, normalValue.ToDouble());
        // blacklisted: base 100 * level 2 * ordinary q=3 scaling 3, no penalties/overdrive.
        Assert.Equal(600, blacklistedValue.ToDouble());
    }

    [Fact]
    public void ConceptDrainRefusesAnUnevaluableUsageProgramInsteadOfGuessingThePenalty()
    {
        var world = ConceptWorld(
            selectedLevel: 1,
            requirementsMet: null,
            costUsesRarity: false,
            rarity: 0,
            freeSlots: 0,
            costScaleRaw: 0,
            levelRaw: 0,
            prerequisiteRaw: 2,
            overdriveSpeedPercent: 100);

        Assert.False(OwnedConceptDrainMath.TryComputeModifier(world, Recipe, 1, out _));
    }

    [Fact]
    public void MissingSelectedLevelDefaultsToOne()
    {
        var raw = new RawConceptDrainBasisBuffer();
        raw.Append(new RawConceptDrainBasis(
            Recipe, Guid.NewGuid(), Recipe, 0, default, default, false, false));
        var rows = WorldConceptDrainBasisDeriver.Build(
            raw,
            PublicationTable<WorldAlchemyType>.Empty,
            PublicationTable<WorldNumberVariable>.Empty,
            PublicationTable<WorldAlchemyCost>.Empty,
            PublicationTable<WorldResource>.Empty);

        Assert.Equal(1, rows[0].SelectedLevel);
    }

    private static PublicationTable<WorldMasteryCost> MasteryWorld(
        int masteryLevel,
        RawMasteryCost first = default,
        RawMasteryCost second = default,
        double perLevelRaw = 0,
        bool standardIsRecord = false)
    {
        var costs = new RawMasteryCostBuffer();
        if (first.ResourceId != Guid.Empty) costs.Append(in first);
        if (second.ResourceId != Guid.Empty) costs.Append(in second);
        var spells = new WorldSampleBuffer<RawSpellRecipeSample, WorldSpellRecipe>();
        var spell = SpellSample(masteryLevel);
        spells.Append(in spell);
        var program = new WorldModifierProgram(
            Recipe, WorldModifierProgramRole.SpellLevelingStandard,
            standardIsRecord, 0, false, default);
        var programs = PublicationTable<WorldModifierProgram>.Create(new[] { program });
        var entries = perLevelRaw == 0
            ? PublicationTable<WorldModifierProgramEntry>.Empty
            : PublicationTable<WorldModifierProgramEntry>.Create(new[]
            {
                new WorldModifierProgramEntry(
                    Recipe, WorldModifierProgramRole.SpellLevelingStandard,
                    WorldModifierProgramEntrySet.Modifier, 0, Guid.Empty,
                    GameValueModifierType.Raw, 0, new BigDouble(perLevelRaw)),
            });
        return OwnedMasteryCostMath.Build(
            costs, spells, Recipe, programs, entries, PublicationTable<WorldResource>.Empty);
    }

    private static RawSpellRecipeSample SpellSample(int masteryLevel) =>
        new(
            Recipe, true, 0, default, masteryLevel, true, false, false, 0, 1, 1, false,
            default, default, default, default, default, default, false);

    private static GameWorldState ConceptWorld(
        int selectedLevel,
        bool? requirementsMet,
        bool costUsesRarity,
        double rarity,
        int freeSlots,
        double costScaleRaw,
        double levelRaw,
        double prerequisiteRaw,
        double overdriveSpeedPercent)
    {
        var programs = new[]
        {
            Record(WorldModifierProgramRole.ConceptDrain, 100),
            Record(WorldModifierProgramRole.ConceptSpeed, 100),
            Record(WorldModifierProgramRole.ConceptFreeUsageSlots, freeSlots),
            Record(WorldModifierProgramRole.ConceptOverdriveSpeed, overdriveSpeedPercent),
            Record(WorldModifierProgramRole.ConceptOverdriveDrain, 100),
            List(WorldModifierProgramRole.ConceptCompletionCost),
            List(WorldModifierProgramRole.ConceptDrainLevel),
            List(WorldModifierProgramRole.InstanceScalingCost),
            List(WorldModifierProgramRole.InstanceScalingSpeed),
        };
        var entries = new[]
        {
            Entry(WorldModifierProgramRole.ConceptDrainLevel, levelRaw),
            Entry(WorldModifierProgramRole.InstanceScalingCost, costScaleRaw),
        };
        Array.Sort(programs, static (left, right) => ((int)left.Role).CompareTo((int)right.Role));
        Array.Sort(entries, static (left, right) => ((int)left.Role).CompareTo((int)right.Role));
        var penalty = new GameValueModifier(
            GameValueModifierType.Raw, new BigDouble(prerequisiteRaw));
        var basis = new WorldConceptDrainBasis(
            Recipe, Guid.NewGuid(), Recipe, 0, selectedLevel,
            in penalty, in penalty, new BigDouble(rarity), costUsesRarity, false);
        var requirement = new WorldEntityRequirement(
            Recipe,
            WorldRequirementOwnerKind.AlchemyRecipe,
            0,
            requirementsMet.HasValue
                ? WorldRequirementConditionKind.Literal
                : WorldRequirementConditionKind.Unknown,
            requirementsMet.HasValue ? "test-literal" : "OpaqueRequirement",
            Guid.Empty,
            requirementsMet == true ? 1 : 0,
            0,
            default,
            default,
            WorldRequirementProgramKind.Usage);
        return new GameWorldState
        {
            ModifierPrograms = PublicationTable<WorldModifierProgram>.Create(programs),
            ModifierProgramEntries = PublicationTable<WorldModifierProgramEntry>.Create(entries),
            ConceptDrainBasis = PublicationTable<WorldConceptDrainBasis>.Create(new[] { basis }),
            EntityRequirements = PublicationTable<WorldEntityRequirement>.Create(new[] { requirement }),
        };
    }

    private static WorldModifierProgram Record(WorldModifierProgramRole role, double memo) =>
        new(Recipe, role, true, memo, false, new BigDouble(memo));

    private static WorldModifierProgram List(WorldModifierProgramRole role) =>
        new(Recipe, role, false, 0, false, default);

    private static WorldModifierProgramEntry Entry(WorldModifierProgramRole role, double amount) =>
        new(
            Recipe, role, WorldModifierProgramEntrySet.Modifier, 0, Guid.Empty,
            GameValueModifierType.Raw, 0, new BigDouble(amount));

    private static WorldResource Resource(double quantity, double capacity, double quality, bool bandwidth)
    {
        var traits = new RawResourceTraits(
            0, 0, 0, false, false, false, bandwidth, false, false, false,
            default, 0, 0, 0, false, 0, default, default, default, default, false);
        var reading = new RawResourceSample(
            ResourceA, new BigDouble(quantity), new BigDouble(capacity), true,
            default, default, new BigDouble(quality), default, default,
            new BigDouble(999), default, false, false, false, 0, Guid.Empty,
            default, in traits, default);
        return new WorldResource(
            in reading, true, new BigDouble(capacity - quantity), 0, false,
            new BigDouble(quantity), default);
    }
}

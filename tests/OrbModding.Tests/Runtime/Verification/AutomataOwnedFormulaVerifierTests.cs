using System;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

public sealed class AutomataOwnedFormulaVerifierTests
{
    private static readonly Guid ConceptId = Guid.Parse("50000000-0000-0000-0000-000000000001");
    private static readonly Guid SpellId = Guid.Parse("60000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = Guid.Parse("70000000-0000-0000-0000-000000000001");

    [Fact]
    public void OwnedSpellCostAgreementClassifiesAsAgreement()
    {
        var result = VerifySpell(ownedAmount: 10, nativeAmount: 10);

        Assert.Equal(VerificationVerdict.Agree, result.Verdict);
    }

    [Fact]
    public void OwnedSpellCostDivergenceClassifiesAsDisagreement()
    {
        var result = VerifySpell(ownedAmount: 11, nativeAmount: 10);

        Assert.Equal(VerificationVerdict.Disagree, result.Verdict);
        Assert.Contains(
            result.Detail,
            row => row.Contains("SpellRecipeSO term=cost", StringComparison.Ordinal));
    }

    [Fact]
    public void UnreadableOwnedSpellOracleClassifiesAsInconclusive()
    {
        var verifier = new AutomataSpellLevelVerifier(typeof(object), typeof(object));
        var session = new DifferentialVerificationSession("Owned spell level cost");
        session.Start();

        var verified = verifier.TryVerifyCost(
            new object(), new GameWorldState(), session.Run, session, out var failure);
        if (verified) session.RecordVerified();
        else session.RecordUnverifiable(failure);
        session.EndTick();

        Assert.Equal(VerificationVerdict.Inconclusive, session.Complete().Verdict);
    }

    [Fact]
    public void OwnedFormulaOraclesFailClosedWhenNativeShapesMove()
    {
        Assert.False(new AutomataSpellLevelVerifier(typeof(object), typeof(object)).IsAvailable);
        Assert.False(new AutomataConceptDrainVerifier(typeof(object), typeof(object)).IsAvailable);
        Assert.False(new AutomataSpellTypeLayerVerifier(typeof(object)).IsAvailable);
    }

    /// <summary>
    /// The oracle returns <c>BigDouble</c> and the comparison is only meaningful against that exact
    /// shape. A build answering the same question in a plain <c>double</c> is a different reading,
    /// and taking it would compare the layer against a silently narrowed number.
    /// </summary>
    [Fact]
    public void TheSpellTypeLayerOracleRefusesAMovedReturnType()
    {
        Assert.True(new AutomataSpellTypeLayerVerifier(typeof(TypeLayerSpell)).IsAvailable);
        Assert.False(new AutomataSpellTypeLayerVerifier(typeof(DriftedTypeLayerSpell)).IsAvailable);
    }

    [Fact]
    public void SpellTypeLayerAgreementClassifiesAsAgreement()
    {
        var session = VerifyTypeLayer(ours: 4.5d, theirs: 4.5d);

        Assert.Equal(VerificationVerdict.Agree, session.Complete().Verdict);
        Assert.Equal(1, session.EntitiesVerified);
        Assert.Equal(1, session.Run.Compared);
    }

    /// <summary>
    /// 4.5 is the product with the resonance exponent applied to the elemental type alone; 9 is what
    /// raising both types would give. The disagreement has to name the position, because a loadout
    /// holding one spell twice has no other way to say which of the two it was.
    /// </summary>
    [Fact]
    public void SpellTypeLayerDivergenceNamesThePositionAndTheTerm()
    {
        var session = VerifyTypeLayer(ours: 9d, theirs: 4.5d);
        var finding = session.Complete();

        Assert.Equal(VerificationVerdict.Disagree, finding.Verdict);
        var sample = Assert.Single(session.Run.Failures);
        Assert.Equal("Spell slot=0 term=type-power-percent", sample.Aspect);
        Assert.Equal(SpellId, sample.EntityId);
        Assert.Equal(DifferentialOutcome.Mismatch, sample.Outcome);
    }

    /// <summary>
    /// An empty position has no types to multiply, and a loadout of them reports that it verified
    /// nothing rather than inventing an agreement out of the skips.
    /// </summary>
    [Fact]
    public void AnEmptyLoadoutPositionIsAnExpectedNamedSkip()
    {
        var world = new GameWorldState
        {
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[] { Slot(0, occupied: false) }),
        };

        var session = VerifyPosition(world, slotIndex: 0, new TypeLayerSpell(4.5d), out var failure);

        Assert.Equal(string.Empty, failure);
        Assert.Equal(1, session.ExpectedSkips);
        Assert.Equal(0, session.EntitiesVerified);
        Assert.Equal(0, session.Run.Compared);
        Assert.Equal(
            "Spell type layer INCONCLUSIVE: nothing could be verified — 1 entities were expected skips.",
            session.Complete().Headline());
    }

    /// <summary>
    /// The deriver fails closed on a slot naming a type the world did not publish. That absence is
    /// the finding: a position the game will happily answer for and the suite cannot is exactly the
    /// gap this pass exists to surface.
    /// </summary>
    [Fact]
    public void AnOccupiedPositionWithNoDerivedLayerStaysUnverifiable()
    {
        var world = new GameWorldState
        {
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[] { Slot(2, occupied: true) }),
        };

        var session = VerifyPosition(world, slotIndex: 2, new TypeLayerSpell(4.5d), out var failure);

        Assert.Equal("spell slot 2 published no derived spell type layer.", failure);
        Assert.Equal(1, session.Unverifiable);
        Assert.Equal(0, session.ExpectedSkips);
    }

    [Fact]
    public void APositionTheWorldNeverPublishedStaysUnverifiable()
    {
        var session = VerifyPosition(
            new GameWorldState(), slotIndex: 0, new TypeLayerSpell(4.5d), out var failure);

        Assert.Equal("spell slot 0 was absent from the immutable world.", failure);
        Assert.Equal(1, session.Unverifiable);
    }

    private static DifferentialVerificationSession VerifyTypeLayer(double ours, double theirs)
    {
        var world = new GameWorldState
        {
            SpellSlots = PublicationTable<WorldSpellSlot>.Create(new[] { Slot(0, occupied: true) }),
            SpellTypeResonance = PublicationTable<WorldSpellTypeResonance>.Create(new[]
            {
                new WorldSpellTypeResonance(
                    0, SpellId, 2, new BigDouble(2), true,
                    new BigDouble(ours), BigDouble.One, BigDouble.One),
            }),
        };

        return VerifyPosition(world, slotIndex: 0, new TypeLayerSpell(theirs), out _);
    }

    private static DifferentialVerificationSession VerifyPosition(
        GameWorldState world,
        int slotIndex,
        object spell,
        out string failure)
    {
        var verifier = new AutomataSpellTypeLayerVerifier(spell.GetType());
        var session = new DifferentialVerificationSession("Spell type layer");
        session.Start();

        if (verifier.TryVerify(spell, slotIndex, world, session.Run, session, out failure))
        {
            session.RecordVerified();
        }
        else
        {
            session.RecordUnverifiable(failure);
        }

        session.EndTick();
        return session;
    }

    private static WorldSpellSlot Slot(int slotIndex, bool occupied) =>
        new(
            slotIndex, occupied ? SpellId : Guid.Empty, occupied,
            false, false, false, false, false, false, true, true, true, 0, 0, default);

    public sealed class TypeLayerSpell
    {
        private readonly BigDouble _percent;

        public TypeLayerSpell(double percent) => _percent = new BigDouble(percent);

        public BigDouble GetSpellTypePowerPercent() => _percent;
    }

    public sealed class DriftedTypeLayerSpell
    {
        public double GetSpellTypePowerPercent() => 4.5d;
    }

    [Fact]
    public void UninstantiatedConceptRecipeIsAnExpectedNamedSkip()
    {
        var verifier = new AutomataConceptDrainVerifier(typeof(DrainRecipe), typeof(DrainInstance));
        var session = new DifferentialVerificationSession("Concept drain");
        session.Start();

        var verified = verifier.TryVerify(
            new DrainRecipe(), new GameWorldState(), session.Run, session, out var failure);
        if (verified) session.RecordVerified();
        else session.RecordUnverifiable(failure);

        Assert.True(verified);
        Assert.Equal(string.Empty, failure);
        Assert.Equal(1, session.ExpectedSkips);
        Assert.Equal(0, session.EntitiesVerified);
        Assert.Equal(0, session.Unverifiable);
    }

    [Fact]
    public void InstantiatedConceptRecipeWithoutBasisRemainsUnverifiable()
    {
        var instances = PublicationTable<WorldAlchemyInstance>.Create(new[]
        {
            new WorldAlchemyInstance(ConceptId, 1, 1, true, BigDouble.One),
        });
        var verifier = new AutomataConceptDrainVerifier(typeof(DrainRecipe), typeof(DrainInstance));
        var session = new DifferentialVerificationSession("Concept drain");
        session.Start();

        var verified = verifier.TryVerify(
            new DrainRecipe(), new GameWorldState { AlchemyInstances = instances },
            session.Run, session, out var failure);
        if (!verified) session.RecordUnverifiable(failure);

        Assert.False(verified);
        Assert.Contains("no immutable owned drain basis", failure, StringComparison.Ordinal);
        Assert.Equal(0, session.ExpectedSkips);
        Assert.Equal(1, session.Unverifiable);
    }

    private static VerificationFinding VerifySpell(double ownedAmount, double nativeAmount)
    {
        var resource = new ResourceSO();
        resource.SetGuid(ResourceId);
        var spell = new SpellRecipeSO { uuid = SpellId.ToString() };
        spell.levelCost.costs.Add(new ResourceTuple(resource, new BigDouble(nativeAmount)));
        var published = new WorldSpellRecipe(
            SpellId, true, 0, default, 0, true, true, 1, Guid.Empty,
            false, false, 0, 1, 1, false,
            default, default, default, default, default, default, false);
        var world = new GameWorldState
        {
            SpellRecipes = PublicationTable<WorldSpellRecipe>.Create(new[] { published }),
            MasteryCosts = PublicationTable<WorldMasteryCost>.Create(new[]
            {
                new WorldMasteryCost(
                    SpellId, 0, ResourceId, new BigDouble(ownedAmount), affordable: true),
            }),
        };
        var verifier = new AutomataSpellLevelVerifier(typeof(SpellRecipeSO), typeof(ResourceCostList));
        var session = new DifferentialVerificationSession("Owned spell level cost");
        session.Start();

        var verified = verifier.TryVerifyCost(spell, world, session.Run, session, out var failure);
        if (verified) session.RecordVerified();
        else session.RecordUnverifiable(failure);
        session.EndTick();
        return session.Complete();
    }

    private sealed class DrainRecipe
    {
        public Guid GetGuid() => ConceptId;
        public int GetMaxUsageSlots() => 1;
    }

    private sealed class DrainInstance
    {
        public int quantity = 1;

        public DrainInstance(DrainRecipe recipe) => _ = recipe;

        public BigDouble GetDrainCostMod() => BigDouble.One;
    }
}

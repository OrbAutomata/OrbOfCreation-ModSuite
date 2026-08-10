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

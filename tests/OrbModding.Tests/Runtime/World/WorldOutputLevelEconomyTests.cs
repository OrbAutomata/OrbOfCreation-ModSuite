using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

public sealed class WorldOutputLevelEconomyTests : IDisposable
{
    private static readonly Guid RecipeId = new("a1000000-0000-0000-0000-000000000001");
    private static readonly Guid ResourceId = new("a1000000-0000-0000-0000-000000000002");
    public WorldOutputLevelEconomyTests() => OrbModding.GameStubs.GameStubState.Reset();
    public void Dispose() => OrbModding.GameStubs.GameStubState.Reset();

    [Theory]
    [InlineData(true, 120d, 250d)]
    [InlineData(false, 123d, 253d)]
    public void ShippingSpellReaderPublishesFreshCurrentAndNextCostsWithoutChangingOutput(
        bool requirementsMet, double current, double next)
    {
        Player.Current.spellOutputLevel.Value = 2;
        GlobalValues.instance.spellCostStandard.value.modifiers.Add(new ValueModifier(
            ValueModifier.ValueModifierType.MultiStacking, new BigDouble(2)));
        GlobalValues.instance.insufficientReqCostMod = new ValueModifier(
            ValueModifier.ValueModifierType.Raw, new BigDouble(3));
        var recipe = new SpellRecipeSO { uuid = RecipeId.ToString(), spellCostMod = new ValueModifierRecord(new BigDouble(100)) };
        var resource = new ResourceSO { uuid = ResourceId.ToString() };
        recipe.baseResourceCost.costs.Add(new ResourceTuple(resource, new BigDouble(123.4)));
        var spell = new Spell(recipe) { BaseEffectLevel = 1, NativeUsageRequirementsMet = requirementsMet };
        // Native and owned current prices have settled; the prospective level is derived afresh.
        spell.Cost.costs.Add(new ResourceTuple(resource, new BigDouble(current)));
        var list = new SpellListVariable();
        list.SetGuid(KnownEntities.ActiveSpells.Uuid);
        list.value.Add(spell);
        IdScriptableObject.RuntimeLookup[KnownEntities.ActiveSpells.Uuid] = list;
        var frame = new GameWorldCycleFrame();
        var report = new WorldSpellSlotReader(typeof(IdScriptableObject), typeof(SpellListVariable), Resolve)
            .Collect(new HashSet<Guid>(), frame);
        Assert.Equal(0, report.Skipped);

        var quote = frame.LevelEconomy.Build(PublicationTable<WorldResource>.Empty,
            new WorldSpellWorkbench(1, 10, true, outputLevel: 2, maximumOutputLevel: 5), Resonance(),
            WorldRelationTable.Build(frame.SpellCosts, WorldSpellCostLookup.Order)).Output;

        Assert.True(quote.Complete, quote.Reason);
        Assert.True(quote.HasActiveWork);
        Assert.Equal(current, Assert.Single(quote.Current.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(next, Assert.Single(quote.Next.AsSpan().ToArray()).Amount.ToDouble());
        Assert.Equal(ResourceId, quote.Current[0].ResourceId);
        Assert.Equal(0, quote.Current[0].SlotIndex);
        Assert.Equal(WorldLevelPriceKind.Immediate, quote.Current[0].Kind);
        Assert.Equal(2, Player.Current.spellOutputLevel.AsInt());
        Assert.Equal(current, spell.Cost.costs[0].GetValue().ToDouble());
        Assert.Equal(123.4d, recipe.baseResourceCost.costs[0].GetValue().ToDouble());
        Assert.Equal(0, spell.costMod.GetValueCalls);
        Assert.Equal(0, recipe.spellCostMod.GetValueCalls);
        Assert.Equal(0, Player.Current.spellCost.value.GetValueCalls);
    }

    [Fact]
    public void MissingAnyLoadedSpellResonanceCannotLookLikeAFreeProspectiveCast()
    {
        var capture = new WorldOutputLevelCapture(Resolve);
        capture.Begin();
        var recipe = new SpellRecipeSO { uuid = RecipeId.ToString(), spellCostMod = new ValueModifierRecord(new BigDouble(100)) };
        recipe.baseResourceCost.costs.Add(new ResourceTuple(new ResourceSO { uuid = ResourceId.ToString() }, new BigDouble(1)));
        capture.Capture(new Spell(recipe), 0, Guid.NewGuid(), true);
        var quote = capture.Finish(true).Build(2, 5, PublicationTable<WorldSpellTypeResonance>.Empty);
        Assert.False(quote.Complete);
        Assert.Contains("type-cost layer", quote.Reason);
    }

    [Fact]
    public void StaleNativeCurrentCostBlocksAProspectiveOutputDecision()
    {
        var capture = new WorldOutputLevelCapture(Resolve);
        capture.Begin();
        var recipe = new SpellRecipeSO { uuid = RecipeId.ToString(), spellCostMod = new ValueModifierRecord(new BigDouble(100)) };
        recipe.baseResourceCost.costs.Add(new ResourceTuple(new ResourceSO { uuid = ResourceId.ToString() }, new BigDouble(10)));
        capture.Capture(new Spell(recipe), 0, Guid.NewGuid(), true);
        var quote = capture.Finish(true).Build(2, 5, Resonance(), PublicationTable<WorldSpellCost>.Create(new[]
        { new WorldSpellCost(0, WorldSpellCostKind.Immediate, ResourceId, new BigDouble(9999)) }));
        Assert.False(quote.Complete);
        Assert.Contains("have not settled", quote.Reason);
        Assert.Empty(quote.Next.AsSpan().ToArray());
    }

    [Fact]
    public void KnownCurrentPriceSurvivesAnUnknownNextLevelProgram()
    {
        var sample = new WorldOutputLevelSample(0, Guid.NewGuid(), 1, new BigDouble(100),
            new BigDouble(100), true, PublicationTable<GameResourceCost>.Create(new[]
            { new GameResourceCost(ResourceId, new BigDouble(10)) }));
        var inputs = new WorldOutputLevelInputs(string.Empty,
            PublicationTable<WorldOutputLevelSample>.Create(new[] { sample }),
            PublicationTable<WorldModifierProgram>.Empty, PublicationTable<WorldModifierProgramEntry>.Empty,
            new BigDouble(100), default);

        var quote = inputs.Build(2, 5, Resonance(), PublicationTable<WorldSpellCost>.Create(new[]
        { new WorldSpellCost(0, WorldSpellCostKind.Immediate, ResourceId, new BigDouble(10)) }));

        Assert.True(quote.Complete);
        Assert.Equal(10d, Assert.Single(quote.Current.AsSpan().ToArray()).Amount.ToDouble());
        Assert.False(quote.NextComplete);
        Assert.Contains("next output cost program", quote.NextReason);
        Assert.Empty(quote.Next.AsSpan().ToArray());
    }

    [Fact]
    public void OutputBudgetRateKeepsCurrentDrainAndLossBeforeOverflowDamping()
    {
        var inputs = new GameResourceRateInputs
        {
            Quantity = new BigDouble(200), MaxQuantity = new BigDouble(100), Rate = new BigDouble(20),
            GainRate = new BigDouble(100), Quality = new BigDouble(50), Drain = new BigDouble(2),
            ResetTimePassed = BigDouble.One, ResourceOverflowPercent = BigDouble.One,
            ResourceOverflowLossPercent = BigDouble.One, FixedDeltaTime = 0.02, Visible = true,
        };
        // No active rate modifiers: the native overflow branch clamps positive net rate to zero,
        // but Output's next cast budget still uses the actual 20 - 2/0.5 available stock/s.
        Assert.Equal(0d, GameResourceRateMath.GetTrueRate(in inputs).ToDouble());
        Assert.Equal(16d, GameResourceRateMath.GetUnclampedNetRate(in inputs).ToDouble());
        inputs.Drain = new BigDouble(15);
        Assert.Equal(-10d, GameResourceRateMath.GetUnclampedNetRate(in inputs).ToDouble());
    }

    private static PublicationTable<WorldSpellTypeResonance> Resonance() => PublicationTable<WorldSpellTypeResonance>.Create(new[]
    { new WorldSpellTypeResonance(0, RecipeId, 0, BigDouble.One, false, BigDouble.One, BigDouble.One, BigDouble.One) });
    private static Type? Resolve(string name) => typeof(Spell).Assembly.GetType(name, false);
}

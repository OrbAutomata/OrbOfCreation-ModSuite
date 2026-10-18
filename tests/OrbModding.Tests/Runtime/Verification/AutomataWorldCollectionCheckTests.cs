using System;
using System.Linq;
using OrbAutomata;
using OrbModding.Common.Runtime.GameMath;
using Xunit;

namespace OrbModding.Tests.Runtime.Verification;

public sealed class AutomataWorldCollectionCheckTests : IDisposable
{
    public AutomataWorldCollectionCheckTests() => ResetRegistries();

    public void Dispose() => ResetRegistries();

    /// <summary>
    /// The published-cost verdict is an exact comparison against the game's own price. An entity
    /// whose game price could not be read was not compared, so the verdict covers less than the
    /// world and has to say so.
    /// </summary>
    [Fact]
    public void AnEntityWhoseGamePriceCannotBeReadLeavesThePublishedCostIncomplete()
    {
        var resource = new global::ResourceSO
        {
            uuid = Guid.NewGuid().ToString(),
            quantity = new BigDouble(5.0, 0),
        };
        global::ResourceSO.All.Add(resource);
        var priced = Structure(resource);
        priced.purchaseCost.costs.Add(new global::ResourceTuple(resource, new BigDouble(2.0, 0)));
        var unreadable = Structure(resource);
        unreadable.purchaseCost = null!;

        var finding = new AutomataWorldCollectionCheck().Run().Findings
            .Single(candidate => candidate.Subject == "Published cost");

        Assert.Equal(VerificationVerdict.Incomplete, finding.Verdict);
        Assert.Equal("1 entities had a game price this check could not read", finding.Reason);
    }

    private static global::StructureSO Structure(global::ResourceSO resource)
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        structure.baseCost.costs.Add(new global::ResourceTuple(resource, new BigDouble(2.0, 0)));
        var scaling = new global::ValueModifierVariable
        {
            value = new global::ValueModifier(
                global::ValueModifier.ValueModifierType.Raw, BigDouble.Zero),
        };
        global::ValueModifierVariable.All.Add(scaling);
        structure.costPerQuantity = new global::ValueModifierRef { variable = scaling };
        global::StructureSO.All.Add(structure);
        return structure;
    }

    private static void ResetRegistries()
    {
        global::ResourceSO.All.Clear();
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ValueModifierVariable.All.Clear();
    }
}

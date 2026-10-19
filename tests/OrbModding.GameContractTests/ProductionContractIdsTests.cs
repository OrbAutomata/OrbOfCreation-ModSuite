using OrbModding.Common;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class ProductionContractIdsTests
{
    [Fact]
    public void EveryProductionContractIdIsExactlyOneManifestContract()
    {
        ProductionContractIds.AssertEachIsExactlyOneManifestContract(typeof(PluginIds).Assembly);
    }
}

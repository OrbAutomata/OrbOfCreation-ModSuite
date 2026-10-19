using OrbAutomata.GameMcp;
using OrbModding.Common;
using OrbModding.GameContractTests;
using Xunit;

namespace OrbModding.ProfileTests;

public sealed class ProfileContractIdsTests
{
    [Fact]
    public void EveryProfileBuildContractIdIsExactlyOneManifestContract()
    {
        var owners = ProductionContractIds.AssertEachIsExactlyOneManifestContract(typeof(PluginIds).Assembly);

        Assert.Contains(typeof(ModalOpenGameAction), owners);
    }
}

using OrbAutomata;
using OrbModding.Common.Runtime.Configuration;

namespace OrbModding.TestSupport;

internal static class AutoHarvestConfigurationFactory
{
    internal static SuiteRuntimeConfiguration Create(
        bool masterEnabled,
        bool emergencyDisabled,
        bool activeMode,
        bool fruitSelected,
        bool treasureSelected)
    {
        return new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = masterEnabled },
            Safety = new SuiteSafetyConfiguration { EmergencyDisable = emergencyDisabled },
            AutoHarvest = new AutoHarvestConfiguration
            {
                Mode = activeMode
                    ? AutoHarvestOperationMode.Active
                    : AutoHarvestOperationMode.Disabled,
                CollectFruitTrees = fruitSelected,
                CollectTreasureTrees = treasureSelected,
            },
        };
    }
}

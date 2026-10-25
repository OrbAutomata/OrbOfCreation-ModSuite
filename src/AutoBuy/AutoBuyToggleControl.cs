using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

internal sealed class AutoBuyToggleControl
{
    private readonly AutomataConfigurationStore _configuration;
    private readonly System.Func<AutoSpellLevelCapability> _readSpellLevelCapability;
    private readonly System.Func<FeatureStatusSnapshot> _readStatus;
    private readonly System.Func<FeatureStatusSnapshot> _readSpellLevelStatus;

    public AutoBuyToggleControl(
        AutomataConfigurationStore configuration,
        System.Func<AutoSpellLevelCapability> readSpellLevelCapability,
        System.Func<FeatureStatusSnapshot> readStatus,
        System.Func<FeatureStatusSnapshot> readSpellLevelStatus)
    {
        _configuration = configuration;
        _readSpellLevelCapability = readSpellLevelCapability;
        _readStatus = readStatus;
        _readSpellLevelStatus = readSpellLevelStatus;
    }

    internal SuiteRuntimeConfiguration Config => _configuration.Current;
    internal AutoSpellLevelCapability SpellLevelCapability => _readSpellLevelCapability();
    internal FeatureStatusSnapshot Status => _readStatus();
    internal FeatureStatusSnapshot SpellLevelStatus => _readSpellLevelStatus();

    public bool IsOn =>
        _configuration.Current.AutoBuy.Mode == AutoBuyOperationMode.Active;

    public void Toggle() => _configuration.ToggleAutoBuy();
}

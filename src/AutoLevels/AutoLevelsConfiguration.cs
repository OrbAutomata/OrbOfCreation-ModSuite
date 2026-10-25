using BepInEx.Configuration;

namespace OrbAutomata;

internal enum AutoLevelsOperationMode
{
    Disabled = 0,
    Active = 1,
}

internal sealed record AutoLevelsConfiguration
{
    internal AutoLevelsOperationMode Mode { get; init; }
    internal double SpellCostSeconds { get; init; } = 1d;
    internal double DrainBufferSeconds { get; init; } = 300d;
}

internal static class AutoLevelsSettings
{
    internal static readonly AutomationFeatureSettings Definition = new(
        "Adjusts global levels within the configured spell-cost and resource-buffer budgets.",
        AutomationFeatureSetting.Define("Mode", AutoLevelsOperationMode.Disabled, "Auto Levels",
            "Disabled leaves global levels to the player. Active raises or lowers eligible levels within the configured budgets.", 0,
            static config => config.AutoLevels.Mode,
            static (config, value) => config with { AutoLevels = config.AutoLevels with { Mode = value } }),
        AutomationFeatureSetting.Define("SpellCostSeconds", 1d, "Spell cost (seconds of income)",
            "Maximum immediate cost per loaded spell, measured in seconds of net regeneration after drains. An over-budget current level is lowered; raising requires the next level to fit.", 10,
            static config => config.AutoLevels.SpellCostSeconds,
            static (config, value) => config with { AutoLevels = config.AutoLevels with { SpellCostSeconds = value } },
            new AcceptableValueRange<double>(0d, double.MaxValue)),
        AutomationFeatureSetting.Define("DrainBufferSeconds", 300d, "Drain buffer (seconds)",
            "Minimum time current stock must cover net depletion. A domain that owns most of the resource's drain is lowered when this buffer is short; pressure from other activities holds it.", 20,
            static config => config.AutoLevels.DrainBufferSeconds,
            static (config, value) => config with { AutoLevels = config.AutoLevels with { DrainBufferSeconds = value } },
            new AcceptableValueRange<double>(0d, double.MaxValue)));
}

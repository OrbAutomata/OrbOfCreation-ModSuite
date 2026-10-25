namespace OrbAutomata;

internal enum QualityOfLifeOperationMode
{
    Disabled = 0,
    Active = 1,
}

internal sealed record QualityOfLifeConfiguration
{
    internal QualityOfLifeOperationMode Mode { get; init; }
    internal bool MutePassiveNotifications { get; init; } = true;
}

internal static class QualityOfLifeSettings
{
    internal static readonly AutomationFeatureSettings Definition = new(
        "Applies the selected conveniences to reduce repetitive interruptions.",
        AutomationFeatureSetting.Define("Mode", QualityOfLifeOperationMode.Disabled, "Quality of Life",
            "Disabled leaves convenience settings to the player. Active applies the selected conveniences.", 0,
            static config => config.QualityOfLife.Mode,
            static (config, value) => config with { QualityOfLife = config.QualityOfLife with { Mode = value } }),
        AutomationFeatureSetting.Define("MutePassiveNotifications", true, "Mute passive notifications",
            "Mutes the reflective Learning mutagen popups after their passive has been encountered. Resource gains continue. The game saves mute choices; disabling this option does not unmute them.", 10,
            static config => config.QualityOfLife.MutePassiveNotifications,
            static (config, value) => config with { QualityOfLife = config.QualityOfLife with { MutePassiveNotifications = value } }));
}

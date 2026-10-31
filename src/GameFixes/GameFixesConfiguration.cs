using System.Linq;

namespace OrbAutomata;

internal enum GameFixesOperationMode
{
    Disabled = 0,
    Active = 1,
}

internal sealed record GameFixesConfiguration
{
    internal GameFixesOperationMode Mode { get; init; } = GameFixesOperationMode.Active;
    internal bool SoundPoolGuard { get; init; } = true;
    internal bool MergeRepeatedSounds { get; init; }
    internal bool AutoDevelopmentCostOnce { get; init; }
    internal bool PreciseRunClock { get; init; }
}

internal static class GameFixesSettings
{
    internal static readonly AutomationFeatureSettings Definition = new(
        "Repairs the game's own code where it crashes, makes needless noise, does needless work or keeps time wrongly. A fix never changes what you earn, buy or unlock unless its own description says what changes.",
        new[]
        {
            AutomationFeatureSetting.Define("Mode", GameFixesOperationMode.Active, "Game Fixes",
                "Disabled runs the game's own code untouched. Active applies the selected fixes, also while Automation is off or STOP ALL is engaged. Changes take effect immediately.", 0,
                static config => config.GameFixes.Mode,
                static (config, value) => config with { GameFixes = config.GameFixes with { Mode = value } }),
        }.Concat(GameFixCatalog.Settings).ToArray());
}

using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;

namespace OrbAutomata;

/// <summary>
/// Game Fixes has no service: it installs its patches when the suite installs its own and switches
/// them with the published configuration, whether or not automation runs.
/// </summary>
internal sealed class GameFixesFeatureContribution : AutomataFeatureContribution
{
    internal const string FeatureId = "GameFixes";

    internal GameFixesFeatureContribution() : base(FeatureId, "Game Fixes", null, "GameFixes", "Game Fixes", 19, 9,
        iconIdentity: NativeFeatureIcon.General) { }

    internal override AutomationFeatureSettings Settings => GameFixesSettings.Definition;

    internal override bool StopsWithAutomation => false;

    internal override bool IsConfigured(SuiteRuntimeConfiguration config) =>
        config.GameFixes.Mode == GameFixesOperationMode.Active;

    internal override string? ConfiguredConstraint(SuiteRuntimeConfiguration config) =>
        GameFixCatalog.All.Any(fix => fix.IsSelected(config)) ? null : "No game fix is selected.";

    internal override IAutomataStandingFeature CreateStanding(AutomataStandingComposition context)
    {
        var failures = GameFixCatalog.Install(context.Harmony);
        foreach (var failure in failures)
            context.Warn(failure.Key.Name + " is not installed and stays off: " + failure.Value);
        return new GameFixesHost(GameFixCatalog.All, failures, context.Statuses.Get(Id), context.Configuration,
            context.ConfigurationGeneration);
    }
}

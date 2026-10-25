using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class AutoLevelsServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly AutoLevelsFeatureDependencies _dependencies;
    internal AutoLevelsServiceCycleFeature(AutoLevelsFeatureDependencies dependencies) => _dependencies = dependencies;
    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var gameAction = context.GameActions.Add("game_casting_dial", new SpellCompositionGameAction(
            _dependencies.ReadLifecycleEpoch, _dependencies.TryPermit, _dependencies.OwnershipFailure,
            registry: _dependencies.Registry));
        var registration = context.Registry.Register(AutoLevelsService.Define(gameAction, _dependencies),
            ServiceActionDispatchPolicy.Bounded(1));
        return new AutomationServiceFeatureRuntime<AutoLevelsCycleState, AutoLevelsCycleAction>(
            registration, AutoLevelsService.ServiceId, gameAction, _dependencies.Status,
            _dependencies.OwnsActionFamily, _dependencies.OwnershipFailure, AutoLevelsServiceProjection.ProjectHealth,
            context.LifecycleValue, context.ConfigurationGeneration, _dependencies.Warn,
            describeDetail: AutoLevelsServiceProjection.Describe);
    }
}

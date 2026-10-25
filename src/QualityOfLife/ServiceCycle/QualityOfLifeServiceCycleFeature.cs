using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

internal sealed class QualityOfLifeServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly QualityOfLifeFeatureDependencies _dependencies;
    internal QualityOfLifeServiceCycleFeature(QualityOfLifeFeatureDependencies dependencies) => _dependencies = dependencies;

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        var gameAction = context.GameActions.Add("passive-notifications", new PassiveNotificationMuteGameAction(
            _dependencies.Registry, _dependencies.ReadLifecycleEpoch, _dependencies.TryPermit,
            _dependencies.OwnershipFailure));
        var registration = context.Registry.Register(QualityOfLifeService.Define(gameAction, _dependencies),
            ServiceActionDispatchPolicy.Bounded(1));
        return new AutomationServiceFeatureRuntime<QualityOfLifeCycleState, QualityOfLifeCycleAction>(
            registration, QualityOfLifeService.ServiceId, gameAction, _dependencies.Status,
            _dependencies.OwnsActionFamily, _dependencies.OwnershipFailure, ProjectHealth,
            context.LifecycleValue, context.ConfigurationGeneration, _dependencies.Warn);
    }

    private static (FeatureStatusState State, FeatureStatusReasonCode Reason, string Summary) ProjectHealth(
        ServiceStateProjectionSnapshot projection)
    {
        for (var index = 0; index < projection.Count; index++)
        {
            var entry = projection.GetEntry(index);
            if (entry.Key.Value == QualityOfLifeWorkerDefinition.EvidenceReadyKey &&
                entry.Value.Kind == ServiceProjectionValueKind.Boolean && entry.Value.Boolean)
                return (FeatureStatusState.Operational, FeatureStatusReasonCode.None, string.Empty);
        }
        return (FeatureStatusState.NotReady, FeatureStatusReasonCode.RegistryNotReady,
            "Quality of Life is waiting for a clean passive-ability world publication.");
    }
}

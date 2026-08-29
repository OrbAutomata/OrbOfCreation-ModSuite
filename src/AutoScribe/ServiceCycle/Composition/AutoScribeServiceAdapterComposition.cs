using System;

namespace OrbAutomata;

internal sealed class AutoScribeServiceAdapterComposition
{
    private AutoScribeServiceAdapterComposition(
        IAutomataServiceDefinition<AutoScribeCycleState, AutoScribeCycleAction> definition,
        AutoScribeOneShotCraftGameAction gameAction,
        AutoScribeActionHealth health)
    {
        Definition = definition;
        GameAction = gameAction;
        Health = health;
    }

    internal IAutomataServiceDefinition<AutoScribeCycleState, AutoScribeCycleAction> Definition { get; }
    internal AutoScribeOneShotCraftGameAction GameAction { get; }
    internal AutoScribeActionHealth Health { get; }

    internal static AutoScribeServiceAdapterComposition Create(
        AutoScribeFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var health = new AutoScribeActionHealth();
        var gameAction = new AutoScribeOneShotCraftGameAction(
            dependencies.RegistryResolver,
            dependencies.Profile,
            dependencies.ReadLifecycleEpoch,
            dependencies.TryCaptureMutationPermit,
            dependencies.ReadOwnershipFailure);
        var actionPort = new AutoScribeCycleActionAdapter(
            gameAction,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            health);
        return new AutoScribeServiceAdapterComposition(
            AutoScribeService.Define(
                dependencies.Profile,
                actionPort,
                dependencies.OwnsActionFamily,
                () => gameAction.IsQuarantined),
            gameAction,
            health);
    }
}

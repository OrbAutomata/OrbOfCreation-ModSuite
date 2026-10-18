using System;
using OrbModding.Common;

namespace OrbAutomata;

internal sealed class AutoItemsServiceAdapterComposition
{
    private AutoItemsServiceAdapterComposition(
        IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> definition,
        AutoItemsConsumableUseGameAction gameAction,
        AutoItemsActionHealth health)
    {
        Definition = definition;
        GameAction = gameAction;
        Health = health;
    }

    internal IAutomataServiceDefinition<AutoItemsCycleState, AutoItemsCycleAction> Definition { get; }
    internal AutoItemsConsumableUseGameAction GameAction { get; }
    internal AutoItemsActionHealth Health { get; }

    internal static AutoItemsServiceAdapterComposition Create(
        AutoItemsFeatureDependencies dependencies,
        LifecycleGameActions gameActions)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var health = new AutoItemsActionHealth();
        var gameAction = new AutoItemsConsumableUseGameAction(
            dependencies.RegistryResolver,
            dependencies.TryCaptureMutationPermit,
            dependencies.ReadOwnershipFailure);
#if SERVICE_CYCLE_PROFILE
        gameActions.Add("game_consumable", new AutoItemsConsumablePlayerGameAction(
            dependencies.RegistryResolver,
            dependencies.ReadLifecycleEpoch,
            dependencies.TryCaptureMutationPermit,
            dependencies.ReadOwnershipFailure));
#endif
        var actions = new AutoItemsCycleActionAdapter(
            gameAction,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            dependencies.ReadOwnershipFailure,
            health);
        return new AutoItemsServiceAdapterComposition(
            AutoItemsService.Define(actions),
            gameAction,
            health);
    }
}

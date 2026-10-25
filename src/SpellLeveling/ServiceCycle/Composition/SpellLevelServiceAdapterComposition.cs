using System;
using OrbModding;
using OrbModding.Common;

namespace OrbAutomata;

/// <summary>
/// Builds the Spell Leveling action adapter and the plain typed service definition from the feature's
/// dependencies. There is no capture adapter: the runtime pins the world and the worker reads it, so
/// nothing is left for capture to do.
/// </summary>
internal sealed class SpellLevelServiceAdapterComposition
{
    private SpellLevelServiceAdapterComposition(
        IAutomataServiceDefinition<
            SpellLevelCycleState,
            SpellLevelCycleAction> definition,
        SpellLevelGameAction natives,
        SpellLevelCycleActionAdapter actions,
        SpellLevelBoundaryStatusState boundaryStatus)
    {
        Definition = definition;
        Natives = natives;
        Actions = actions;
        BoundaryStatus = boundaryStatus;
    }

    internal IAutomataServiceDefinition<
        SpellLevelCycleState,
        SpellLevelCycleAction> Definition { get; }

    /// <summary>
    /// The registered action, shared by service submissions and the capability probe.
    /// The lifecycle registry owns its bindings and disposal.
    /// </summary>
    internal SpellLevelGameAction Natives { get; }
    internal SpellLevelCycleActionAdapter Actions { get; }
    internal SpellLevelBoundaryStatusState BoundaryStatus { get; }

    internal static SpellLevelServiceAdapterComposition Create(
        SpellLevelFeatureDependencies dependencies,
        LifecycleGameActions gameActions)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var natives = gameActions.Add("game_level_up", new SpellLevelGameAction(
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            static () => "the spell-level action family is unavailable"));
        var boundaryStatus = new SpellLevelBoundaryStatusState();
        var actions = new SpellLevelCycleActionAdapter(
            natives,
            dependencies.Warn,
            dependencies.Capability.Observe,
            boundaryStatus.Observe);
        return new SpellLevelServiceAdapterComposition(
            SpellLevelService.Define(actions), natives, actions, boundaryStatus);
    }
}

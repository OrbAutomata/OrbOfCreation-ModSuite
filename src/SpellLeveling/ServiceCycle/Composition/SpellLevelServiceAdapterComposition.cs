using System;
using OrbModding;

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
        SpellLevelNativeAdapter natives,
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
    /// The one native adapter, shared by the action boundary and the capability probe. Sharing it is
    /// the point: both need the same bound contracts, and a proven contract mismatch holds for both
    /// until the lifecycle is replaced.
    /// </summary>
    internal SpellLevelNativeAdapter Natives { get; }
    internal SpellLevelCycleActionAdapter Actions { get; }
    internal SpellLevelBoundaryStatusState BoundaryStatus { get; }

    internal static SpellLevelServiceAdapterComposition Create(SpellLevelFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var natives = new SpellLevelNativeAdapter();
        var boundaryStatus = new SpellLevelBoundaryStatusState();
        var actions = new SpellLevelCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            message => Plugin.Log?.LogAutomataWarning(message),
            dependencies.Capability.Observe,
            boundaryStatus.Observe);
        return new SpellLevelServiceAdapterComposition(
            SpellLevelService.Define(actions), natives, actions, boundaryStatus);
    }
}

using System;

namespace OrbAutomata;

/// <summary>
/// Builds the Auto Cast action adapter and the plain typed service definition from the feature's
/// dependencies. There is no capture adapter: the runtime pins the world and the worker reads it, so
/// nothing is left for capture to do.
/// </summary>
internal sealed class AutoCastServiceAdapterComposition
{
    private AutoCastServiceAdapterComposition(
        IAutomataServiceDefinition<
            AutoCastCycleState,
            AutoCastCycleAction> definition,
        AutoCastCycleActionAdapter actions)
    {
        Definition = definition;
        Actions = actions;
    }

    internal IAutomataServiceDefinition<
        AutoCastCycleState,
        AutoCastCycleAction> Definition { get; }
    internal AutoCastCycleActionAdapter Actions { get; }

    internal static AutoCastServiceAdapterComposition Create(AutoCastFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var actions = new AutoCastCycleActionAdapter(
            new AutoCastNativeAdapter(),
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnsActionFamily,
            dependencies.ManualPause,
            dependencies.Warn);
        return new AutoCastServiceAdapterComposition(
            AutoCastService.Define(actions, dependencies.ManualPause), actions);
    }
}

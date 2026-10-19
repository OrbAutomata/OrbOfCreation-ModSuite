using System;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

internal sealed class AutoHarvestServiceAdapterComposition
{
    private AutoHarvestServiceAdapterComposition(
        AutoHarvestBindingResolver bindings,
        IAutomataServiceDefinition<
            AutoHarvestCycleState,
            AutoHarvestCycleAction> definition,
        AutoHarvestCycleActionAdapter actions
#if SERVICE_CYCLE_PROFILE
        , AutomataProfileOperations profileOperations
#endif
        )
    {
        Bindings = bindings;
        Definition = definition;
        Actions = actions;
#if SERVICE_CYCLE_PROFILE
        ProfileOperations = profileOperations;
#endif
    }

    internal AutoHarvestBindingResolver Bindings { get; }
    internal IAutomataServiceDefinition<
        AutoHarvestCycleState,
        AutoHarvestCycleAction> Definition { get; }
    internal AutoHarvestCycleActionAdapter Actions { get; }
#if SERVICE_CYCLE_PROFILE
    internal AutomataProfileOperations ProfileOperations { get; }
#endif

    internal static AutoHarvestServiceAdapterComposition Create(
        AutoHarvestFeatureDependencies dependencies
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));

        var contractCircuit = new AutoHarvestContractCircuit();
#if SERVICE_CYCLE_PROFILE
        var profileOperations = new AutomataProfileOperations(
            profileProbe ?? throw new ArgumentNullException(nameof(profileProbe)));
        var bindings = new AutoHarvestBindingResolver(
            dependencies.RegistryResolver,
            contractCircuit,
            profileOperations);
        var reader = new AutoHarvestNativeStateReader(
            dependencies.RegistryResolver,
            profileOperations);
#else
        var bindings = new AutoHarvestBindingResolver(
            dependencies.RegistryResolver,
            contractCircuit);
        var reader = new AutoHarvestNativeStateReader(dependencies.RegistryResolver);
#endif
        var actions = new AutoHarvestCycleActionAdapter(
            bindings,
            new AutoHarvestMutationAdapter(
                reader
#if SERVICE_CYCLE_PROFILE
                , profileOperations
#endif
                ),
            dependencies.OwnsActionFamily,
            dependencies.TryCaptureMutationPermit,
            dependencies.Warn
#if SERVICE_CYCLE_PROFILE
            , profileOperations,
            bindings
#endif
            );
        return new AutoHarvestServiceAdapterComposition(
            bindings,
            AutoHarvestService.Define(actions),
            actions
#if SERVICE_CYCLE_PROFILE
            , profileOperations
#endif
            );
    }
}

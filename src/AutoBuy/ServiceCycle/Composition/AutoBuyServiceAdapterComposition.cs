using System;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.Runtime.ServiceCycle.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
#endif

namespace OrbAutomata;

/// <summary>
/// Builds the Auto Buy action adapter and the plain typed service definition from the feature's
/// dependencies. World collection and the native purchase boundary both bind the one shared audited
/// owning-view resolver implementation; the runtime pins the resulting world relation and the
/// worker projects it, so there is no separate capture adapter.
/// </summary>
internal sealed class AutoBuyServiceAdapterComposition
{
    private AutoBuyServiceAdapterComposition(
        IAutomataServiceDefinition<
            AutoBuyCycleState,
            AutoBuyCycleAction> definition,
        AutoBuyCycleActionAdapter actions,
        AutoBuyInvariantLatch latch)
    {
        Definition = definition;
        Actions = actions;
        Latch = latch;
    }

    internal IAutomataServiceDefinition<
        AutoBuyCycleState,
        AutoBuyCycleAction> Definition { get; }
    internal AutoBuyCycleActionAdapter Actions { get; }
    internal AutoBuyInvariantLatch Latch { get; }

    internal static AutoBuyServiceAdapterComposition Create(
        AutoBuyFeatureDependencies dependencies
#if SERVICE_CYCLE_PROFILE
        , ServiceCycleProfileProbe profileProbe
#endif
        )
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var latch = new AutoBuyInvariantLatch();
        var refusals = new AutoBuyRefusalResponder(
            latch,
            dependencies.RefusalBundles,
            dependencies.ReportInvariant);

#if SERVICE_CYCLE_PROFILE
        var profileOperations = new AutomataProfileOperations(
            profileProbe ?? throw new ArgumentNullException(nameof(profileProbe)));
        var actions = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(profileOperations, dependencies.Info),
            new AutoBuyNativeQueueRoomAdapter(),
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnershipMask,
            profileOperations,
            refusals,
            dependencies.GameMcpOwnership,
            dependencies.Info,
            dependencies.Warn);
#else
        var actions = new AutoBuyCycleActionAdapter(
            new AutoBuyNativePurchaseAdapter(),
            new AutoBuyNativeQueueRoomAdapter(),
            dependencies.ReadLifecycleEpoch,
            dependencies.OwnershipMask,
            refusals,
            dependencies.Info,
            dependencies.Warn);
#endif
        return new AutoBuyServiceAdapterComposition(AutoBuyService.Define(actions, latch), actions, latch);
    }
}

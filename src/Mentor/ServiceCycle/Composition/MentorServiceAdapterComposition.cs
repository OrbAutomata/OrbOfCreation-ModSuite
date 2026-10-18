using System;
using OrbAutomata;
using OrbModding;

namespace OrbMentor;

internal sealed class MentorServiceAdapterComposition
{
    private MentorServiceAdapterComposition(
        IAutomataServiceDefinition<MentorCycleState, MentorCycleAction> definition,
        MentorNativeAdapter natives)
    {
        Definition = definition;
        Natives = natives;
    }

    internal IAutomataServiceDefinition<MentorCycleState, MentorCycleAction> Definition { get; }
    internal MentorNativeAdapter Natives { get; }

    internal static MentorServiceAdapterComposition Create(
        MentorFeatureDependencies dependencies)
    {
        if (dependencies is null) throw new ArgumentNullException(nameof(dependencies));
        var natives = new MentorNativeAdapter();
        var actions = new MentorCycleActionAdapter(
            natives,
            dependencies.ReadLifecycleEpoch,
            dependencies.CaptureMutationPermit,
            message => Plugin.Log?.LogAutomataWarning(message));
        return new MentorServiceAdapterComposition(MentorService.Define(actions), natives);
    }
}

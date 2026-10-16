#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
#endif

namespace OrbAutomata;

internal interface IAutoHarvestBindingPort
{
    AutoHarvestResolvedPairSet ResolvePairSet();
}

/// <summary>
/// The action boundary's view of the live game: fresh target identity, queue state, every native
/// click-time admission term, and the exact instance it mutates.
/// </summary>
/// <remarks>
/// The immutable publication remains the planning authority. Mutable player-click terms are read
/// again here because planning evidence cannot authorize a later mutation after the world moves.
/// </remarks>
internal interface IAutoHarvestSubmissionStatePort
{
    bool TryResolveCurrentPair(
        in ResolvedAutoHarvestPair resolved,
        out ResolvedAutoHarvestPair current);

    AutoHarvestSubmissionState CaptureSubmissionState(in ResolvedAutoHarvestPair resolved);

    bool IsPairEngaged(in ResolvedAutoHarvestPair resolved);

    /// <summary>
    /// The plot's one instance of the pair's action, or <c>null</c> when the plot holds no such
    /// instance, holds more than one, or holds something this contract cannot identify.
    /// </summary>
    object? ReadPrototype(in ResolvedAutoHarvestPair resolved);

    AutoHarvestSubmissionFailureCode ValidateClickAdmission(
        in ResolvedAutoHarvestPair resolved,
        out object? prototype);
}

internal interface IAutoHarvestMutationPort
{
    AutoHarvestSubmissionResult Submit(
        in ResolvedAutoHarvestPair resolved,
        in AutoHarvestPairFacts facts,
        AutoHarvestActionSafetyState safety
#if SERVICE_CYCLE_PROFILE
        , in ServiceActionContext context
#endif
        );
}

internal interface IAutoHarvestContractCircuit
{
    AutoHarvestNativeFailure FailureFor(AutoHarvestPair pair);
    void Block(AutoHarvestPair pair, AutoHarvestRuntimeFailureScope scope);
}

internal interface IAutoHarvestGatePort
{
    void ObserveResolvedPairs(in AutoHarvestResolvedPairSet pairs);
    bool IsQuarantined(AutoHarvestPair pair);
    void Quarantine(AutoHarvestPair pair);
}

using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Auto Cast. The worker plans at most one cast or one charge
/// release per cycle; the adapter re-resolves the loadout position, re-reads the facts the snapshot
/// cannot publish — whether a target request is open, whether the caster is free, and whether the
/// spell has a target at all — and submits the verified mutation.
/// </summary>
internal interface IAutoCastCycleActionPort
{
    ServiceActionResult TryExecute(
        in AutoCastCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);

    /// <summary>
    /// What the start gate's configuration said about whether the service may run, read once per
    /// cycle. A charge hold the service took is let go on the main thread once it says no.
    /// </summary>
    void ObserveOperational(bool operational);
}

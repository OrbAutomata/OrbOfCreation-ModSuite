using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The native execution boundary for Spell Leveling. The worker plans at most one mastery-level
/// purchase per cycle; the adapter re-resolves the spell, checks the unpublished leveling prerequisite,
/// revalidates readiness and affordability, and submits the verified mutation.
/// </summary>
internal interface ISpellLevelCycleActionPort
{
    ServiceActionResult TryExecute(
        in SpellLevelCycleAction action,
        in SuiteRuntimeConfiguration config,
        in ServiceActionContext context);
}

/// <summary>
/// What the game says this feature is currently able to do, read on the main thread.
/// </summary>
/// <remarks>
/// The worker answers <c>Single</c> or <c>All</c> from the snapshot, but never <c>Locked</c>: whether
/// spell leveling is unlocked at all is the leveling prerequisite, and that is only reachable through
/// the latching no-argument <c>Check()</c> that capture refuses to call (W59). The toggle button's
/// tooltip names the capability, so something main-thread has to be able to answer it — this is that
/// something, consulted from the first cycle of a lifecycle until it answers, rather than per frame.
/// A port that cannot answer names why in <paramref name="failure"/>, a sentence that stays the same
/// while the failure does, and hands back what the game threw as <paramref name="cause"/>.
/// </remarks>
internal interface ISpellLevelCapabilityPort
{
    bool TryReadCapability(out AutoSpellLevelCapability capability, out GameActionReason failure, out Exception? cause);
}

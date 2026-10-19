using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// The automation quantity a failed transition already moved, so a refusal can name what it
/// changed instead of leaving the caller to retry into more damage.
/// </summary>
internal readonly struct CraftingInstanceLifecycleSideEffect
{
    private CraftingInstanceLifecycleSideEffect(int before, int after)
    {
        Observed = true;
        AutomationBefore = before;
        AutomationAfter = after;
    }

    internal bool Observed { get; }
    internal int AutomationBefore { get; }
    internal int AutomationAfter { get; }

    internal static CraftingInstanceLifecycleSideEffect Automation(int before, int after) =>
        new(before, after);
}

internal readonly struct CraftingInstanceLifecycleSubmission
{
    internal CraftingInstanceLifecycleSubmission(
        NativeActionOutcome outcome,
        CraftingInstanceLifecycleSideEffect sideEffect = default)
    {
        Outcome = outcome;
        SideEffect = sideEffect;
    }

    internal static CraftingInstanceLifecycleSubmission Refused(
        ServiceActionResultCode code,
        GameActionReason reason) =>
        new(NativeActionOutcome.Reject(code, reason));

    internal NativeActionOutcome Outcome { get; }
    internal CraftingInstanceLifecycleSideEffect SideEffect { get; }
}

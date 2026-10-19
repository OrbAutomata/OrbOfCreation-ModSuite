using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;

namespace OrbAutomata;

/// <summary>
/// Main-thread evidence of the most recent refusal at the Auto Items action boundary, kept until the
/// next use verifies. Exact native and ownership reasons remain available to status projection
/// instead of being reduced to a result code. A fault is not kept here: the service's retry gate
/// backs that item off and owns its reason.
/// </summary>
internal sealed class AutoItemsActionHealth
{
    internal bool HasFailure { get; private set; }
    internal ServiceActionResultCode Code { get; private set; }
    internal string Reason { get; private set; } = string.Empty;
    internal long Revision { get; private set; }

    internal void Observe(in NativeActionOutcome submission)
    {
        if (submission.Verified || submission.IsFault)
        {
            Clear();
            return;
        }

        var reason = submission.Reason;
        if (string.IsNullOrWhiteSpace(reason))
            reason = "Auto Items failed without a reason.";
        HasFailure = true;
        Code = submission.Code;
        Reason = reason;
        Revision = checked(Revision + 1);
    }

    internal void ObserveOwnership(string reason)
    {
        HasFailure = true;
        Code = AutoItemsActionResultCodes.MutationPermitUnavailable;
        Reason = string.IsNullOrWhiteSpace(reason)
            ? "Auto Items does not own the complete consumable-use transaction."
            : reason;
        Revision = checked(Revision + 1);
    }

    internal void Clear()
    {
        if (!HasFailure && Reason.Length == 0) return;
        HasFailure = false;
        Code = CommonActionResultCodes.Committed;
        Reason = string.Empty;
        Revision = checked(Revision + 1);
    }

    internal void InvalidateLifecycle() => Clear();
}

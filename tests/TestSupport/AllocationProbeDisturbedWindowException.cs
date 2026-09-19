using System;
using System.Collections.Generic;
using System.Globalization;

namespace OrbModding.TestSupport;

/// <summary>
/// Thrown when an allocation probe measured a different byte count in every window it opened for the
/// same warmed work, so it can vouch for none of them and reports the disturbance instead of a
/// number.
/// </summary>
internal sealed class AllocationProbeDisturbedWindowException : Exception
{
    internal AllocationProbeDisturbedWindowException(IReadOnlyList<long> windows)
        : base(
            $"An allocation probe measured {Spell(windows)} bytes in {windows.Count} windows over " +
            "the same warmed work and no two of them agreed, so it can attribute none of the " +
            "numbers to the code it measured. Under load this runtime charges a thread bytes it " +
            "never allocated; every such charge observed was positive, under 8,192 and a multiple " +
            "of eight, the shape of an allocation quantum's unused remainder. A byte count that " +
            "does not reproduce is that disturbance rather than an allocation, and the probe " +
            "reports no byte count it could not measure twice.")
    {
        Windows = windows;
    }

    /// <summary>Every window the probe opened, in the order it measured them.</summary>
    internal IReadOnlyList<long> Windows { get; }

    private static string Spell(IReadOnlyList<long> windows)
    {
        var text = new System.Text.StringBuilder();
        for (var index = 0; index < windows.Count; index++)
        {
            if (index > 0) text.Append(index == windows.Count - 1 ? " and " : ", ");
            text.Append(windows[index].ToString(CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }
}

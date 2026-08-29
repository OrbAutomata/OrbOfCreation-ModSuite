using System;

namespace OrbModding.TestSupport;

/// <summary>
/// Thrown when an allocation probe measured two different byte counts for the same warmed work, so
/// it can vouch for neither and reports the disturbance instead of a number.
/// </summary>
internal sealed class AllocationProbeDisturbedWindowException : Exception
{
    internal AllocationProbeDisturbedWindowException(long measured, long confirmation)
        : base(
            $"An allocation probe measured {measured} bytes and then {confirmation} bytes for the " +
            "same warmed work, so it can attribute neither number to the code it measured. Under " +
            "load this runtime charges a thread bytes it never allocated; every such charge " +
            "observed was positive, under 8,192 and a multiple of eight, the shape of an " +
            "allocation quantum's unused remainder. A byte count that does not reproduce is that " +
            "disturbance rather than an allocation, and the probe reports no byte count it could " +
            "not measure twice.")
    {
        Measured = measured;
        Confirmation = confirmation;
    }

    internal long Measured { get; }

    internal long Confirmation { get; }
}

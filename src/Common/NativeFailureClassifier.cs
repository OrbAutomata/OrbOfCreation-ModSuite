using System;

namespace OrbModding.Common;

/// <summary>
/// The exceptions a native read or call may throw that the suite answers as an unavailable contract
/// or a fault. Anything else is a suite defect and escapes to the boundary that reports it.
/// </summary>
public static class NativeFailureClassifier
{
    public static bool IsExpected(Exception exception) =>
        exception is InvalidOperationException or ArgumentException or OverflowException;
}

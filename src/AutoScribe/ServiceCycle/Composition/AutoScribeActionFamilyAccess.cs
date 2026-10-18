using System;
using OrbModding.Common;

namespace OrbAutomata;

internal static class AutoScribeActionFamilyAccess
{
    internal static bool Owns(Func<bool> readOwnership)
    {
        try { return readOwnership(); }
        catch (Exception ex) when (NativeFailureClassifier.IsExpected(ex))
        {
            return false;
        }
    }
}

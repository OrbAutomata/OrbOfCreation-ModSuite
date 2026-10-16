using System;

namespace OrbAutomata;

internal static class AutoScribeActionFamilyAccess
{
    internal static bool Owns(Func<bool> readOwnership)
    {
        try { return readOwnership(); }
        catch (Exception ex) when (ex is InvalidOperationException or MemberAccessException)
        {
            return false;
        }
    }
}

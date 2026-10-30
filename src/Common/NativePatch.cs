using System;
using System.Reflection;
using HarmonyLib;

namespace OrbModding.Common;

internal static class NativePatch
{
    /// <summary>
    /// Applies one Harmony patch to an audited game method and returns the failure line, or null
    /// when it is installed. A missing target or a patch that throws, such as a transpiler refusing
    /// an unaudited body, leaves the game method as shipped.
    /// </summary>
    internal static string? TryPatch(
        Harmony harmony,
        MethodInfo? target,
        string name,
        HarmonyMethod? prefix = null,
        HarmonyMethod? postfix = null,
        HarmonyMethod? transpiler = null)
    {
        if (target is null) return name + " is unavailable.";
        try
        {
            harmony.Patch(target, prefix: prefix, postfix: postfix, transpiler: transpiler);
            return null;
        }
        catch (Exception exception)
        {
            return name + ": " + ExceptionDetail.Describe(exception);
        }
    }
}

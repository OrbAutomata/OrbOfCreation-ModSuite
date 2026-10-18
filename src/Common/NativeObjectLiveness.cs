namespace OrbModding.Common;

/// <summary>
/// The game's own object-lifetime rule. A destroyed Unity object keeps its C# reference but
/// compares equal to null, and reading through it throws or answers values from a scene that is
/// gone, so a native reference counts as live only when it is neither null nor destroyed.
/// </summary>
internal static class NativeObjectLiveness
{
    internal static bool IsAlive(object? value) =>
        value is not null && (value is not UnityEngine.Object unityObject || unityObject != null);
}

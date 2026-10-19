namespace OrbModding.Common.Runtime.World;

internal static class WorldFailures
{
    internal static string Join(params string[] values)
    {
        var result = string.Empty;
        for (var index = 0; index < values.Length; index++)
        {
            if (values[index].Length == 0) continue;
            result = result.Length == 0 ? values[index] : result + "; " + values[index];
        }
        return result;
    }
}

internal static class WorldSkipTally
{
    internal static void Skip(ref int skipped, ref string firstFailure, string reason)
    {
        skipped++;
        if (firstFailure.Length == 0) firstFailure = reason;
    }
}

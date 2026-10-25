using System;

namespace OrbModding.Common.Runtime.World;

internal static class WorldFailures
{
    internal static WorldCollectionFailure ReadFault(string sentence, Exception exception, Guid targetId = default) =>
        new(GameActionFaultLog.Reason(sentence, exception, "world collection"), targetId,
            "native_read_failed", hasReadFault: true);

    /// <summary>A prerequisite capture did not deliver; reuse its cause rather than logging it twice.</summary>
    internal static WorldCollectionFailure Dependency(string sentence, in WorldCollectionFailure upstream) =>
        new(upstream.Length == 0 ? new GameActionReason(sentence) : upstream.Reason.Around(sentence + ": "),
            upstream.TargetId, "capture_dependency_incomplete", hasReadFault: upstream.HasReadFault);

    internal static WorldCollectionFailure Join(params WorldCollectionFailure[] values)
    {
        var result = default(WorldCollectionFailure);
        foreach (var value in values)
        {
            if (value.Length == 0) { result = result.Include(value); continue; }
            if (result.Length == 0) { result = result.Include(value); continue; }
            var reason = new GameActionReason(result.Reason.Sentence + "; " + value.Reason.Sentence,
                result.Reason.Reference.Length > 0 ? result.Reason.Reference : value.Reason.Reference);
            result = new WorldCollectionFailure(reason,
                result.HasReadFault ? result.TargetId : value.TargetId,
                result.HasReadFault ? result.Code : value.Code, result.HasReadFault || value.HasReadFault);
        }
        return result;
    }

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
    internal static void Skip(ref int skipped, ref WorldCollectionFailure firstFailure, WorldCollectionFailure reason)
    {
        skipped++;
        firstFailure = firstFailure.Include(reason);
    }
}

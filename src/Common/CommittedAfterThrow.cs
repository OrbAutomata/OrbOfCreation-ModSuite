#if SERVICE_CYCLE_PROFILE
using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbModding.Common;

/// <summary>
/// The latest suite-log reference for each feature whose change landed although the game threw.
/// </summary>
/// <remarks>
/// The press itself answered a plain commit, because the change is there and the caller can act on
/// nothing else. A game error that did not stop the change is still a defect to investigate, and
/// <c>suite_health</c> is where a maintainer learns it happened. A reference names an entry in the
/// log <see cref="GameActionFaultLog"/> wrote it to, so pointing that log somewhere else empties this.
/// Each write publishes one new immutable array, so a reader on any thread sees whole entries.
/// </remarks>
internal static class CommittedAfterThrow
{
    private static readonly object Sync = new();
    private static CommittedAfterThrowEntry[] _latest = Array.Empty<CommittedAfterThrowEntry>();

    /// <summary>One entry per feature, ordered by feature name.</summary>
    internal static IReadOnlyList<CommittedAfterThrowEntry> Latest => Volatile.Read(ref _latest);

    /// <summary>
    /// Keeps <paramref name="reference"/> as <paramref name="feature"/>'s latest, replacing the one
    /// before it. An empty reference means the log had nowhere to write, so there is nothing to find.
    /// </summary>
    internal static void Record(string feature, string reference)
    {
        if (reference.Length == 0) return;
        lock (Sync)
        {
            var next = new List<CommittedAfterThrowEntry>(_latest.Length + 1);
            foreach (var entry in _latest)
            {
                if (!string.Equals(entry.Feature, feature, StringComparison.Ordinal)) next.Add(entry);
            }
            next.Add(new CommittedAfterThrowEntry(feature, reference));
            next.Sort((left, right) => string.CompareOrdinal(left.Feature, right.Feature));
            Volatile.Write(ref _latest, next.ToArray());
        }
    }

    internal static void Clear()
    {
        lock (Sync)
        {
            Volatile.Write(ref _latest, Array.Empty<CommittedAfterThrowEntry>());
        }
    }
}

internal readonly struct CommittedAfterThrowEntry
{
    internal CommittedAfterThrowEntry(string feature, string reference)
    {
        Feature = feature;
        Reference = reference;
    }

    internal string Feature { get; }
    internal string Reference { get; }
}
#endif

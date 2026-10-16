using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;

/// <summary>
/// What a recording session calls the things it recorded.
/// </summary>
/// <remarks>
/// <para>
/// The semantic stream identifies a service by a number, because a number is what fits in a fixed
/// record and strings never enter a payload. A number is also unreadable: a reader looking at
/// service 2 has no way to learn it was Auto Harvest. The roster closes that without touching the
/// record, by saying once per session what the numbers meant.
/// </para>
/// <para>
/// Every entry carries the machine identity as well as the display name. The machine identity is the
/// durable one — it is what the runtime actually registered — so a reader that does not recognise a
/// display name, or finds it empty, still has something exact to show instead of a bare ordinal.
/// </para>
/// <para>
/// Entries are kinded rather than assumed to be services. Only services are recorded today, but the
/// same question is coming for the configuration and strategy publications, and a roster that already
/// says what kind of thing each row names answers it without a second artifact.
/// </para>
/// </remarks>
internal readonly struct ServiceCycleTraceRosterEntry
{
    internal ServiceCycleTraceRosterEntry(string kind, ulong identity, string machineId, string displayName)
    {
        if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("A roster kind is required.", nameof(kind));
        if (string.IsNullOrWhiteSpace(machineId))
            throw new ArgumentException("A roster machine identity is required.", nameof(machineId));
        Kind = kind;
        Identity = identity;
        MachineId = machineId;
        DisplayName = displayName ?? string.Empty;
    }

    internal string Kind { get; }
    internal ulong Identity { get; }
    internal string MachineId { get; }
    internal string DisplayName { get; }
}

/// <summary>
/// The roster a recording session writes beside its segments.
/// </summary>
internal sealed class ServiceCycleTraceRoster
{
    internal const string ServiceKind = "service";

    /// <summary>
    /// One world-collection category, identified by its position in the collector's traversal. The
    /// second kind the roster carries, and the reason it was kinded from the start.
    /// </summary>
    internal const string WorldCategoryKind = "world-category";

    internal static readonly ServiceCycleTraceRoster Empty = new(Array.Empty<ServiceCycleTraceRosterEntry>());

    private readonly ServiceCycleTraceRosterEntry[] _entries;

    internal ServiceCycleTraceRoster(ServiceCycleTraceRosterEntry[] entries) =>
        _entries = entries ?? throw new ArgumentNullException(nameof(entries));

    internal int Count => _entries.Length;

    internal ServiceCycleTraceRosterEntry this[int index] => _entries[index];

    internal ReadOnlySpan<ServiceCycleTraceRosterEntry> Entries => _entries;
}

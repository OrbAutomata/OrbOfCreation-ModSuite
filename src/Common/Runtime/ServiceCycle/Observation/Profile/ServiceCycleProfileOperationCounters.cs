#if SERVICE_CYCLE_PROFILE
namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

internal struct ServiceCycleProfileOperationCounters
{
    private ulong _stableIdReads;
    private ulong _listEntries;

    internal void AddStableIdReads(ulong count = 1) => _stableIdReads += count;
    internal void AddListEntries(ulong count = 1) => _listEntries += count;

    internal ServiceCycleProfileOperations Snapshot() => new(_stableIdReads, _listEntries);
}
#endif

#if SERVICE_CYCLE_PROFILE
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;

namespace OrbModding.ServiceCycleTrace.Performance;

internal static class ServiceCycleProfileNames
{
    /// <summary>
    /// The reported name of a span id, from the suite's own enumeration rather than a second table
    /// the tool keeps in step by hand.
    /// </summary>
    internal static string Stage(int stage) => ServiceCycleProfileSpans.Name(stage);
}
#endif

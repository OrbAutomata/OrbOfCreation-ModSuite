using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;

/// <summary>Builds world-collection span payloads.</summary>
/// <remarks>
/// Appended to the suite chain rather than to a service's, for the same reason a publication is: the
/// suite reads the world once and every service shares that reading, so a span belongs to the session
/// rather than to whichever service happened to be holding the cycle it was read under.
/// </remarks>
internal readonly struct ServiceCycleSemanticWorldEmitter
{
    private readonly ServiceCycleSemanticCausalWriter _writer;
    private readonly bool _enabled;

    internal ServiceCycleSemanticWorldEmitter(ServiceCycleSemanticCausalWriter writer, bool enabled)
    {
        _writer = writer;
        _enabled = enabled;
    }

    internal void WorldCategoryCollected(
        int category,
        int sampled,
        int passCategories,
        ulong lifecycle,
        long frameIdentity,
        MonotonicTimestamp observedAt,
        MonotonicDuration elapsed)
    {
        if (!_enabled) return;
        var payload = ServiceCycleSemanticPayload.WorldCategoryFact(
            category,
            sampled,
            passCategories,
            lifecycle,
            frameIdentity,
            observedAt.Ticks,
            elapsed.Ticks);
        _writer.AppendSuite(ServiceCycleSemanticEventKind.WorldCategoryCollected, in payload);
    }
}

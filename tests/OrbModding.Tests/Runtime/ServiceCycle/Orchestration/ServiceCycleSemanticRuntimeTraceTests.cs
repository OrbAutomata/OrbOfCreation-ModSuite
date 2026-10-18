using System;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

public sealed class ServiceCycleSemanticRuntimeTraceTests
{
    /// <summary>
    /// The recent-event buffer is what a bug report reads. When a trace fact throws, the buffer stops
    /// recording for good so tracing cannot alter play, and it keeps what threw so the report says
    /// why its events end where they do.
    /// </summary>
    [Fact]
    public void AFaultedTraceKeepsWhatThrew()
    {
        var recorder = new ServiceCycleSemanticRecorder(
            new ServiceCycleTraceSessionId(701),
            eventCapacity: 64,
            serviceCapacity: 1);
        var trace = new ServiceCycleSemanticRuntimeTrace(recorder, 1);

        trace.Bind(
            3,
            new ServiceId("orbmodding.semantic-trace-fault-test"),
            new ConfigGeneration(1),
            new StrategyGeneration(1),
            new LifecycleGeneration(1),
            lifecycleSemanticVersion: 1,
            new MonotonicTimestamp(1));

        Assert.True(trace.Source.EmissionFaulted);
        Assert.Contains(nameof(ArgumentOutOfRangeException), trace.Source.EmissionFaultCause, StringComparison.Ordinal);
    }
}

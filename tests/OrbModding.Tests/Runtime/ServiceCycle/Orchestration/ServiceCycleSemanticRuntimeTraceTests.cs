using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Lifecycle;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.Common.Runtime.ServiceCycle.Tracing.Emission;
using Xunit;

namespace OrbModding.Tests.Runtime.ServiceCycle.Orchestration;

[Collection(GameActionFaultLogCollection.Name)]
public sealed class ServiceCycleSemanticRuntimeTraceTests : IDisposable
{
    private readonly List<string> _faults = new();
    public ServiceCycleSemanticRuntimeTraceTests() => GameActionFaultLog.ConfigureLog(_faults.Add);
    public void Dispose() => GameActionFaultLog.ConfigureLog(null);

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
        Assert.StartsWith("The recent-event buffer could not record an event. Reference MCP-",
            trace.Source.EmissionFaultCause, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(ArgumentOutOfRangeException), trace.Source.EmissionFaultCause);
        var fault = Assert.Single(_faults);
        Assert.Contains("System.ArgumentOutOfRangeException", fault);
        Assert.Contains(fault.Split(' ')[3], trace.Source.EmissionFaultCause);
    }
}

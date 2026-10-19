using System;
using System.Diagnostics;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

internal static class AvailableServiceDiagnostics
{
    /// <summary>The service's diagnostics from the first read that does not find the worker holding the handoff.</summary>
    internal static ServiceCycleServiceDiagnosticsSnapshot Await(SuiteFramePump pump, ServiceId serviceId)
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            if (ServiceCycleDiagnostics.ReadAvailableService(pump, serviceId) is { } service) return service;
            if (deadline.Elapsed > ServiceCycleTestDeadline.Value)
                throw new TimeoutException($"Service '{serviceId}' had no fresh diagnostics read.");
            Thread.Yield();
        }
    }
}

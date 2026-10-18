using System;
using System.Collections.Generic;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal enum ServiceFailureSite
{
    Start,
    Capture,
    Attribution,
    Execution,
}

internal readonly struct ServiceFailure
{
    internal ServiceFailure(
        int serviceOrdinal,
        ServiceFailureSite site,
        string reason)
    {
        ServiceOrdinal = serviceOrdinal;
        Site = site;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    internal int ServiceOrdinal { get; }
    internal ServiceFailureSite Site { get; }
    internal string Reason { get; }
}

internal sealed class ServiceFailureBuffer
{
    private readonly List<ServiceFailure> _failures = new();

    internal int Count => _failures.Count;

    internal void BeginFrame() => _failures.Clear();

    internal void Observe(
        int serviceOrdinal,
        ServiceFailureSite site,
        string reason) =>
        _failures.Add(new ServiceFailure(
            serviceOrdinal,
            site,
            reason));

    internal ServiceFailure At(int index) => _failures[index];

    internal void Clear() => _failures.Clear();
}

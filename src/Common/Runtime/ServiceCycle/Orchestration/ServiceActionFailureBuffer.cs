using System;
using System.Collections.Generic;

namespace OrbModding.Common.Runtime.ServiceCycle.Orchestration;

internal enum ServiceActionFailureSite
{
    Attribution,
    Execution,
}

internal readonly struct ServiceActionFailure
{
    internal ServiceActionFailure(
        int serviceOrdinal,
        ServiceActionFailureSite site,
        string reason)
    {
        ServiceOrdinal = serviceOrdinal;
        Site = site;
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }

    internal int ServiceOrdinal { get; }
    internal ServiceActionFailureSite Site { get; }
    internal string Reason { get; }
}

internal sealed class ServiceActionFailureBuffer
{
    private readonly List<ServiceActionFailure> _failures = new();

    internal int Count => _failures.Count;

    internal void BeginFrame() => _failures.Clear();

    internal void Observe(
        int serviceOrdinal,
        ServiceActionFailureSite site,
        string reason) =>
        _failures.Add(new ServiceActionFailure(
            serviceOrdinal,
            site,
            reason));

    internal ServiceActionFailure At(int index) => _failures[index];

    internal void Clear() => _failures.Clear();
}

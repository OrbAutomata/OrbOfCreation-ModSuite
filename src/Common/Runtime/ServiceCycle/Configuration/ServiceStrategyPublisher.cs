using System;
using System.Threading;
using OrbModding.Common.Runtime.Strategy;
using RuntimeStrategyGeneration = OrbModding.Common.Runtime.StrategyGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Configuration;

/// <summary>
/// The write half of the shared strategy publication, for the strategist, the one service that
/// decides what the suite wants.
/// </summary>
/// <remarks>
/// Separate from the publisher for the same reason the world's and the configuration's sinks are:
/// reading is the runtime's job, which pins one bulletin per cycle and hands it to the service. A
/// strategist that could also read would be a second, unpinned path to the strategy.
/// </remarks>
public interface IServiceStrategyPublicationSink
{
    RuntimeStrategyGeneration Publish(SuiteStrategy bulletin);
}

/// <summary>One immutable strategy bulletin together with the generation that identifies it.</summary>
public sealed class StrategyPublication
{
    internal StrategyPublication(RuntimeStrategyGeneration generation, SuiteStrategy bulletin)
    {
        Generation = generation;
        Bulletin = bulletin;
    }

    public RuntimeStrategyGeneration Generation { get; }
    public SuiteStrategy Bulletin { get; }
}

/// <summary>
/// The suite's one strategy publication: an atomic slot holding the immutable bulletin every service
/// evaluates against, and the one generation that identifies it.
/// </summary>
/// <remarks>
/// It starts on the neutral bulletin at generation one, which constrains nothing — so a suite with no
/// strategist behaves exactly as it did before strategy was delivered, and a service reads a real
/// bulletin from its first cycle instead of a missing one.
/// </remarks>
public sealed class ServiceStrategyPublisher :
    IDisposable,
    IServiceStrategyPublicationSink
{
    private readonly PublicationLock _sync = new();
    private StrategyPublication _latest;
    private bool _disposed;

    internal ServiceStrategyPublisher(SuiteStrategy initialBulletin)
    {
        if (initialBulletin is null) throw new ArgumentNullException(nameof(initialBulletin));
        _latest = new StrategyPublication(new RuntimeStrategyGeneration(1), initialBulletin);
    }

    public StrategyPublication ReadLatest()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _latest) ??
            throw new ObjectDisposedException(nameof(ServiceStrategyPublisher));
    }

    public RuntimeStrategyGeneration Publish(SuiteStrategy bulletin)
    {
        if (bulletin is null) throw new ArgumentNullException(nameof(bulletin));
        lock (_sync)
        {
            ThrowIfDisposed();
            var next = new StrategyPublication(_latest.Generation.Next(), bulletin);
            Volatile.Write(ref _latest, next);
            return next.Generation;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            Volatile.Write(ref _latest, null!);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed))
            throw new ObjectDisposedException(nameof(ServiceStrategyPublisher));
    }

    private sealed class PublicationLock { }
}

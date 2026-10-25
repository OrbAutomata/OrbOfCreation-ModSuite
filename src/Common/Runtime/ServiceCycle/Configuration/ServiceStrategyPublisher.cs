using System;
using System.Threading;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using RuntimeStrategyGeneration = OrbModding.Common.Runtime.StrategyGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Configuration;

/// <summary>
/// The write half of the shared strategy publication, for whichever service decides what the suite
/// wants. Services receive its immutable value pinned at cycle start.
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

/// <summary>
/// The read half of the strategist's latest immutable bulletin for player-facing surfaces.
/// </summary>
internal interface IStrategistBulletinSource
{
    StrategistBulletin? ReadStrategistBulletin();
    string UnavailableReason { get; }
}

/// <summary>
/// The write half of the strategist's judgment and advisory policy. Replacing only the judgment
/// moves no strategy generation; a changed policy is picked up by the next service cycle.
/// </summary>
internal interface IStrategistBulletinSink
{
    /// <summary>Replaces the latest bulletin and returns its sequence number.</summary>
    ulong PublishStrategistBulletin(StrategistBulletin bulletin, SuiteStrategy strategy);
    void ObserveStrategistFault(in ServiceFault fault);
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
/// bulletin from its first cycle instead of a missing one. Beside it sits the strategist's latest
/// bulletin, a plain latest-value slot for the surfaces that show it.
/// </remarks>
public sealed class ServiceStrategyPublisher :
    IDisposable,
    IServiceStrategyPublicationSink,
    IStrategistBulletinSink,
    IStrategistBulletinSource
{
    private readonly PublicationLock _sync = new();
    private StrategyPublication _latest;
    private StrategistBulletin? _strategistBulletin;
    private ulong _strategistBulletinSequence;
    private string _strategistUnavailableReason = string.Empty;
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

    /// <summary>The strategist's latest bulletin, or null when it has not judged this game yet.</summary>
    internal StrategistBulletin? ReadStrategistBulletin()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _strategistBulletin);
    }

    StrategistBulletin? IStrategistBulletinSource.ReadStrategistBulletin() => ReadStrategistBulletin();
    string IStrategistBulletinSource.UnavailableReason => Volatile.Read(ref _strategistUnavailableReason);

    void IStrategistBulletinSink.ObserveStrategistFault(in ServiceFault fault)
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            if (fault.IsValid && _latest.Bulletin != SuiteStrategyDefaults.Neutral)
                Volatile.Write(ref _latest, new StrategyPublication(_latest.Generation.Next(), SuiteStrategyDefaults.Neutral));
            Volatile.Write(ref _strategistUnavailableReason, ServiceCycleDiagnostics.DescribeFault(in fault));
        }
    }

    ulong IStrategistBulletinSink.PublishStrategistBulletin(StrategistBulletin bulletin, SuiteStrategy strategy)
    {
        if (bulletin is null) throw new ArgumentNullException(nameof(bulletin));
        if (strategy is null) throw new ArgumentNullException(nameof(strategy));
        lock (_sync)
        {
            ThrowIfDisposed();
            if (_latest.Bulletin != strategy)
                Volatile.Write(ref _latest, new StrategyPublication(_latest.Generation.Next(), strategy));
            Volatile.Write(ref _strategistBulletin, bulletin);
            return ++_strategistBulletinSequence;
        }
    }

    /// <summary>
    /// A lifecycle replacement puts the neutral strategy back once and drops the bulletin: both judged
    /// the world that is gone.
    /// </summary>
    internal void ResetForLifecycle()
    {
        lock (_sync)
        {
            ThrowIfDisposed();
            Volatile.Write(
                ref _latest,
                new StrategyPublication(_latest.Generation.Next(), SuiteStrategyDefaults.Neutral));
            Volatile.Write(ref _strategistBulletin, null);
            Volatile.Write(ref _strategistUnavailableReason, string.Empty);
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

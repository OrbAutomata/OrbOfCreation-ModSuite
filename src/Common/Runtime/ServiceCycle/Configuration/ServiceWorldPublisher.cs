using System;
using System.Threading;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using RuntimeWorldGeneration = OrbModding.Common.Runtime.WorldGeneration;

namespace OrbModding.Common.Runtime.ServiceCycle.Configuration;

/// <summary>
/// Answers "which world is currently live" without handing over the world.
/// </summary>
/// <remarks>
/// A service bound to one of these is gated on it: the runtime will not start its cycle while the
/// live world was collected before that service last attempted a game-facing action. Only the
/// generation crosses this seam, so it is a scheduling question rather than a second way to read the
/// world — a cycle still pins its snapshot exactly once, at capture.
/// </remarks>
public interface IServiceWorldGenerationSource
{
    bool TryGetLatestGeneration(out RuntimeWorldGeneration generation);
}

/// <summary>
/// The write half of the shared world publication, handed to whichever service collects the game.
/// </summary>
/// <remarks>
/// Separate from the publisher so a collector cannot read back what it published. Reading is the
/// runtime's job — it pins one snapshot per cycle and hands it to the service — and a collector that
/// could also read would be a second, unpinned path to the world.
/// </remarks>
public interface IServiceWorldPublicationSink<in TWorld>
    where TWorld : notnull
{
    RuntimeWorldGeneration Publish(TWorld snapshot, RuntimeWorldGeneration generation);
}

/// <summary>One immutable world snapshot together with the generation that identifies it.</summary>
public sealed class WorldPublication<TWorld>
    where TWorld : notnull
{
    internal WorldPublication(RuntimeWorldGeneration generation, TWorld snapshot)
    {
        Generation = generation;
        Snapshot = snapshot;
    }

    public RuntimeWorldGeneration Generation { get; }
    public TWorld Snapshot { get; }
}

/// <summary>
/// A separately typed, atomic source of immutable world snapshots: one collection pass publishes,
/// every service reads, latest wins.
/// </summary>
/// <remarks>
/// <para>
/// This is the same bargain configuration and strategy already make, applied to the readings that
/// were previously captured once per service. A consumer that runs before a newer snapshot lands uses
/// the previous one and picks the new one up next cycle, so nothing here introduces cross-service
/// scheduling — ordering between services stays conceptual.
/// </para>
/// <para>
/// The generation is the whole point of publishing rather than sharing a mutable object. There is no
/// value in a service acting twice on the same reading of the world, so a consumer records the
/// generation it last acted on and waits until the published one differs. <see cref="WorldGeneration"/>
/// rejects zero precisely so that a consumer's initial <c>default</c> can never collide with a real
/// publication.
/// </para>
/// <para>
/// The first publication is generation 1 and normally carries an empty world, since collection has
/// not run yet. That is deliberate: a service that starts early reads "nothing known" through the
/// ordinary lookup path and proceeds, rather than blocking on a snapshot that may be a frame away.
/// It also means generation 1 is spoken for: a caller stamping generations with a frame counter must
/// not stamp frame 1, which no real host does — Unity's counter is already in the thousands by the
/// time a save is playable.
/// </para>
/// </remarks>
public sealed class ServiceWorldPublisher<TWorld> :
    IDisposable,
    IServiceWorldGenerationSource,
    IServiceWorldPublicationSink<TWorld>
    where TWorld : notnull
{
    private readonly PublicationLock _sync = new();
    private WorldPublication<TWorld> _latest;
    private bool _disposed;

    public ServiceWorldPublisher(TWorld initialSnapshot)
    {
        ServiceCycleTypeSafetyValidator.EnsureWorldType<TWorld>();
        if (initialSnapshot is null) throw new ArgumentNullException(nameof(initialSnapshot));
        _latest = new WorldPublication<TWorld>(new RuntimeWorldGeneration(1), initialSnapshot);
    }

    public WorldPublication<TWorld> ReadLatest()
    {
        ThrowIfDisposed();
        return Volatile.Read(ref _latest) ??
            throw new ObjectDisposedException(nameof(ServiceWorldPublisher<TWorld>));
    }

    /// <summary>
    /// Replaces the published snapshot and returns the generation that now identifies it. Callers
    /// publish unconditionally: deciding that a snapshot is not worth republishing is a collection
    /// concern, and suppressing it here would leave consumers unable to tell a stalled collector from
    /// an unchanged world.
    /// </summary>
    public RuntimeWorldGeneration Publish(TWorld snapshot)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        lock (_sync)
        {
            ThrowIfDisposed();
            return Swap(new WorldPublication<TWorld>(_latest.Generation.Next(), snapshot));
        }
    }

    /// <summary>
    /// Publishes under a generation the caller chose, which must be strictly newer than the live one.
    /// </summary>
    /// <remarks>
    /// A collector stamps the moment its readings were true, not the moment it finished deriving
    /// them, so that a consumer comparing "has the world moved past my last action" gets the right
    /// answer. Deriving takes frames; a generation minted at publish time would claim the snapshot
    /// was newer than the action it is missing. Monotonicity stays this class's rule either way — the
    /// caller supplies the meaning, not the ordering.
    /// </remarks>
    public RuntimeWorldGeneration Publish(TWorld snapshot, RuntimeWorldGeneration generation)
    {
        if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
        if (!generation.IsValid)
            throw new ArgumentException("A valid world generation is required.", nameof(generation));
        lock (_sync)
        {
            ThrowIfDisposed();
            if (generation.Value <= _latest.Generation.Value)
                throw new ArgumentOutOfRangeException(
                    nameof(generation),
                    "A world generation must be strictly newer than the published one.");
            return Swap(new WorldPublication<TWorld>(generation, snapshot));
        }
    }

    /// <summary>
    /// Returns the publication to the state it was constructed in: generation 1, carrying the empty
    /// world. Called when the run the snapshot describes stops existing.
    /// </summary>
    /// <remarks>
    /// A lifecycle boundary destroys the objects a snapshot was read from, so the snapshot stops
    /// being a reading of anything. The generation deliberately goes backwards to the reserved
    /// generation 1: it is the one value that already means "nothing has been collected", so every
    /// consumer that distinguishes a real publication from the initial one answers "no world" without
    /// learning a second rule. Collection restamps from the frame counter, which is far past 1, so
    /// the next real publication is still strictly newer than this one.
    /// </remarks>
    public void Flush(TWorld emptySnapshot)
    {
        if (emptySnapshot is null) throw new ArgumentNullException(nameof(emptySnapshot));
        lock (_sync)
        {
            ThrowIfDisposed();
            Swap(new WorldPublication<TWorld>(new RuntimeWorldGeneration(1), emptySnapshot));
        }
    }

    private RuntimeWorldGeneration Swap(WorldPublication<TWorld> publication)
    {
        Volatile.Write(ref _latest, publication);
        return publication.Generation;
    }

    bool IServiceWorldGenerationSource.TryGetLatestGeneration(out RuntimeWorldGeneration generation)
    {
        var latest = Volatile.Read(ref _latest);
        if (Volatile.Read(ref _disposed) || latest is null)
        {
            generation = default;
            return false;
        }

        generation = latest.Generation;
        return true;
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
            throw new ObjectDisposedException(nameof(ServiceWorldPublisher<TWorld>));
    }

    private sealed class PublicationLock { }
}

using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Contracts;

/// <summary>
/// What kind of effect an action had on the world outside the worker.
/// </summary>
/// <remarks>
/// <para>
/// The action pipeline was written when pressing a button in the game was the only thing an action
/// could be, so "committed" and "a native mutation was verified" were the same statement. They are
/// not. The pipeline's actual safety property is that <em>no action may claim a native mutation it
/// cannot prove</em>; requiring every action to be one was an accident of there having been only one
/// kind.
/// </para>
/// <para>
/// A publication is the other kind: the worker produced an immutable snapshot and the main thread
/// handed it to a publisher. It touches no native object, so it reports truthful zeroes into
/// <see cref="ServiceNativeCallTotals"/> rather than fabricating an attempt — which is what keeps the
/// native-call audit meaning "how hard did we poke the game" for the services that actually do.
/// </para>
/// </remarks>
public enum ServiceActionEffect
{
    /// <summary>Nothing happened: the action was rejected before it could take effect.</summary>
    None = 0,

    /// <summary>The action reached an audited native mutation boundary.</summary>
    NativeMutation = 1,

    /// <summary>The action handed an immutable snapshot to a publisher.</summary>
    Publication = 2,
}

/// <summary>Which publication a publishing action advanced.</summary>
/// <remarks>
/// Enumerated rather than inferred from the generation's type so the evidence stays one value-typed
/// struct. The channels are the things a worker can hand the main thread; a service that publishes to
/// none of them has no business reporting a publication effect.
/// </remarks>
public enum ServicePublicationChannel
{
    World = 1,
    Strategy = 2,
    Configuration = 3,
    StrategistBulletin = 4,
}

/// <summary>
/// Evidence that a publishing action took effect: the channel it advanced and the generation now
/// live.
/// </summary>
/// <remarks>
/// This is the publication counterpart to <see cref="ServiceNativeMutationEvidence"/>, and it is
/// evidence in the same sense — it names a specific, checkable thing that is now true. A reader of a
/// trace can see exactly which snapshot each service published and, from a consumer's decision codes,
/// which one it acted against.
/// </remarks>
public readonly struct ServicePublicationEvidence : IEquatable<ServicePublicationEvidence>
{
    private ServicePublicationEvidence(ServicePublicationChannel channel, ulong generation)
    {
        Channel = channel;
        Generation = generation;
    }

    public ServicePublicationChannel Channel { get; }

    /// <summary>
    /// The generation now live on that channel, as a raw value. The typed generations differ per
    /// channel and cannot share a field; <see cref="Channel"/> is what says how to read it.
    /// </summary>
    public ulong Generation { get; }

    public bool IsValid =>
        Channel is ServicePublicationChannel.World or
            ServicePublicationChannel.Strategy or
            ServicePublicationChannel.Configuration or
            ServicePublicationChannel.StrategistBulletin &&
        Generation != 0;

    public static ServicePublicationEvidence World(WorldGeneration generation)
    {
        if (!generation.IsValid)
            throw new ArgumentException("A valid world generation is required.", nameof(generation));
        return new ServicePublicationEvidence(ServicePublicationChannel.World, generation.Value);
    }

    public static ServicePublicationEvidence Strategy(StrategyGeneration generation)
    {
        if (generation.Value == 0)
            throw new ArgumentException("A valid strategy generation is required.", nameof(generation));
        return new ServicePublicationEvidence(ServicePublicationChannel.Strategy, generation.Value);
    }

    public static ServicePublicationEvidence Configuration(ConfigGeneration generation)
    {
        if (!generation.IsValid)
            throw new ArgumentException("A valid configuration generation is required.", nameof(generation));
        return new ServicePublicationEvidence(ServicePublicationChannel.Configuration, generation.Value);
    }

    public static ServicePublicationEvidence StrategistBulletin(ulong sequence)
    {
        if (sequence == 0)
            throw new ArgumentException("A published bulletin's sequence is required.", nameof(sequence));
        return new ServicePublicationEvidence(ServicePublicationChannel.StrategistBulletin, sequence);
    }

    public bool Equals(ServicePublicationEvidence other) =>
        Channel == other.Channel && Generation == other.Generation;

    public override bool Equals(object? obj) =>
        obj is ServicePublicationEvidence other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Channel, Generation);

    public override string ToString() => $"{Channel}:{Generation}";

    public static bool operator ==(ServicePublicationEvidence left, ServicePublicationEvidence right) =>
        left.Equals(right);

    public static bool operator !=(ServicePublicationEvidence left, ServicePublicationEvidence right) =>
        !left.Equals(right);
}

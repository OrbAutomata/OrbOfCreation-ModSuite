using System;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;

public static class ServiceActionTimelineSources
{
    public static IServiceActionTimelineSource Shared => ServiceActionTimelineRegistry.Shared;
}

/// <summary>Single-owner publication point for the active suite action timeline.</summary>
public sealed class ServiceActionTimelineRegistry : IServiceActionTimelineSource
{
    private IServiceActionTimelineSource? _source;
    private long _baseRevision;

    public static ServiceActionTimelineRegistry Shared { get; } = new();

    public int TimelineServiceCount => _source?.TimelineServiceCount ?? 0;
    public int TimelineBucketCapacity => _source?.TimelineBucketCapacity ?? 0;
    public int TimelineCellCapacity => _source?.TimelineCellCapacity ?? 0;
    public long TimelineRevision => checked(_baseRevision + (_source?.TimelineRevision ?? 0));

    public ServiceActionTimelineCopyResult CopyTimelineTo(
        Span<ServiceActionTimelineCellSnapshot> destination)
    {
        var source = _source;
        if (source is null)
            return new ServiceActionTimelineCopyResult(0, 0, 0, 0, TimelineRevision);
        var copied = source.CopyTimelineTo(destination);
        return new ServiceActionTimelineCopyResult(
            copied.ServiceCount,
            copied.BucketCount,
            copied.AvailableCount,
            copied.WrittenCount,
            TimelineRevision);
    }

    internal ServiceActionTimelineRegistration Register(
        IServiceActionTimelineSource source)
    {
        if (source is null) throw new ArgumentNullException(nameof(source));
        if (_source is not null)
            throw new InvalidOperationException("An action timeline is already registered.");
        _baseRevision = checked(_baseRevision + 1);
        _source = source;
        return new ServiceActionTimelineRegistration(this, source);
    }

    internal void Remove(IServiceActionTimelineSource source)
    {
        if (!ReferenceEquals(_source, source)) return;
        _baseRevision = checked(_baseRevision + source.TimelineRevision + 1);
        _source = null;
    }
}

internal sealed class ServiceActionTimelineRegistration : IDisposable
{
    private ServiceActionTimelineRegistry? _registry;
    private IServiceActionTimelineSource? _source;

    internal ServiceActionTimelineRegistration(
        ServiceActionTimelineRegistry registry,
        IServiceActionTimelineSource source)
    {
        _registry = registry;
        _source = source;
    }

    public void Dispose()
    {
        var registry = _registry;
        var source = _source;
        _registry = null;
        _source = null;
        if (registry is not null && source is not null) registry.Remove(source);
    }
}

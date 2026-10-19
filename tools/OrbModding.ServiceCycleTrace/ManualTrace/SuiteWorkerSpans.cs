using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>Which suite workers were evaluating during a stretch of the game's part of a frame.</summary>
internal enum SuiteWorkerPresence
{
    None,
    Capturing,
    OtherOnly,
}

/// <summary>
/// When each service's worker was evaluating, read from the worker's own start and end stamps that
/// every evaluation outcome carries.
/// </summary>
/// <remarks>
/// An outcome is recorded when the main thread acquires it, after the frames it overlapped, so the
/// spans are only queried once the whole capture has been read. The capturing service is the one
/// whose cycles capture: its worker derives the world from what the pump read.
/// </remarks>
internal sealed class SuiteWorkerSpans
{
    private readonly HashSet<ulong> _capturing = new();
    private readonly List<(ulong Service, long From, long To)> _observed = new();
    private Span[] _capturingSpans = Array.Empty<Span>();
    private Span[] _otherSpans = Array.Empty<Span>();
    private bool _frozen;

    internal void Observe(in ServiceCycleSemanticEvent item)
    {
        if (_frozen) throw new InvalidOperationException("Worker spans are already frozen.");
        var payload = item.Payload;
        switch (item.Kind)
        {
            case ServiceCycleSemanticEventKind.CaptureCompleted:
                _capturing.Add(payload.Service);
                return;
            case ServiceCycleSemanticEventKind.EvaluationCompleted:
            case ServiceCycleSemanticEventKind.EvaluationFaulted:
            case ServiceCycleSemanticEventKind.EvaluationDeferred:
            case ServiceCycleSemanticEventKind.ProjectionFaulted:
                if (payload.DurationTicks <= 0) return;
                _observed.Add((payload.Service, payload.TimestampTicks - payload.DurationTicks, payload.TimestampTicks));
                return;
        }
    }

    internal void Freeze()
    {
        if (_frozen) return;
        _frozen = true;
        _capturingSpans = Merge(_observed.Where(x => _capturing.Contains(x.Service)));
        _otherSpans = Merge(_observed.Where(x => !_capturing.Contains(x.Service)));
        _observed.Clear();
    }

    /// <summary>Whether any worker evaluated between <paramref name="from"/> and <paramref name="to"/>.</summary>
    internal SuiteWorkerPresence During(long from, long to)
    {
        if (!_frozen) throw new InvalidOperationException("Worker spans are read only once the capture is read.");
        if (Overlaps(_capturingSpans, from, to)) return SuiteWorkerPresence.Capturing;
        return Overlaps(_otherSpans, from, to) ? SuiteWorkerPresence.OtherOnly : SuiteWorkerPresence.None;
    }

    private static Span[] Merge(IEnumerable<(ulong Service, long From, long To)> spans)
    {
        var merged = new List<Span>();
        foreach (var span in spans.OrderBy(x => x.From))
        {
            if (merged.Count != 0 && span.From <= merged[^1].To)
            {
                merged[^1] = new Span(merged[^1].From, Math.Max(merged[^1].To, span.To));
                continue;
            }
            merged.Add(new Span(span.From, span.To));
        }
        return merged.ToArray();
    }

    private static bool Overlaps(Span[] spans, long from, long to)
    {
        var low = 0;
        var high = spans.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (spans[middle].To <= from) low = middle + 1;
            else high = middle;
        }
        return low < spans.Length && spans[low].From < to;
    }

    private readonly record struct Span(long From, long To);
}

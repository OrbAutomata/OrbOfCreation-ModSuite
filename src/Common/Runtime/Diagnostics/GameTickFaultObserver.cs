using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace OrbModding.Common.Runtime;

internal enum GameTickFaultSite
{
    Element,
    View,
}

/// <summary>One game element that threw from the game's own tick, as first seen.</summary>
internal sealed class GameTickFaultReport
{
    internal GameTickFaultReport(
        GameTickFaultSite site,
        string subject,
        string exceptionType,
        string firstTrace,
        int count)
    {
        Site = site;
        Subject = subject;
        ExceptionType = exceptionType;
        FirstTrace = firstTrace;
        Count = count;
    }

    internal GameTickFaultSite Site { get; }

    /// <summary>The element's name, UUID and native type, as far as the game exposes them.</summary>
    internal string Subject { get; }

    internal string ExceptionType { get; }

    /// <summary>The first throw's exception text and stack, verbatim.</summary>
    internal string FirstTrace { get; }

    /// <summary>How often this element has thrown this exception type in this lifecycle.</summary>
    internal int Count { get; }

    internal string Headline =>
        "The game's own " + (Site == GameTickFaultSite.Element ? "tick" : "view update") +
        " threw " + ExceptionType + " from " + Subject + " (" + Count + "x this lifecycle).";
}

/// <summary>
/// Names the game element that throws from the game's own tick, and never changes what the throw
/// does. The patched call sites run through <see cref="IncrementElement"/> and
/// <see cref="CheckView"/>, whose exception filter records the element and answers false: the
/// exception is never caught, so it unwinds with its original stack exactly as it would unpatched.
/// </summary>
/// <remarks>
/// The no-throw path is one extra static call per element and allocates nothing. Recording runs on
/// the throw path only, inside the game's tick, so it is bounded to a dictionary lookup; naming the
/// element and logging happen on the suite's own update through <see cref="Drain"/>.
/// </remarks>
internal static class GameTickFaultObserver
{
    internal const int MaxDistinctFaults = 64;

    /// <summary>
    /// How many back-to-back throws of one element make the game stuck: at the game's fixed tick,
    /// about one second of every tick failing at the same place.
    /// </summary>
    internal const int RepeatThreshold = 50;

    private static readonly long BurstGapTicks = Stopwatch.Frequency;
    private static readonly object Sync = new();
    private static readonly Dictionary<FaultKey, Fault> Faults = new(FaultKeyComparer.Instance);
    private static readonly List<Fault> Pending = new();
    private static Fault? _repeating;
    private static Func<long> _timestamp = Stopwatch.GetTimestamp;

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void IncrementElement(IIncrement element, float deltaTime)
    {
        try
        {
            element.Increment(deltaTime);
        }
        catch (Exception exception) when (Record(GameTickFaultSite.Element, element, exception))
        {
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void CheckView(ManagedView view)
    {
        try
        {
            view.CheckViewVisibility();
        }
        catch (Exception exception) when (Record(GameTickFaultSite.View, view, exception))
        {
            throw;
        }
    }

    /// <summary>The fault that first repeated past <see cref="RepeatThreshold"/>, if any.</summary>
    internal static GameTickFaultReport? Repeating
    {
        get
        {
            lock (Sync) return _repeating?.Report();
        }
    }

    internal static IReadOnlyList<GameTickFaultReport> Snapshot()
    {
        lock (Sync)
        {
            var reports = new List<GameTickFaultReport>(Faults.Count);
            foreach (var fault in Faults.Values) reports.Add(fault.Report());
            return reports;
        }
    }

    /// <summary>
    /// Names every newly recorded element and hands each to <paramref name="log"/> once. Runs on
    /// the Unity main thread, outside the tick.
    /// </summary>
    internal static void Drain(Func<object, string> describe, Action<GameTickFaultReport> log)
    {
        if (describe is null) throw new ArgumentNullException(nameof(describe));
        if (log is null) throw new ArgumentNullException(nameof(log));
        Fault[] fresh;
        lock (Sync)
        {
            if (Pending.Count == 0) return;
            fresh = Pending.ToArray();
            Pending.Clear();
        }
        foreach (var fault in fresh)
        {
            string subject;
            try { subject = describe(fault.Subject); }
            catch (Exception exception)
            {
                subject = fault.Subject.GetType().FullName + " (name unavailable: " +
                          ExceptionDetail.Describe(exception) + ")";
            }
            GameTickFaultReport report;
            lock (Sync)
            {
                fault.Describe(subject);
                report = fault.Report();
            }
            log(report);
        }
    }

    /// <summary>Lifecycle transitions invalidate every native reference the faults hold.</summary>
    internal static void Reset()
    {
        lock (Sync)
        {
            Faults.Clear();
            Pending.Clear();
            _repeating = null;
        }
    }

    internal static void UseClockForTests(Func<long> timestamp) =>
        _timestamp = timestamp ?? throw new ArgumentNullException(nameof(timestamp));

    private static bool Record(GameTickFaultSite site, object subject, Exception exception)
    {
        try
        {
            var now = _timestamp();
            lock (Sync)
            {
                var key = new FaultKey(subject, exception.GetType());
                if (!Faults.TryGetValue(key, out var fault))
                {
                    if (Faults.Count >= MaxDistinctFaults) return false;
                    fault = new Fault(site, subject, exception, now);
                    Faults.Add(key, fault);
                    Pending.Add(fault);
                }
                else
                {
                    fault.Repeat(now, BurstGapTicks);
                }
                if (_repeating is null && fault.BurstCount >= RepeatThreshold) _repeating = fault;
            }
        }
        catch
        {
        }
        return false;
    }

    private sealed class Fault
    {
        private readonly GameTickFaultSite _site;
        private readonly string _exceptionType;
        private readonly string _firstTrace;
        private string _subjectDescription;
        private int _count;
        private long _lastSeen;

        internal Fault(GameTickFaultSite site, object subject, Exception exception, long now)
        {
            _site = site;
            Subject = subject;
            _exceptionType = exception.GetType().FullName ?? exception.GetType().Name;
            _firstTrace = exception.ToString();
            _subjectDescription = subject.GetType().FullName ?? subject.GetType().Name;
            _count = 1;
            BurstCount = 1;
            _lastSeen = now;
        }

        internal object Subject { get; }
        internal int BurstCount { get; private set; }

        internal void Repeat(long now, long burstGap)
        {
            _count++;
            BurstCount = now - _lastSeen > burstGap ? 1 : BurstCount + 1;
            _lastSeen = now;
        }

        internal void Describe(string subject) => _subjectDescription = subject;

        internal GameTickFaultReport Report() =>
            new(_site, _subjectDescription, _exceptionType, _firstTrace, _count);
    }

    private readonly struct FaultKey
    {
        internal FaultKey(object subject, Type exceptionType)
        {
            Subject = subject;
            ExceptionType = exceptionType;
        }

        internal object Subject { get; }
        internal Type ExceptionType { get; }
    }

    private sealed class FaultKeyComparer : IEqualityComparer<FaultKey>
    {
        internal static readonly FaultKeyComparer Instance = new();

        public bool Equals(FaultKey left, FaultKey right) =>
            ReferenceEquals(left.Subject, right.Subject) &&
            ReferenceEquals(left.ExceptionType, right.ExceptionType);

        public int GetHashCode(FaultKey key) =>
            (RuntimeHelpers.GetHashCode(key.Subject) * 397) ^ key.ExceptionType.GetHashCode();
    }
}

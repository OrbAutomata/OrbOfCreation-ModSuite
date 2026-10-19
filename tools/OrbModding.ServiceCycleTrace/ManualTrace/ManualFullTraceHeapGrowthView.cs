using System.Globalization;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;

namespace OrbModding.ServiceCycleTrace.ManualTrace;

/// <summary>
/// Whether the heap the collector keeps finding alive grows, and which of the suspects holds it: the
/// game on its own, the suite's world readings, host objects nobody destroys, or a reservation that
/// outgrows its use.
/// </summary>
/// <remarks>
/// <para>
/// The floor is the used heap read right after a collection, so it is what survived rather than what
/// was allocated. Its slope is fitted over per-minute minima, which leaves out the garbage a minute
/// accumulates between collections.
/// </para>
/// <para>
/// An emergency stop pauses every cycle, world capture included, so a stretch spent stopped is the
/// game running alone. Lifecycle activations split stretches too, because a load or a reset replaces
/// what the heap holds. So does every configuration the player applies, so a stretch with one feature
/// turned off keeps the rest of the suite running without it.
/// </para>
/// </remarks>
internal static class ManualFullTraceHeapGrowthView
{
    internal const long MinuteTicks = 60 * 10_000_000L;
    internal const long WindowTicks = 10 * MinuteTicks;
    internal const int MinimumMinutes = 10;
    private const int HealthyWorldSnapshots = 16;
    private const double ObjectGrowthPerHour = 500;
    private const double Megabyte = 1_048_576d;

    internal static void Write(TextWriter writer, ManualFullTraceSession session)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(session);
        var reading = Read(session);
        writer.WriteLine("## Heap growth");
        writer.WriteLine();
        if (reading.Stretches.All(x => x.Floors.Count == 0))
        {
            writer.WriteLine(
                "Unavailable: this capture holds no heap samples across a collection. A profiling build " +
                "writes one per pump; its log says why when it could not.");
            writer.WriteLine();
            return;
        }

        var names = ManualFullTraceServiceView.Names(session);
        var running = Combine(reading.Stretches.Where(x => !x.Stopped));
        var stopped = Combine(reading.Stretches.Where(x => x.Stopped));
        var busiest = Control(reading);
        var busiestLabel = busiest is { } service
            ? names.GetValueOrDefault(service) ?? ManualFullTraceServiceView.Label(names, service)
            : "—";
        writer.WriteLine(
            "The floor is the used heap right after a collection: what survived, not what was allocated. " +
            "A stopped stretch is an emergency stop, when no suite cycle runs and the game runs alone. " +
            "Applying a configuration starts a new stretch.");
        writer.WriteLine();
        writer.WriteLine($"| Stretch | From min | Minutes | Floor MB | Floor MB/h | Reserved − used MB | World snapshots | ScriptableObjects | GameObjects | MonoBehaviours | Commits/h | {busiestLabel} commits/h |");
        writer.WriteLine("|:---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (var stretch in reading.Stretches.Where(x => x.Floors.Count != 0))
        {
            CensusReading? first = stretch.Censuses.Count == 0 ? null : stretch.Censuses[0];
            CensusReading? last = stretch.Censuses.Count == 0 ? null : stretch.Censuses[^1];
            writer.WriteLine(
                $"| {(stretch.Stopped ? "Stopped" : "Running")} | {Fixed((stretch.FromTicks - reading.OriginTicks) / (double)MinuteTicks, "F1")} | " +
                $"{Fixed(stretch.Minutes, "F1")} | {Range(stretch.Floors[0].Bytes / Megabyte, stretch.Floors[^1].Bytes / Megabyte)} | " +
                $"{Optional(stretch.FloorMegabytesPerHour, "F0")} | " +
                $"{CensusRange(first, last, x => (x.Census.ReservedHeap - x.UsedHeap) / Megabyte)} | " +
                $"{(stretch.Censuses.Count == 0 ? "—" : Count(stretch.Censuses.Max(x => x.Census.LiveWorldSnapshots)))} | " +
                $"{CensusRange(first, last, x => x.Census.ScriptableObjects)} | {CensusRange(first, last, x => x.Census.GameObjects)} | " +
                $"{CensusRange(first, last, x => x.Census.MonoBehaviours)} | {PerHour(stretch.Commits.Values.Sum(), stretch)} | " +
                $"{PerHour(busiest is { } key ? stretch.Commits.GetValueOrDefault(key) : 0, stretch)} |");
        }
        writer.WriteLine();

        writer.WriteLine("### Verdicts");
        writer.WriteLine();
        writer.WriteLine("- **The game grows its own heap:** " + GameAlone(running, stopped));
        if (busiest is { } loudest)
            writer.WriteLine($"- **{busiestLabel}'s commits grow the heap:** " + Commits(reading, loudest, busiestLabel));
        writer.WriteLine("- **The suite holds old world readings:** " + WorldReadings(reading));
        writer.WriteLine("- **Host objects nobody destroys:** " + HostObjects(reading));
        writer.WriteLine("- **The reservation outgrows its use:** " + Reservation(reading, running));
        writer.WriteLine("- **Captures cost more as the game grows:** " + Captures(reading));
        writer.WriteLine();

        var drivers = Drivers(reading, names);
        writer.WriteLine("### What the floor rises with");
        writer.WriteLine();
        if (drivers.Count == 0)
        {
            writer.WriteLine(
                $"Unanswered: fewer than {MinimumWindows} running ten-minute windows with a floor to fit.");
            writer.WriteLine();
            return;
        }
        writer.WriteLine(
            "Per running ten-minute window, the floor's rise against each service's committed actions. " +
            "A strong correlation is a lead, not a cause: everything speeds up as the game does.");
        writer.WriteLine();
        writer.WriteLine("| Service | Windows | Correlation | MB per 1,000 commits |");
        writer.WriteLine("|:---|---:|---:|---:|");
        foreach (var driver in drivers)
        {
            writer.WriteLine(
                $"| {driver.Label} | {Count(driver.Windows)} | {Fixed(driver.Correlation, "F2")} | " +
                $"{Fixed(driver.MegabytesPerThousand, "F1")} |");
        }
        writer.WriteLine();
    }

    internal const int MinimumWindows = 6;

    private static string GameAlone(Combined running, Combined stopped)
    {
        if (stopped.Minutes < MinimumMinutes || stopped.MegabytesPerHour is not { } alone)
            return $"Unanswered: no emergency-stop stretch of {MinimumMinutes} minutes or more with a floor to fit.";
        if (running.MegabytesPerHour is not { } suite)
            return $"Unanswered: no running stretch of {MinimumMinutes} minutes or more with a floor to fit.";
        var numbers = $"stopped {Fixed(alone, "F0")} MB/h over {Fixed(stopped.Minutes, "F0")} min, running " +
            $"{Fixed(suite, "F0")} MB/h over {Fixed(running.Minutes, "F0")} min";
        if (suite <= 0) return $"Unanswered: the floor does not rise while running ({numbers}).";
        var ratio = alone / suite;
        if (ratio >= 0.5) return $"Confirmed: the floor keeps rising with the suite stopped ({numbers}).";
        if (ratio <= 0.2)
            return $"Refuted: the floor stops rising when the suite stops, so it follows what the suite does ({numbers}).";
        return $"Partly: the floor rises slower with the suite stopped ({numbers}).";
    }

    /// <summary>
    /// The busiest service that has a fitted running stretch without its commits, so a feature turned
    /// off for a stretch is the control even when another service commits more; otherwise the busiest.
    /// </summary>
    private static ulong? Control(Reading reading)
    {
        var running = reading.Stretches.Where(x => !x.Stopped).ToList();
        var fitted = running.Where(x => x.FloorMegabytesPerHour is not null).ToList();
        var services = running
            .SelectMany(x => x.Commits)
            .GroupBy(x => x.Key)
            .OrderByDescending(x => x.Sum(y => y.Value))
            .Select(x => x.Key)
            .ToList();
        foreach (var service in services)
        {
            if (fitted.Any(x => !x.Commits.ContainsKey(service)) && fitted.Any(x => x.Commits.ContainsKey(service)))
                return service;
        }
        return services.Count == 0 ? null : services[0];
    }

    /// <summary>
    /// The busiest service turned off while the rest of the suite runs: a floor that stops with its
    /// commits follows what it buys or holds, one that keeps rising grows with play regardless.
    /// </summary>
    private static string Commits(Reading reading, ulong service, string label)
    {
        var running = reading.Stretches.Where(x => !x.Stopped).ToList();
        var quiet = Combine(running.Where(x => !x.Commits.ContainsKey(service)));
        var busy = Combine(running.Where(x => x.Commits.ContainsKey(service)));
        if (quiet.MegabytesPerHour is not { } without)
            return $"Unanswered: no running stretch of {MinimumMinutes} minutes or more in which {label} committed nothing.";
        if (busy.MegabytesPerHour is not { } with)
            return $"Unanswered: no running stretch of {MinimumMinutes} minutes or more in which {label} committed.";
        var numbers = $"without its commits {Fixed(without, "F0")} MB/h over {Fixed(quiet.Minutes, "F0")} min, with them " +
            $"{Fixed(with, "F0")} MB/h over {Fixed(busy.Minutes, "F0")} min";
        if (with <= 0) return $"Unanswered: the floor does not rise while {label} commits ({numbers}).";
        var ratio = without / with;
        if (ratio <= 0.2)
            return $"Confirmed: the floor stops rising while {label} commits nothing and the rest of the suite runs ({numbers}).";
        if (ratio >= 0.5)
            return $"Refuted: the floor keeps rising without {label}'s commits ({numbers}).";
        return $"Partly: the floor rises slower without {label}'s commits ({numbers}).";
    }

    private static string WorldReadings(Reading reading)
    {
        var censuses = reading.Stretches.SelectMany(x => x.Censuses).ToList();
        if (censuses.Count == 0) return "Unanswered: this capture holds no heap census.";
        var (first, last) = Thirds(censuses, x => x.Census.LiveWorldSnapshots);
        var most = censuses.Max(x => x.Census.LiveWorldSnapshots);
        var numbers = $"at most {Count(most)} reachable over {Count(censuses.Count)} censuses, " +
            $"{Fixed(first, "F1")} on average in the first third and {Fixed(last, "F1")} in the last";
        return most > HealthyWorldSnapshots && last > first + HealthyWorldSnapshots / 2d
            ? $"Confirmed: world snapshots accumulate — {numbers}."
            : $"Refuted: {numbers}.";
    }

    private static string HostObjects(Reading reading)
    {
        var censuses = reading.Stretches.Where(x => !x.Stopped).SelectMany(x => x.Censuses).ToList();
        if (censuses.Count < 3) return "Unanswered: fewer than three censuses while running.";
        var kinds = new (string Name, Func<CensusReading, double> Count)[]
        {
            ("ScriptableObjects", x => x.Census.ScriptableObjects),
            ("GameObjects", x => x.Census.GameObjects),
            ("MonoBehaviours", x => x.Census.MonoBehaviours),
        };
        var rates = kinds
            .Select(kind => (kind.Name, PerHour: Slope(censuses.Select(x => (x.TimestampTicks, kind.Count(x)))) ?? 0,
                First: kind.Count(censuses[0]), Last: kind.Count(censuses[^1])))
            .ToList();
        var numbers = string.Join(", ", rates.Select(x =>
            $"{x.Name} {Count((long)x.First)} → {Count((long)x.Last)} ({Signed(x.PerHour)}/h)"));
        return rates.Any(x => x.PerHour >= ObjectGrowthPerHour && x.Last >= x.First * 1.05)
            ? $"Confirmed: {numbers}."
            : $"Refuted: {numbers}.";
    }

    private static string Reservation(Reading reading, Combined running)
    {
        var censuses = reading.Stretches.SelectMany(x => x.Censuses).ToList();
        if (censuses.Count < 3) return "Unanswered: fewer than three censuses.";
        var gap = Slope(censuses.Select(x => (x.TimestampTicks, (x.Census.ReservedHeap - x.UsedHeap) / Megabyte)));
        var reserved = Slope(censuses.Select(x => (x.TimestampTicks, x.Census.ReservedHeap / Megabyte)));
        if (gap is not { } gapPerHour || reserved is not { } reservedPerHour)
            return "Unanswered: the censuses span too little time to fit.";
        var numbers = $"reserved {Signed(reservedPerHour)} MB/h, unused part of it {Signed(gapPerHour)} MB/h, " +
            $"floor {Optional(running.MegabytesPerHour, "F0")} MB/h running";
        return gapPerHour >= 50 && gapPerHour >= 0.5 * (running.MegabytesPerHour ?? 0)
            ? $"Confirmed: {numbers}."
            : $"Refuted: {numbers}.";
    }

    private static string Captures(Reading reading)
    {
        if (reading.Captures.Count < 3) return "Unanswered: no measured captures.";
        var (first, last) = Thirds(reading.Captures, x => x.Kilobytes);
        var numbers = $"{Fixed(first, "F0")} KB per capture in the first third, {Fixed(last, "F0")} KB in the last";
        return last >= first * 1.25
            ? $"Confirmed, as garbage per reading rather than anything kept: {numbers}."
            : $"Refuted: {numbers}.";
    }

    private static List<Driver> Drivers(Reading reading, Dictionary<ulong, string> names)
    {
        var windows = reading.Stretches
            .Where(x => !x.Stopped)
            .SelectMany(x => x.Windows())
            .Where(x => x.FloorMegabytes is not null)
            .ToList();
        if (windows.Count < MinimumWindows) return new List<Driver>();
        var services = windows.SelectMany(x => x.Commits.Keys).Distinct();
        var drivers = new List<Driver>();
        foreach (var service in services)
        {
            var points = windows
                .Select(x => (Commits: x.Commits.TryGetValue(service, out var count) ? count : 0d, Rise: x.FloorMegabytes!.Value))
                .ToList();
            if (Correlate(points) is not { } fit) continue;
            drivers.Add(new Driver(
                ManualFullTraceServiceView.Label(names, service),
                points.Count,
                fit.Correlation,
                fit.Slope * 1_000));
        }
        return drivers.OrderByDescending(x => x.Correlation).Take(5).ToList();
    }

    private static Reading Read(ManualFullTraceSession session)
    {
        var origin = session.Document.FirstTimestampTicks;
        var stretches = new List<Stretch>();
        var captures = new List<CaptureReading>();
        var current = new Stretch(stopped: false, origin);
        stretches.Add(current);
        var stopped = false;
        ulong lifecycle = 0;
        ServiceCycleSemanticPayload? previous = null;
        foreach (var segment in session.Segments())
        foreach (var item in segment.Events)
        {
            var payload = item.Payload;
            switch (item.Kind)
            {
                case ServiceCycleSemanticEventKind.EmergencyEntered when !stopped:
                case ServiceCycleSemanticEventKind.EmergencyCleared when stopped:
                    stopped = !stopped;
                    current = Begin(stretches, current, stopped, payload.TimestampTicks);
                    break;
                case ServiceCycleSemanticEventKind.ConfigurationPublished when current.ToTicks > current.FromTicks:
                    current = Begin(stretches, current, stopped, payload.TimestampTicks);
                    break;
                case ServiceCycleSemanticEventKind.LifecycleActivated when payload.Lifecycle != lifecycle:
                    if (lifecycle != 0) current = Begin(stretches, current, stopped, payload.TimestampTicks);
                    lifecycle = payload.Lifecycle;
                    break;
                case ServiceCycleSemanticEventKind.ActionCommitted:
                    current.Commit(payload.Service, payload.TimestampTicks);
                    break;
                case ServiceCycleSemanticEventKind.PumpHeapSampled:
                    if (previous is { } before && payload.CollectionsAtPumpStart > before.CollectionsAtPumpEnd)
                        current.Floor(payload.TimestampTicks, payload.HeapAtPumpStart);
                    if (payload.CollectionsAtPumpEnd > payload.CollectionsAtPumpStart)
                        current.Floor(payload.TimestampTicks, payload.HeapAtPumpEnd);
                    if (payload.HeapCensus is { } census)
                        current.Censuses.Add(new CensusReading(payload.TimestampTicks, payload.HeapAtPumpEnd, census));
                    if (payload.CapturesMeasured > 0)
                        captures.Add(new CaptureReading(payload.TimestampTicks, payload.CaptureHeapGrowth / 1024d / payload.CapturesMeasured));
                    previous = payload;
                    current.Extend(payload.TimestampTicks);
                    break;
            }
        }
        return new Reading(origin, stretches, captures);
    }

    private static Stretch Begin(List<Stretch> stretches, Stretch current, bool stopped, long timestampTicks)
    {
        current.Extend(timestampTicks);
        var next = new Stretch(stopped, timestampTicks);
        stretches.Add(next);
        return next;
    }

    private static Combined Combine(IEnumerable<Stretch> stretches)
    {
        var fitted = stretches.Where(x => x.FloorMegabytesPerHour is not null && x.Minutes >= MinimumMinutes).ToList();
        var minutes = fitted.Sum(x => x.Minutes);
        return new Combined(
            minutes,
            minutes == 0 ? null : fitted.Sum(x => x.FloorMegabytesPerHour!.Value * x.Minutes) / minutes);
    }

    private static (double First, double Last) Thirds<T>(IReadOnlyList<T> items, Func<T, double> value)
    {
        var third = Math.Max(1, items.Count / 3);
        return (items.Take(third).Average(value), items.Skip(items.Count - third).Average(value));
    }

    /// <summary>Least-squares slope per hour of a series stamped in trace ticks.</summary>
    internal static double? Slope(IEnumerable<(long Ticks, double Value)> series)
    {
        var points = series.ToList();
        if (points.Count < 2) return null;
        var hours = points.Select(x => (x.Ticks - points[0].Ticks) / (60d * MinuteTicks)).ToList();
        var meanX = hours.Average();
        var meanY = points.Average(x => x.Value);
        var spread = hours.Sum(x => (x - meanX) * (x - meanX));
        if (spread <= 0) return null;
        return hours.Select((x, index) => (x - meanX) * (points[index].Value - meanY)).Sum() / spread;
    }

    private static (double Correlation, double Slope)? Correlate(IReadOnlyList<(double X, double Y)> points)
    {
        var meanX = points.Average(x => x.X);
        var meanY = points.Average(x => x.Y);
        var sxx = points.Sum(p => (p.X - meanX) * (p.X - meanX));
        var syy = points.Sum(p => (p.Y - meanY) * (p.Y - meanY));
        var sxy = points.Sum(p => (p.X - meanX) * (p.Y - meanY));
        if (sxx <= 0 || syy <= 0) return null;
        return (sxy / Math.Sqrt(sxx * syy), sxy / sxx);
    }

    private static string CensusRange(CensusReading? first, CensusReading? last, Func<CensusReading, double> value) =>
        first is { } from && last is { } to ? Range(value(from), value(to)) : "—";

    private static string PerHour(double count, Stretch stretch) =>
        Fixed(count / Math.Max(stretch.Minutes / 60d, 1e-9), "F0");

    private static string Range(double from, double to) => $"{Count((long)from)} → {Count((long)to)}";

    private static string Signed(double value) => (value >= 0 ? "+" : "") + Fixed(value, "N0");

    private static string Optional(double? value, string format) => value is { } known ? Fixed(known, format) : "—";

    private static string Count(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Fixed(double value, string format) => value.ToString(format, CultureInfo.InvariantCulture);

    private sealed record Reading(long OriginTicks, List<Stretch> Stretches, List<CaptureReading> Captures);

    private readonly record struct CensusReading(long TimestampTicks, long UsedHeap, PumpHeapCensus Census);

    private readonly record struct CaptureReading(long TimestampTicks, double Kilobytes);

    private readonly record struct Combined(double Minutes, double? MegabytesPerHour);

    private sealed record Driver(string Label, int Windows, double Correlation, double MegabytesPerThousand);

    private sealed record Window(double? FloorMegabytes, Dictionary<ulong, double> Commits);

    private sealed class Stretch
    {
        private readonly List<(long Ticks, ulong Service)> _commits = new();

        internal Stretch(bool stopped, long fromTicks)
        {
            Stopped = stopped;
            FromTicks = fromTicks;
            ToTicks = fromTicks;
        }

        internal bool Stopped { get; }
        internal long FromTicks { get; }
        internal long ToTicks { get; private set; }
        internal List<(long Ticks, long Bytes)> Floors { get; } = new();
        internal List<CensusReading> Censuses { get; } = new();
        internal double Minutes => (ToTicks - FromTicks) / (double)MinuteTicks;

        internal Dictionary<ulong, double> Commits =>
            _commits.GroupBy(x => x.Service).ToDictionary(x => x.Key, x => (double)x.Count());

        internal double? FloorMegabytesPerHour =>
            Minutes >= MinimumMinutes ? Slope(MinuteMinima(Floors)) : null;

        internal void Extend(long timestampTicks) => ToTicks = Math.Max(ToTicks, timestampTicks);

        internal void Floor(long timestampTicks, long bytes) => Floors.Add((timestampTicks, bytes));

        internal void Commit(ulong service, long timestampTicks) => _commits.Add((timestampTicks, service));

        /// <summary>Whole ten-minute windows: the floor's fitted rise across each, and what each service committed.</summary>
        internal IEnumerable<Window> Windows()
        {
            for (var from = FromTicks; from + WindowTicks <= ToTicks; from += WindowTicks)
            {
                var to = from + WindowTicks;
                var floors = Floors.Where(x => x.Ticks >= from && x.Ticks < to).ToList();
                var slope = Slope(MinuteMinima(floors));
                var commits = _commits
                    .Where(x => x.Ticks >= from && x.Ticks < to)
                    .GroupBy(x => x.Service)
                    .ToDictionary(x => x.Key, x => (double)x.Count());
                yield return new Window(slope / 6, commits);
            }
        }

        private static IEnumerable<(long Ticks, double Value)> MinuteMinima(IEnumerable<(long Ticks, long Bytes)> floors) =>
            floors
                .GroupBy(x => x.Ticks / MinuteTicks)
                .Select(x => (x.Key * MinuteTicks, x.Min(y => y.Bytes) / Megabyte));
    }
}

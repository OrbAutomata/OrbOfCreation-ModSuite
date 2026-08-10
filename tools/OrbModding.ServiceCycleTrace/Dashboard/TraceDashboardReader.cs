#if SERVICE_CYCLE_PROFILE
using System.Globalization;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Format;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Profile;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Roster;
using OrbModding.Common.Runtime.ServiceCycle.Tracing;
using OrbModding.ServiceCycleTrace.Journal;
using OrbModding.ServiceCycleTrace.ManualTrace;
using OrbModding.ServiceCycleTrace.Performance;

namespace OrbModding.ServiceCycleTrace.Dashboard;

internal static class TraceDashboardReader
{
    private const double TicksPerMillisecond = TimeSpan.TicksPerMillisecond;

    /// <summary>
    /// Reads whatever the capture actually holds. The full trace is the spine and is required; the
    /// profile and the journal are recorded by separate, separately armed machinery, so a capture
    /// without them is an ordinary release-build session rather than a broken input, and the
    /// dashboard says which panes are empty instead of refusing to render at all.
    /// </summary>
    internal static TraceDashboardDocument Read(TraceCaptureSelection selection)
    {
        var full = ManualFullTraceSessionReader.Read(selection.FullSessionDirectory);
        var notes = new List<string>(selection.Notes);
        var profile = ReadProfile(selection.RunDirectory, notes);
        var journal = ReadJournal(selection.RunDirectory, notes);
        var firstTicks = full.Document.FirstTimestampTicks;
        var lastTicks = full.Document.LastTimestampTicks;
        if (lastTicks < firstTicks)
            throw new InvalidDataException("The full trace has an invalid observed window.");

        var pumps = new List<TraceDashboardPump>();
        var events = new List<TraceDashboardEvent>();
        var acceptedPumps = 0;
        long? previousPumpTicks = null;
        foreach (var segment in full.Segments())
        {
            foreach (var item in segment.Events)
            {
                var payload = item.Payload;
                if (item.Kind == ServiceCycleSemanticEventKind.PumpCompleted)
                {
                    if (payload.PumpAccepted) acceptedPumps++;
                    pumps.Add(Pump(
                        in payload,
                        firstTicks,
                        firstAccepted: payload.PumpAccepted && acceptedPumps == 1,
                        previousPumpTicks));
                    previousPumpTicks = payload.TimestampTicks;
                }
                else
                {
                    events.Add(Event(item.Kind, in payload, firstTicks));
                }
            }
        }

        var roster = full.Roster();
        var decisions = Decisions(
            journal?.Records ?? Array.Empty<DecisionJournalRecord>(),
            events,
            firstTicks,
            lastTicks);
        var aggregates = new List<TraceDashboardStageAggregate>();
        var samples = new List<TraceDashboardStageSample>();
        if (profile is not null)
        {
            var calibration = profile.Manifest.Calibration;
            foreach (var record in profile.Records)
            {
                if (record.Kind == ServiceCycleProfileRecordKind.Aggregate)
                    aggregates.Add(Aggregate(in record, in calibration));
                else
                    samples.Add(Sample(in record, in calibration, firstTicks));
            }
        }

        var cycles = Cycles(events);
        var services = Services(events, aggregates, cycles, roster);

        var metadata = new TraceDashboardMetadata(
            full.Name,
            full.Document.State.ToString(),
            full.Document.TerminalReason?.ToString() ?? "Unavailable",
            profile is null
                ? "Unavailable"
                : "session-" + profile.Manifest.Session.Value.ToString("x16", CultureInfo.InvariantCulture),
            profile?.Manifest.Completeness.ToString() ?? "Unavailable",
            profile?.Manifest.Reason.ToString() ?? "Unavailable",
            journal is null
                ? "Unavailable"
                : journal.Value.Run.ToString("x16", CultureInfo.InvariantCulture),
            Milliseconds(lastTicks - firstTicks),
            full.Document.WrittenRecords,
            profile?.Manifest.WrittenRecords ?? 0,
            decisions.Length,
            profile?.Manifest.Calibration.TraceActive ?? false,
            profile?.Manifest.Calibration.AllocationAvailable ?? false,
            notes.ToArray());
        return new TraceDashboardDocument(
            2,
            metadata,
            services,
            pumps.ToArray(),
            cycles,
            events.ToArray(),
            decisions,
            aggregates.ToArray(),
            samples.ToArray());
    }

    /// <summary>
    /// The profile session recorded beside the full trace, or nothing when the profiler was never
    /// armed — which is every release build, since the profiler is compiled out of one.
    /// </summary>
    private static ServiceCycleProfileSession? ReadProfile(string? runDirectory, List<string> notes)
    {
        var directory = runDirectory is null ? null : Path.Combine(runDirectory, "profile");
        var sessions = directory is not null && Directory.Exists(directory)
            ? Directory.GetDirectories(directory, "session-*", SearchOption.TopDirectoryOnly)
            : Array.Empty<string>();
        if (sessions.Length == 0)
        {
            notes.Add(
                "No profiler capture in this session (release build): stage timings, allocation, and " +
                "operation counts are unavailable.");
            return null;
        }
        if (sessions.Length != 1)
            throw new InvalidDataException("The correlated capture must contain exactly one 'profile' session.");
        return ServiceCycleProfileSessionReader.Read(sessions[0]);
    }

    /// <summary>
    /// The always-on journal is one directory beside the run folders, not a per-run artifact, so a
    /// capture root correlates against its parent. A root captured before that move keeps its own.
    /// </summary>
    private static JournalRun? ReadJournal(string? runDirectory, List<string> notes)
    {
        var path = JournalDirectory(runDirectory);
        if (path is null)
        {
            notes.Add(
                "No decision journal beside this capture: decision and attributed action spans are unavailable.");
            return null;
        }
        var run = ReadLatestJournalRun(path);
        if (run is null)
            notes.Add("The decision journal beside this capture holds no segments.");
        return run;
    }

    private static string? JournalDirectory(string? root)
    {
        if (root is null) return null;
        var owned = Path.Combine(root, "journal");
        if (Directory.Exists(owned)) return owned;
        var parent = Path.GetDirectoryName(root);
        var shared = parent is null ? null : Path.Combine(parent, "journal");
        if (shared is not null && Directory.Exists(shared)) return shared;
        return null;
    }

    private static JournalRun? ReadLatestJournalRun(string path)
    {
        var directory = new DecisionJournalDirectory(path);
        var inventory = directory.Inventory();
        if (!inventory.HasSegments) return null;
        var latest = directory.ReadSegment(inventory.LastOrdinal, out _).Run.Value;
        var records = new List<DecisionJournalRecord>();
        var assembler = new DecisionJournalWindowAssembler();
        for (var ordinal = inventory.FirstOrdinal; ordinal <= inventory.LastOrdinal; ordinal++)
        {
            var segment = directory.ReadSegment(ordinal, out var encodedBytes);
            assembler.Add(segment, encodedBytes);
            if (segment.Run.Value == latest) records.AddRange(segment.Records);
        }
        var window = assembler.Complete();
        if (window.SegmentCount != checked((ulong)inventory.Count))
            throw new InvalidDataException("The decision-journal inventory changed while it was read.");
        return new JournalRun(latest, records.ToArray());
    }

    private static TraceDashboardPump Pump(
        in ServiceCycleSemanticPayload value,
        long origin,
        bool firstAccepted,
        long? previousPumpTicks) => new(
        Offset(value.TimestampTicks, origin),
        value.FrameIdentity,
        value.PumpAccepted,
        Milliseconds(value.TotalDurationTicks),
        previousPumpTicks is null || value.TimestampTicks < previousPumpTicks.Value
            ? null
            : Milliseconds(value.TimestampTicks - previousPumpTicks.Value),
        Milliseconds(value.ResponseDurationTicks),
        Milliseconds(value.CaptureDurationTicks),
        Milliseconds(value.ActionDurationTicks),
        value.ResponsesAcquired,
        value.CapturesAttempted,
        value.ActionsAttempted,
        value.CyclesStarted,
        value.WorldGateDeferrals,
        value.LifecycleTransitions,
        // The frame that carried the first full world pass also carried the JIT and the first touch
        // of every buffer behind it, and a frame that moved a lifecycle position rebuilt what it
        // moved. Neither is what a warm frame costs, and neither is dropped — both are named so an
        // aggregate can exclude them and still say what it excluded.
        firstAccepted
            ? nameof(ServiceCycleProfileTemperature.ColdProcess)
            : value.PumpAccepted && value.LifecycleTransitions > 0
                ? nameof(ServiceCycleProfileTemperature.LifecycleRebind)
                : nameof(ServiceCycleProfileTemperature.Warm));

    private static TraceDashboardEvent Event(
        ServiceCycleSemanticEventKind kind,
        in ServiceCycleSemanticPayload value,
        long origin) => new(
        Offset(value.TimestampTicks, origin),
        kind.ToString(),
        Lane(kind),
        value.Service,
        value.Lifecycle,
        value.World,
        value.Capture,
        value.Cycle,
        value.Batch,
        value.Action,
        value.FrameIdentity,
        (value.Fields & ServiceCycleSemanticFields.FrameIdentity) != 0,
        Milliseconds(value.DurationTicks),
        value.Code,
        value.Disposition,
        value.ActionCount,
        value.CommittedCount,
        value.NativeCallsAttempted,
        value.MutationAttempts,
        value.MutationsCommitted);

    /// <summary>
    /// Splits every cycle in the window into its stages. Nothing here is new measurement: the worker
    /// already reads the monotonic clock three times per cycle and all three readings already reach
    /// the wire, they were simply never correlated back into one cycle.
    /// </summary>
    /// <remarks>
    /// The key carries the lifecycle because cycle ids restart at one in every lifecycle. Keyed on
    /// service and cycle alone, a later lifecycle overwrote an earlier one row for row: one 43-minute
    /// capture lost 2,782 of World collection's 7,822 cycles and showed the service as
    /// post-prestige-only in the same table that showed Auto Buy as pre-prestige-only, silently, with
    /// the surviving rows mixing stamps from two lifecycles into one handoff and derive figure.
    /// </remarks>
    private static TraceDashboardCycle[] Cycles(List<TraceDashboardEvent> events)
    {
        var stages = new Dictionary<(ulong Service, ulong Lifecycle, ulong Cycle), CycleStages>();
        foreach (var item in events)
        {
            if (item.Service == 0 || item.Cycle == 0) continue;
            var key = (item.Service, item.Lifecycle, item.Cycle);
            if (!stages.TryGetValue(key, out var stage)) stages[key] = stage = new CycleStages();
            stage.Observe(item);
        }

        var firstCycleByService = new Dictionary<ulong, (ulong Service, ulong Lifecycle, ulong Cycle)>();
        var firstLifecycleByService = new Dictionary<ulong, ulong>();
        var rebindCycles = new HashSet<(ulong Service, ulong Lifecycle)>();
        foreach (var pair in stages.OrderBy(x => x.Value.StartMilliseconds))
        {
            var service = pair.Key.Service;
            if (!firstCycleByService.ContainsKey(service))
            {
                firstCycleByService[service] = pair.Key;
                firstLifecycleByService[service] = pair.Value.Lifecycle;
                continue;
            }
            if (pair.Value.Lifecycle != firstLifecycleByService[service])
                rebindCycles.Add((service, pair.Value.Lifecycle));
        }

        ReconcileStartedCycles(events, stages);

        var output = new List<TraceDashboardCycle>(stages.Count);
        var rebindSeen = new HashSet<(ulong, ulong)>();
        foreach (var pair in stages.OrderBy(x => x.Value.StartMilliseconds))
        {
            var stage = pair.Value;
            var service = pair.Key.Service;
            var temperature = firstCycleByService[service] == pair.Key
                ? nameof(ServiceCycleProfileTemperature.ColdProcess)
                : rebindCycles.Contains((service, stage.Lifecycle)) && rebindSeen.Add((service, stage.Lifecycle))
                    ? nameof(ServiceCycleProfileTemperature.LifecycleRebind)
                    : nameof(ServiceCycleProfileTemperature.Warm);
            output.Add(new TraceDashboardCycle(
                stage.StartMilliseconds,
                checked((int)service - 1),
                service,
                stage.Lifecycle,
                pair.Key.Cycle,
                stage.CaptureFrame,
                stage.DispatchFrame,
                temperature,
                stage.CaptureMilliseconds,
                stage.HandoffMilliseconds,
                stage.DeriveMilliseconds,
                stage.ProjectMilliseconds,
                stage.DispatchMilliseconds,
                stage.WorkerAtMilliseconds,
                stage.DispatchAtMilliseconds,
                stage.ActionCount,
                stage.Committed,
                stage.Skipped,
                stage.Failed,
                stage.HasCapture,
                stage.HasWorker));
        }
        return output.ToArray();
    }

    /// <summary>
    /// Every cycle the trace says started must be one row of its own. A cycle whose start predates
    /// the recording is legal and produces a row with no start, so the count is of rows that observed
    /// one rather than of rows outright — an id colliding with another lifecycle's would swallow a
    /// start and show up here as one row short.
    /// </summary>
    private static void ReconcileStartedCycles(
        List<TraceDashboardEvent> events,
        Dictionary<(ulong Service, ulong Lifecycle, ulong Cycle), CycleStages> stages)
    {
        var started = new Dictionary<ulong, int>();
        foreach (var item in events)
        {
            if (item.Service == 0 || item.Cycle == 0) continue;
            if (item.Kind != nameof(ServiceCycleSemanticEventKind.CycleStarted)) continue;
            started[item.Service] = started.TryGetValue(item.Service, out var count) ? count + 1 : 1;
        }

        var rows = new Dictionary<ulong, int>();
        foreach (var pair in stages)
        {
            if (pair.Value.StartedCount == 0) continue;
            rows[pair.Key.Service] =
                rows.TryGetValue(pair.Key.Service, out var count) ? count + 1 : 1;
        }

        foreach (var pair in started)
        {
            rows.TryGetValue(pair.Key, out var kept);
            if (kept == pair.Value) continue;
            throw new InvalidOperationException(
                $"Service {pair.Key} started {pair.Value} cycles but the dashboard kept {kept} rows " +
                "carrying a start. Cycle rows and CycleStarted events must reconcile exactly.");
        }
    }

    private static TraceDashboardService[] Services(
        List<TraceDashboardEvent> events,
        List<TraceDashboardStageAggregate> aggregates,
        TraceDashboardCycle[] cycles,
        ServiceCycleTraceRoster roster)
    {
        var rostered = RosteredNames(roster);
        var output = new List<TraceDashboardService>();
        foreach (var service in events.Select(x => x.Service).Where(x => x != 0).Distinct().OrderBy(x => x))
        {
            var ordinal = checked((int)service - 1);
            var commits = events
                .Where(x => x.Service == service && x.Kind == nameof(ServiceCycleSemanticEventKind.ActionCommitted))
                .ToArray();
            // A commit is either a verified native mutation or a publication, and the publishing
            // shape is unforgeable: ServiceActionResult refuses to build a native commit with no
            // native evidence. So a service whose every commit carried none is the collector.
            var role = commits.Length == 0
                ? "Unobserved"
                : commits.All(x => x.NativeCallsAttempted == 0 && x.MutationAttempts == 0)
                    ? "Source"
                    : "Execution";
            // The recording's own answer first. Everything below it is inference kept for captures
            // written before a roster was recorded, and inference is exactly what produced "Service 2"
            // for a feature the suite has always had a name for.
            rostered.TryGetValue(service, out var recorded);
            var profiled = FeatureStageName(aggregates, ordinal);
            var named = recorded is not null || profiled is not null || role == "Source";
            output.Add(new TraceDashboardService(
                ordinal,
                service,
                recorded ?? profiled ?? (role == "Source"
                    ? "World collection"
                    : "Service " + (ordinal + 1).ToString(CultureInfo.InvariantCulture)),
                role,
                cycles.Count(x => x.Service == service),
                named));
        }
        return output.ToArray();
    }

    /// <summary>
    /// The names the recording itself wrote down, keyed by the identity the records carry. A service
    /// with an entry but no display name falls back to the identity it registered, which is still a
    /// name rather than an ordinal.
    /// </summary>
    private static Dictionary<ulong, string> RosteredNames(ServiceCycleTraceRoster roster)
    {
        var output = new Dictionary<ulong, string>();
        foreach (var entry in roster.Entries)
        {
            if (!string.Equals(entry.Kind, ServiceCycleTraceRoster.ServiceKind, StringComparison.Ordinal))
                continue;
            output[entry.Identity] = entry.DisplayName.Length == 0 ? entry.MachineId : entry.DisplayName;
        }
        return output;
    }

    /// <summary>
    /// A service's name from the profile span block it owns. Suite spans are shared by every ordinal
    /// and name none of them, so only a feature block answers.
    /// </summary>
    private static string? FeatureStageName(List<TraceDashboardStageAggregate> aggregates, int ordinal)
    {
        foreach (var row in aggregates)
        {
            if (row.Service != ordinal || row.StageCode < FirstFeatureStageCode) continue;
            var words = row.Stage.Split(' ');
            if (words.Length >= 2) return words[0] + " " + words[1];
        }
        return null;
    }

    private const int FirstFeatureStageCode = 1_000;

    /// <summary>The frame a stage names when it ran outside any pump frame, or the wire never said.</summary>
    private const long Unframed = -1;

    /// <summary>
    /// The stage timestamps of one cycle, folded in as its events arrive in any order.
    /// </summary>
    private sealed class CycleStages
    {
        private Stamp _captureStarted;
        private Stamp _captureCompleted;
        private Stamp _evaluationStarted;
        private Stamp _statePublished;
        private Stamp _evaluationCompleted;
        private Stamp _dispatchFirst;

        internal ulong Lifecycle { get; private set; }
        internal long CaptureFrame { get; private set; } = Unframed;
        internal long DispatchFrame { get; private set; } = Unframed;

        internal double CaptureMilliseconds { get; private set; }
        internal double DispatchMilliseconds { get; private set; }
        internal int ActionCount { get; private set; }
        internal int Committed { get; private set; }
        internal int Skipped { get; private set; }
        internal int Failed { get; private set; }
        internal int StartedCount { get; private set; }
        internal bool HasCapture => _captureCompleted.Present;
        internal bool HasWorker => _evaluationStarted.Present && _evaluationCompleted.Present;

        internal double StartMilliseconds =>
            _captureStarted.Present ? _captureStarted.At :
            _captureCompleted.Present ? _captureCompleted.At :
            _evaluationStarted.Present ? _evaluationStarted.At : _dispatchFirst.At;

        internal double WorkerAtMilliseconds => _evaluationStarted.At;
        internal double DispatchAtMilliseconds => _dispatchFirst.At;

        /// <summary>Queue latency, not CPU: the cycle waited here rather than working.</summary>
        internal double HandoffMilliseconds => HasCapture && _evaluationStarted.Present
            ? Positive(_evaluationStarted.At - _captureCompleted.At)
            : 0;

        /// <summary>
        /// The evaluator's own work and the snapshot allocation together. A cycle that faulted before
        /// publishing state has no split to make, so all of its worker time lands here.
        /// </summary>
        internal double DeriveMilliseconds => !HasWorker
            ? 0
            : _statePublished.Present
                ? Positive(_statePublished.At - _evaluationStarted.At)
                : Positive(_evaluationCompleted.At - _evaluationStarted.At);

        internal double ProjectMilliseconds => HasWorker && _statePublished.Present
            ? Positive(_evaluationCompleted.At - _statePublished.At)
            : 0;

        internal void Observe(TraceDashboardEvent item)
        {
            if (item.Lifecycle != 0) Lifecycle = item.Lifecycle;
            switch (item.Kind)
            {
                case nameof(ServiceCycleSemanticEventKind.CycleStarted):
                    StartedCount++;
                    break;
                case nameof(ServiceCycleSemanticEventKind.CaptureStarted):
                    _captureStarted = new Stamp(item.OffsetMilliseconds);
                    ObserveCaptureFrame(item);
                    break;
                case nameof(ServiceCycleSemanticEventKind.CaptureCompleted):
                    _captureCompleted = new Stamp(item.OffsetMilliseconds);
                    CaptureMilliseconds = item.DurationMilliseconds;
                    ObserveCaptureFrame(item);
                    break;
                case nameof(ServiceCycleSemanticEventKind.EvaluationStarted):
                    _evaluationStarted = new Stamp(item.OffsetMilliseconds);
                    break;
                case nameof(ServiceCycleSemanticEventKind.StatePublished):
                    _statePublished = new Stamp(item.OffsetMilliseconds);
                    break;
                case nameof(ServiceCycleSemanticEventKind.EvaluationCompleted):
                    _evaluationCompleted = new Stamp(item.OffsetMilliseconds);
                    ActionCount = item.ActionCount;
                    break;
                case nameof(ServiceCycleSemanticEventKind.ActionCommitted):
                case nameof(ServiceCycleSemanticEventKind.ActionSkipped):
                case nameof(ServiceCycleSemanticEventKind.ActionRejected):
                case nameof(ServiceCycleSemanticEventKind.ActionFaulted):
                    ObserveAction(item);
                    break;
            }
        }

        private void ObserveCaptureFrame(TraceDashboardEvent item)
        {
            if (item.Framed) CaptureFrame = item.Frame;
        }

        private void ObserveAction(TraceDashboardEvent item)
        {
            DispatchMilliseconds += item.DurationMilliseconds;
            if (!_dispatchFirst.Present || item.OffsetMilliseconds < _dispatchFirst.At)
                _dispatchFirst = new Stamp(item.OffsetMilliseconds);
            if (item.Framed) DispatchFrame = item.Frame;
            switch (item.Kind)
            {
                case nameof(ServiceCycleSemanticEventKind.ActionCommitted): Committed++; break;
                case nameof(ServiceCycleSemanticEventKind.ActionSkipped): Skipped++; break;
                default: Failed++; break;
            }
        }

        private static double Positive(double value) => value > 0 ? value : 0;

        /// <summary>
        /// A stage timestamp that knows whether it was ever set. Offsets are relative to the window,
        /// so zero is a legal instant and cannot stand in for "did not happen".
        /// </summary>
        private readonly record struct Stamp(double At)
        {
            internal bool Present { get; } = true;
        }
    }

    private static TraceDashboardDecision[] Decisions(
        DecisionJournalRecord[] records,
        List<TraceDashboardEvent> events,
        long firstTicks,
        long lastTicks)
    {
        var output = new List<TraceDashboardDecision>();
        foreach (var record in records)
        {
            if (record.Kind is not (DecisionJournalRecordKind.DecisionSpan or DecisionJournalRecordKind.Action) ||
                record.LastTimestampTicks < firstTicks || record.FirstTimestampTicks > lastTicks)
                continue;
            var worker = WorkerDecision(in record, events);
            var action = record.Kind == DecisionJournalRecordKind.Action;
            output.Add(new TraceDashboardDecision(
                record.Kind.ToString(),
                Offset(Math.Max(record.FirstTimestampTicks, firstTicks), firstTicks),
                Offset(Math.Min(record.LastTimestampTicks, lastTicks), firstTicks),
                record.Service.Value,
                record.FirstCycle,
                record.LastCycle,
                record.RepeatCount,
                action ? "Unavailable" : record.DecisionOutcomeKind.ToString(),
                action ? "Unavailable" : DecisionJournalValueNames.Decision(record.DecisionOutcomeCode),
                action
                    ? record.ActionOutcome.Disposition + "/" + DecisionJournalValueNames.Action(record.ActionOutcome.Code)
                    : record.DecisionOutcomeKind + "/" + DecisionJournalValueNames.Decision(record.DecisionOutcomeCode),
                worker.Samples,
                worker.AverageMilliseconds,
                worker.MicrosecondsPerCapturedCandidate,
                worker.MicrosecondsPerPlannedAction,
                action ? record.ActionOrdinal : 0,
                action ? record.Attribution.CandidateId.ToString("D") : string.Empty,
                action ? record.Attribution.NativeType.ToString() : string.Empty,
                action ? record.Attribution.ListId.ToString("D") : string.Empty,
                action ? record.Attribution.ViewId.ToString("D") : string.Empty,
                action ? record.Attribution.RouteStatus.ToString() : string.Empty,
                action
                    ? DecisionJournalValueNames.Action(record.ActionOutcome.Code)
                    : DecisionJournalValueNames.Decision(record.DecisionOutcomeCode)));
        }
        return output.ToArray();
    }

    private static WorkerDecisionMetrics WorkerDecision(
        in DecisionJournalRecord record,
        List<TraceDashboardEvent> events)
    {
        var samples = 0;
        var totalMilliseconds = 0d;
        foreach (var item in events)
        {
            if (item.Kind == nameof(ServiceCycleSemanticEventKind.EvaluationCompleted) &&
                item.Service == record.Service.Value &&
                item.Cycle >= record.FirstCycle &&
                item.Cycle <= record.LastCycle)
            {
                samples++;
                totalMilliseconds += item.DurationMilliseconds;
            }
        }

        if (samples == 0)
            return default;
        var average = totalMilliseconds / samples;
        return new WorkerDecisionMetrics(
            samples,
            average,
            null,
            null);
    }

    private static TraceDashboardStageAggregate Aggregate(
        in ServiceCycleProfileRecord record,
        in ServiceCycleProfileCalibration calibration)
    {
        var averageTicks = (double)record.TotalElapsedRawTicks / record.OccurrenceCount;
        var operations = record.Operations;
        return new TraceDashboardStageAggregate(
            record.StageCode,
            ServiceCycleProfileNames.Stage(record.StageCode),
            record.ServiceOrdinal,
            record.Temperature.ToString(),
            record.OccurrenceCount,
            Microseconds(record.TotalElapsedRawTicks, calibration.TimestampFrequency),
            Microseconds(averageTicks, calibration.TimestampFrequency),
            Microseconds(record.MinimumElapsedRawTicks, calibration.TimestampFrequency),
            Microseconds(record.MaximumElapsedRawTicks, calibration.TimestampFrequency),
            calibration.AllocationAvailable
                ? (double)record.TotalAllocatedBytes / record.OccurrenceCount
                : null,
            Operations(in operations));
    }

    private static TraceDashboardStageSample Sample(
        in ServiceCycleProfileRecord record,
        in ServiceCycleProfileCalibration calibration,
        long origin)
    {
        var timestamp = CalibratedTimestamp(record.FirstStartedAtRawTicks, in calibration);
        var operations = record.Operations;
        return new TraceDashboardStageSample(
            Offset(timestamp, origin),
            record.StageCode,
            ServiceCycleProfileNames.Stage(record.StageCode),
            record.ServiceOrdinal,
            record.Cycle,
            record.Frame,
            record.Temperature.ToString(),
            Microseconds(record.TotalElapsedRawTicks, calibration.TimestampFrequency),
            calibration.AllocationAvailable ? checked((long)record.TotalAllocatedBytes) : null,
            Operations(in operations));
    }

    private static TraceDashboardOperations Operations(in ServiceCycleProfileOperations value) => new(
        value.ReflectedFieldReads,
        value.ReflectedMethodCalls,
        value.StableIdReads,
        value.ListEntries,
        value.InvocationArgumentArrays,
        value.RecordCopies);

    private static long CalibratedTimestamp(long rawTimestamp, in ServiceCycleProfileCalibration calibration)
    {
        var delta = checked(rawTimestamp - calibration.RawTimestamp);
        var monotonicDelta = checked((long)decimal.Truncate(
            (decimal)delta * MonotonicDuration.TicksPerSecond / calibration.TimestampFrequency));
        return checked(calibration.MonotonicTimestampTicks + monotonicDelta);
    }

    private static string Lane(ServiceCycleSemanticEventKind kind) => kind switch
    {
        ServiceCycleSemanticEventKind.CaptureStarted or
        ServiceCycleSemanticEventKind.CaptureCompleted or
        ServiceCycleSemanticEventKind.CaptureUnavailable or
        ServiceCycleSemanticEventKind.CaptureFaulted => "Capture",
        ServiceCycleSemanticEventKind.CycleQueued or
        ServiceCycleSemanticEventKind.CycleStarted or
        ServiceCycleSemanticEventKind.CycleCompleted or
        ServiceCycleSemanticEventKind.CycleFaulted or
        ServiceCycleSemanticEventKind.CycleOrphaned or
        ServiceCycleSemanticEventKind.EvaluationStarted or
        ServiceCycleSemanticEventKind.EvaluationCompleted or
        ServiceCycleSemanticEventKind.EvaluationDeferred or
        ServiceCycleSemanticEventKind.EvaluationFaulted => "Worker",
        ServiceCycleSemanticEventKind.StatePublished or
        ServiceCycleSemanticEventKind.BatchPublished or
        ServiceCycleSemanticEventKind.BatchCompleted or
        ServiceCycleSemanticEventKind.BatchAborted or
        ServiceCycleSemanticEventKind.BatchOrphaned => "Publication",
        ServiceCycleSemanticEventKind.ActionAttempted or
        ServiceCycleSemanticEventKind.ActionCommitted or
        ServiceCycleSemanticEventKind.ActionSkipped or
        ServiceCycleSemanticEventKind.ActionRejected or
        ServiceCycleSemanticEventKind.ActionFaulted => "Action",
        ServiceCycleSemanticEventKind.LifecycleRequested or
        ServiceCycleSemanticEventKind.LifecycleActivated or
        ServiceCycleSemanticEventKind.LifecycleRetired or
        ServiceCycleSemanticEventKind.StartAttempted or
        ServiceCycleSemanticEventKind.StartDeferred or
        ServiceCycleSemanticEventKind.StartFaulted or
        ServiceCycleSemanticEventKind.StartReady => "Lifecycle",
        _ => "Control",
    };

    private static double Offset(long ticks, long origin) => Milliseconds(checked(ticks - origin));
    private static double Milliseconds(long ticks) => ticks / TicksPerMillisecond;
    private static double Microseconds(double ticks, long frequency) => ticks * 1_000_000d / frequency;

    private readonly record struct JournalRun(ulong Run, DecisionJournalRecord[] Records);
    private readonly record struct WorkerDecisionMetrics(
        int Samples,
        double AverageMilliseconds,
        double? MicrosecondsPerCapturedCandidate,
        double? MicrosecondsPerPlannedAction);
}
#endif

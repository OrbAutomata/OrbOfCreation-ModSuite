#if SERVICE_CYCLE_PROFILE
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using OrbModding.Common;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Status;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata.GameMcp;

internal static class GameMcpHealthProjection
{
    private static readonly string GameMcpDllSha256 = ComputeExecutingDllSha256();

    /// <summary>
    /// The save's own reset count, read by id off the game's <c>WorldResets</c> variable — "How
    /// many times you've reset the world", under the word the Statistics screen prints over it.
    /// Empty while no world is published, because the page's next line already says that.
    /// </summary>
    /// <remarks>
    /// <c>GlobalVariables.GetGenerationAttr()</c> is not this number and holds no number at all: it
    /// returns the <c>AttributeSO</c> glossary word "Generation", which sits beside
    /// <c>GenerationRateTime</c> and <c>GenerationSplash</c> and heads the tooltip section for how
    /// fast a resource is gained.
    /// </remarks>
    private static string GameMcpWorldResetsText(GameMcpFrameContext context)
    {
        if (!GameMcpWorldRead.IsWorldPublished(context)) return string.Empty;
        return WorldLookup.TryFind(
            context.World!.Snapshot.IntVariables,
            KnownEntities.WorldResets.Uuid,
            out var resets)
            ? ", worldResets " + resets.Value.ToLong().ToString(CultureInfo.InvariantCulture)
            : ", worldResets unavailable";
    }

    internal static string ProjectGameMcpHealthText(GameMcpFrameContext context)
    {
        var stopped = context.Runtime?.EmergencyStopEngaged ??
            context.Configuration.Snapshot.Safety.EmergencyDisable;
        var result = new StringBuilder()
            .Append("available").Append('\n')
            .Append("build: ").Append(PluginIds.Version).Append(" dll sha256 ")
            .Append(GameMcpDllSha256).Append('\n')
            .Append("scene: ").Append(GameMcpTextFormatter.Plain(context.SceneName)).Append('\n')
            // The same lifecycle fact game_probe reports and the world reads refuse on, so the three
            // cannot hold three beliefs about whether a game exists.
            .Append("lifecycle: ").Append(context.LifecycleState.ToString())
            // Named for what it counts: the lifecycle boundaries this process has crossed, from the
            // plugin's load. It was called "generation", which a reader took for the save's NG+
            // number — so that number is now printed beside it, under the game's own word for it,
            // and neither counter borrows the other's name.
            .Append(", lifecycleTransitions ")
            .Append(context.LifecycleGeneration.ToString(CultureInfo.InvariantCulture))
            .Append(GameMcpWorldResetsText(context)).Append('\n')
            // The scene name alone cannot tell a caller which run a verdict describes: the runtime
            // outlives every scene change, so the same scene answered both ways across one session.
            // The publication counter is what actually moves, so it is published — and a lifecycle
            // boundary flushes the publication, so this reads "not published" again once the run
            // it described is gone. It counts publications and says so, because three counters
            // share this page and none of them is another's number.
            .Append("world: ").Append(GameMcpWorldRead.IsWorldPublished(context)
                ? "publication " +
                    context.World!.Generation.Value.ToString(CultureInfo.InvariantCulture)
                : "not published").Append('\n')
            .Append("emergency stop: ").Append(stopped ? "engaged" : "clear").Append('\n');
        // Capability availability is exception-shaped: the leading "available" answers for every
        // unlisted capability. Feature explanations below are independent of their health state.
        if (!context.RuntimeAvailable) result.Append("runtime: unavailable").Append('\n');
        if (!context.NativeContractsAvailable) result.Append("native contracts: unavailable").Append('\n');
        var unavailableVerbs = (context.Runtime?.UnavailableGameActions ??
            Array.Empty<UnavailableGameAction>()).ToList();
        if (!context.ModalDismissAvailable)
            unavailableVerbs.Add(
                new UnavailableGameAction("game_modal", context.ModalDismissUnavailableReason));
        foreach (var verb in unavailableVerbs)
            result.Append(verb.Verb).Append(": unavailable").Append('\n');
        if (!context.RuntimeAvailable && context.RuntimeNotAvailableReason.Length > 0)
            result.Append("runtime reason: ").Append(
                GameMcpTextFormatter.Plain(context.RuntimeNotAvailableReason)).Append('\n');
        foreach (var verb in unavailableVerbs)
        {
            if (verb.Reason.Length == 0) continue;
            result.Append(verb.Verb).Append(" reason: ").Append(
                GameMcpTextFormatter.Plain(verb.Reason)).Append('\n');
        }
        foreach (var committed in CommittedAfterThrow.Latest)
            result.Append(GameMcpTextFormatter.Plain(committed.Feature))
                .Append(": committed, but the game threw; reference ").Append(committed.Reference)
                .Append(" is in the suite log").Append('\n');
        // The load normalizes three settings the documented verbs assume, and a caller never asked
        // for them — so when one did not land, the develop queue and the spell cancel it silently
        // costs are refusals nothing else on the wire can explain.
        if (context.AgentSettingsFailure.Length > 0)
            result.Append("agent settings: ").Append(
                GameMcpTextFormatter.Plain(context.AgentSettingsFailure)).Append('\n');

        // One roster, one line. The state groups are the facts; the word "features" in front of
        // every one of them was the same nine characters repeated, on a roster that moved twice in
        // a six-call round. Every state, every reason code and every name still ships.
        var featureGroups = context.FeatureStatuses
            .GroupBy(feature => new { feature.State, feature.Reason.Code })
            .OrderBy(group => group.Key.State.ToString(), StringComparer.Ordinal)
            .ThenBy(group => group.Key.Code.ToString(), StringComparer.Ordinal)
            .Select(group =>
            {
                var state = GameMcpWireValues.Snake(group.Key.State.ToString());
                var reasonCode = GameMcpWireValues.Snake(group.Key.Code.ToString());
                var qualifier = reasonCode.Length > 0 && reasonCode != "none" &&
                    !string.Equals(reasonCode, state, StringComparison.Ordinal)
                        ? " (" + reasonCode + ")"
                        : string.Empty;
                return state + qualifier + ": " + string.Join(", ", group.Select(
                    feature => GameMcpTextFormatter.Plain(
                        CanonicalGameMcpFeatureName(feature.DisplayName))));
            })
            .ToArray();
        if (featureGroups.Length > 0)
            result.Append("features ").Append(string.Join("; ", featureGroups)).Append('\n');
        foreach (var feature in context.FeatureStatuses)
        {
            var held = feature.State is (FeatureStatusState.TemporarilyBlocked or FeatureStatusState.Locked or
                    FeatureStatusState.Faulted) &&
                feature.Reason.Code != FeatureStatusReasonCode.EmergencyDisabled &&
                feature.Reason.Summary.Length > 0;
            if (held)
                result.Append(CanonicalGameMcpFeatureName(feature.DisplayName)).Append(" reason: ")
                    .Append(GameMcpTextFormatter.Plain(feature.Reason.Summary)).Append('\n');
            if (!string.IsNullOrWhiteSpace(feature.Detail))
                result.Append(CanonicalGameMcpFeatureName(feature.DisplayName)).Append(" details:").Append('\n')
                    .Append(GameMcpTextFormatter.Plain(feature.Detail)).Append('\n');
        }

        var runtimeServices = context.Runtime?.Services ?? Array.Empty<AutomataServiceFrameFacts>();
        var serviceGroups = runtimeServices
            .GroupBy(service =>
                service.HasRunner
                    ? service.Runner.Fault.IsValid ? "faulted" : service.Runner.Phase.ToString()
                    : "unavailable")
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
                GameMcpWireValues.Snake(group.Key) + ": " + string.Join(", ", group.Select(
                    service => GameMcpTextFormatter.Plain(
                        CanonicalGameMcpFeatureName(service.DisplayName)))))
            .ToArray();
        if (serviceGroups.Length > 0)
            result.Append("services ").Append(string.Join("; ", serviceGroups)).Append('\n');
        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// The one name a suite feature answers to, on health and on the automation list alike.
    /// </summary>
    /// <remarks>
    /// Health uses the canonical control IDs. Features without an on/off control remain present
    /// in health; their diagnostic presence does not imply a writable breaker.
    /// </remarks>
    internal static string CanonicalGameMcpFeatureName(string name) =>
        string.Equals(name, "Orb Mentor", StringComparison.Ordinal)
            ? "mentor"
            : GameMcpWireValues.Snake(name);

    internal static string ProjectGameMcpTraceHealthText(GameMcpFrameContext context)
    {
        var status = context.TraceWriterStatus;
        if (status.State == DecisionJournalStatusState.Unavailable)
        {
            // The writer and the collection pass are two independent facts, and the pass is the one
            // a session driving the game asks about. Returning here on the writer alone made the
            // spans unreadable in exactly the runs that had no trace to open afterwards.
            return "unavailable\nreason: the decision journal writer is not active in this runtime\n" +
                GameMcpCollectionSpans.Describe(context);
        }
        var result = new StringBuilder()
            .Append("available").Append('\n')
            .Append("trace writer: ").Append(
                GameMcpWireValues.Snake(status.State.ToString())).Append('\n');
        var outcome = GameMcpWireValues.Snake(status.Result.ToString());
        if (outcome.Length > 0 && outcome != "none")
            result.Append("result: ").Append(outcome).Append('\n');
        result.Append("records: accepted ").Append(
                status.AcceptedRecords.ToString(CultureInfo.InvariantCulture))
            .Append(", written ").Append(
                status.WrittenRecords.ToString(CultureInfo.InvariantCulture))
            .Append(", discarded ").Append(
                status.DiscardedRecords.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("bytes written: ").Append(
                status.BytesWritten.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("segments: written ").Append(
                status.WrittenSegments.ToString(CultureInfo.InvariantCulture))
            .Append(", retained ").Append(
                status.RetainedSegments.ToString(CultureInfo.InvariantCulture)).Append('\n')
            .Append("pending blocks: ").Append(
                status.PendingBlocks.ToString(CultureInfo.InvariantCulture))
            .Append(" (peak ").Append(
                status.PeakPendingBlocks.ToString(CultureInfo.InvariantCulture))
            .Append(")").Append('\n');
        if (status.ArtifactName.Length > 0)
            result.Append("artifact: ").Append(
                GameMcpTextFormatter.Plain(status.ArtifactName)).Append('\n');
        if (status.FaultSite.Length > 0)
            result.Append("fault site: ").Append(
                GameMcpTextFormatter.Plain(status.FaultSite)).Append('\n');
        if (status.FaultMessage.Length > 0)
            result.Append("fault: ").Append(
                GameMcpTextFormatter.Plain(status.FaultMessage)).Append('\n');
        result.Append("revision: ").Append(
            context.TraceWriterRevision.ToString(CultureInfo.InvariantCulture)).Append('\n');
        result.Append(GameMcpCollectionSpans.Describe(context));
        return result.ToString();
    }

    private static string ComputeExecutingDllSha256()
    {
        try
        {
            var path = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "unavailable";
            using var stream = File.OpenRead(path);
            using var sha = SHA256.Create();
            // A build fingerprint answers one question: same DLL or not. Six bytes settle it; the
            // remaining twenty-six were a constant sixty-four-character tax on every health call.
            return string.Concat(sha.ComputeHash(stream).Take(6).Select(
                value => value.ToString("x2", CultureInfo.InvariantCulture)));
        }
        catch (Exception)
        {
            return "unavailable";
        }
    }
}
#endif

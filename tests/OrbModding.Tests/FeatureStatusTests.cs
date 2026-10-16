using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class FeatureStatusTests
{
    private static readonly FeatureStatusKey Key = new("plugin.test", "feature.test");

    [Fact]
    public void PublicCodes_AreAppendOnlyAndStable()
    {
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["ConfigurationDisabled"] = 0,
                ["Locked"] = 1,
                ["NotReady"] = 2,
                ["Operational"] = 3,
                ["TemporarilyBlocked"] = 4,
                ["ContractUnavailable"] = 5,
                ["Degraded"] = 6,
                ["Faulted"] = 7,
            },
            Enum.GetValues<FeatureStatusState>().ToDictionary(value => value.ToString(), value => (int)value));
        Assert.Equal(
            new Dictionary<string, int>
            {
                ["None"] = 0,
                ["ConfigurationDisabled"] = 100,
                ["ParentFeatureDisabled"] = 101,
                ["EmergencyDisabled"] = 102,
                ["ProgressionLocked"] = 200,
                ["GameplayNotReady"] = 300,
                ["RegistryNotReady"] = 301,
                ["LifecycleTransition"] = 302,
                ["QueueNotReady"] = 303,
                ["Initializing"] = 304,
                ["TemporarySafetyBlock"] = 400,
                ["QueueFull"] = 401,
                ["NativeBusy"] = 402,
                ["ManualPause"] = 403,
                ["TargetingInProgress"] = 404,
                ["CapacityExceeded"] = 405,
                ["MutationQuarantined"] = 406,
                ["ActionFamilyConflict"] = 407,
                ["ContractUnavailable"] = 500,
                ["ContractMismatch"] = 501,
                ["IdentityMismatch"] = 502,
                ["EvidenceUnavailable"] = 503,
                ["PartialCapabilityUnavailable"] = 600,
                ["NativeMutationFailed"] = 700,
                ["PostconditionFailed"] = 701,
                ["RuntimeFailure"] = 702,
                ["InvariantViolation"] = 703,
            },
            Enum.GetValues<FeatureStatusReasonCode>().ToDictionary(value => value.ToString(), value => (int)value));
    }

    [Theory]
    [InlineData(FeatureStatusState.ConfigurationDisabled, false, FeatureStatusReasonCode.ConfigurationDisabled)]
    [InlineData(FeatureStatusState.Locked, true, FeatureStatusReasonCode.ProgressionLocked)]
    [InlineData(FeatureStatusState.NotReady, true, FeatureStatusReasonCode.GameplayNotReady)]
    [InlineData(FeatureStatusState.Operational, true, FeatureStatusReasonCode.None)]
    [InlineData(FeatureStatusState.TemporarilyBlocked, true, FeatureStatusReasonCode.TemporarySafetyBlock)]
    [InlineData(FeatureStatusState.ContractUnavailable, true, FeatureStatusReasonCode.ContractUnavailable)]
    [InlineData(FeatureStatusState.Degraded, true, FeatureStatusReasonCode.PartialCapabilityUnavailable)]
    [InlineData(FeatureStatusState.Faulted, true, FeatureStatusReasonCode.RuntimeFailure)]
    public void Snapshot_RepresentsEveryRequiredRuntimeState(
        FeatureStatusState state,
        bool configuredEnabled,
        FeatureStatusReasonCode reasonCode)
    {
        var reason = reasonCode == FeatureStatusReasonCode.None
            ? default
            : new FeatureStatusReason(reasonCode, "evidence");

        var status = new FeatureStatusSnapshot(Key, "Feature", configuredEnabled, state, reason, 3);

        Assert.Equal(state, status.State);
        Assert.Equal(configuredEnabled, status.ConfiguredEnabled);
        Assert.Equal(reasonCode, status.Reason.Code);
        Assert.Contains(configuredEnabled ? "Configured: Enabled" : "Configured: Disabled", FeatureStatusPresenter.Format(status));
    }

    [Fact]
    public void Snapshot_RejectsContradictoryConfigurationAndReasonEvidence()
    {
        Assert.Throws<ArgumentException>(() => new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.ConfigurationDisabled,
            new FeatureStatusReason(FeatureStatusReasonCode.ConfigurationDisabled, "off")));
        Assert.Throws<ArgumentException>(() => new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.Operational,
            new FeatureStatusReason(FeatureStatusReasonCode.RuntimeFailure, "failure")));
        Assert.Throws<ArgumentException>(() => new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.NotReady));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FeatureStatusReason((FeatureStatusReasonCode)699, "unknown"));
    }

    [Theory]
    [InlineData(FeatureStatusState.ConfigurationDisabled, false, FeatureRuntimePresentationState.Off)]
    [InlineData(FeatureStatusState.Locked, true, FeatureRuntimePresentationState.Waiting)]
    [InlineData(FeatureStatusState.NotReady, true, FeatureRuntimePresentationState.Waiting)]
    [InlineData(FeatureStatusState.Operational, true, FeatureRuntimePresentationState.Operational)]
    [InlineData(FeatureStatusState.TemporarilyBlocked, true, FeatureRuntimePresentationState.Blocked)]
    [InlineData(FeatureStatusState.ContractUnavailable, true, FeatureRuntimePresentationState.Unavailable)]
    [InlineData(FeatureStatusState.Degraded, true, FeatureRuntimePresentationState.Degraded)]
    [InlineData(FeatureStatusState.Faulted, true, FeatureRuntimePresentationState.Faulted)]
    public void Presentation_KeepsConfiguredIntentSeparateFromRuntimeHealth(
        FeatureStatusState state,
        bool configuredEnabled,
        FeatureRuntimePresentationState expectedRuntime)
    {
        var reason = state == FeatureStatusState.Operational
            ? default
            : new FeatureStatusReason(
                state == FeatureStatusState.ConfigurationDisabled
                    ? FeatureStatusReasonCode.ConfigurationDisabled
                    : FeatureStatusReasonCode.RuntimeFailure,
                "runtime evidence");
        var status = new FeatureStatusSnapshot(Key, "Feature", configuredEnabled, state, reason);

        var presentation = FeatureStatusPresenter.Present(status);

        Assert.Equal(
            configuredEnabled ? FeatureConfiguredPresentationState.On : FeatureConfiguredPresentationState.Off,
            presentation.ConfiguredState);
        Assert.Equal(configuredEnabled ? "ON" : "OFF", presentation.ConfiguredLabel);
        Assert.Equal(expectedRuntime, presentation.RuntimeState);
    }

    [Fact]
    public void Presentation_MapsOrdinaryTransientWaitReasonsWithoutChangingIntent()
    {
        var status = new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.TemporarilyBlocked,
            new FeatureStatusReason(FeatureStatusReasonCode.QueueFull, "queue is full"));

        var presentation = FeatureStatusPresenter.Present(status);

        Assert.Equal(FeatureConfiguredPresentationState.On, presentation.ConfiguredState);
        Assert.Equal(FeatureRuntimePresentationState.Waiting, presentation.RuntimeState);
    }

    [Fact]
    public void Presentation_BoundsAndWrapsLongDiagnosticText()
    {
        var formatted = FeatureStatusPresenter.BoundAndWrap(
            "one two three four five six seven eight nine ten " + new string('X', 200),
            maximumCharacters: 80,
            lineWidth: 24);

        Assert.Contains('\n', formatted);
        Assert.EndsWith("...", formatted, StringComparison.Ordinal);
        Assert.True(formatted.Length <= 80);
    }

    [Fact]
    public void TooltipPresentation_UsesOneNativeNodePerPhysicalLine()
    {
        var status = new FeatureStatusSnapshot(
            Key,
            "Feature",
            true,
            FeatureStatusState.TemporarilyBlocked,
            new FeatureStatusReason(
                FeatureStatusReasonCode.NativeBusy,
                "The native spell system is busy and will be checked again after its current work settles."));
        var nodes = new List<TooltipNode>();

        TooltipNodeLayout.AddFeatureStatus(nodes, status, new UnityEngine.Color(.4f, 1f, .55f), lineWidth: 42);

        Assert.Equal("Configured: Enabled", nodes[0].Text);
        Assert.Equal("Runtime: Waiting", nodes[1].Text);
        Assert.StartsWith("Reason: ", nodes[2].Text, StringComparison.Ordinal);
        Assert.All(nodes, node =>
        {
            Assert.DoesNotContain('\n', node.Text);
            Assert.DoesNotContain('\r', node.Text);
            Assert.True(node.Text.Length <= 42, $"Tooltip line exceeded its declared width: {node.Text}");
        });
    }

    [Fact]
    public void CompactTooltipPresentation_KeepsDomainStatusOnASeparateLine()
    {
        var status = new FeatureStatusSnapshot(Key, "Artifacts", true, FeatureStatusState.Operational);
        var nodes = new List<TooltipNode>();

        TooltipNodeLayout.AddCompactFeatureStatus(nodes, "Artifacts", status, lineWidth: 72);

        var node = Assert.Single(nodes);
        Assert.Equal("Artifacts: Enabled | Operational", node.Text);
        Assert.DoesNotContain('\n', node.Text);
    }

    [Fact]
    public void Registry_PublishesOnlyCanonicalConditionTransitions()
    {
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        using var registration = registry.Register(Operational(1));

        Assert.False(registration.Update(Operational(1)));
        Assert.True(registration.Update(Blocked("first wording", 1)));
        Assert.False(registration.Update(Blocked("different wording", 1)));
        Assert.True(registration.Update(Blocked("same condition, new lifecycle", 2)));

        Assert.Equal(3, transitions.Count);
        Assert.Equal(
            new[] { FeatureStatusTransitionKind.Added, FeatureStatusTransitionKind.Changed, FeatureStatusTransitionKind.Changed },
            transitions.Select(transition => transition.Kind));
        Assert.Equal(new long[] { 1, 2, 3 }, transitions.Select(transition => transition.Sequence));
    }

    [Fact]
    public void Registry_OrdersSnapshotsAndKeepsSiblingFailuresIndependent()
    {
        var registry = new FeatureStatusRegistry();
        using var later = registry.Register(new FeatureStatusSnapshot(
            new FeatureStatusKey("plugin.z", "feature.b"),
            "Later",
            true,
            FeatureStatusState.Faulted,
            new FeatureStatusReason(FeatureStatusReasonCode.RuntimeFailure, "failed")));
        using var earlier = registry.Register(new FeatureStatusSnapshot(
            new FeatureStatusKey("plugin.a", "feature.a"),
            "Earlier",
            true,
            FeatureStatusState.Operational));

        var snapshot = registry.GetSnapshot();

        Assert.Equal(new[] { "plugin.a/feature.a", "plugin.z/feature.b" }, snapshot.Select(status => status.Key.ToString()));
        Assert.Equal(FeatureStatusState.Operational, snapshot[0].State);
        Assert.Equal(FeatureStatusState.Faulted, snapshot[1].State);
    }

    [Fact]
    public void Registry_RejectsDuplicatePublishersAndRemovesDisposedStatus()
    {
        var registry = new FeatureStatusRegistry();
        var transitions = new List<FeatureStatusTransition>();
        registry.Transitioned += transitions.Add;
        var registration = registry.Register(Operational(0));

        Assert.Throws<InvalidOperationException>(() => registry.Register(Operational(0)));
        registration.Dispose();

        Assert.Empty(registry.GetSnapshot());
        Assert.Equal(FeatureStatusTransitionKind.Removed, transitions[^1].Kind);
        Assert.Equal(Key, transitions[^1].Previous!.Value.Key);
    }

    [Fact]
    public void Registry_IsolatesFailingSubscribers()
    {
        var registry = new FeatureStatusRegistry();
        var received = 0;
        registry.Transitioned += _ => throw new InvalidOperationException("consumer failure");
        registry.Transitioned += _ => received++;

        using var registration = registry.Register(Operational(0));
        registration.Update(Blocked("blocked", 0));

        Assert.Equal(2, received);
    }

    /// <summary>
    /// The registry belongs to the thread that built it, and says so from any other.
    /// </summary>
    /// <remarks>
    /// A dedicated thread rather than <c>Task.Run</c>. The test itself runs on a pool thread, and a
    /// work item queued from one can be picked up by that same thread once it yields — the ownership
    /// check then sees its own thread id, there is no violation to observe, and the test fails on a
    /// scheduling accident rather than on the behaviour it names.
    /// </remarks>
    [Fact]
    public void Registry_RejectsCrossThreadAccess()
    {
        var registry = new FeatureStatusRegistry();
        Exception? observed = null;
        var thread = new Thread(() =>
        {
            try { registry.GetSnapshot(); }
            catch (Exception ex) { observed = ex; }
        });

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(observed);
    }

    private static FeatureStatusSnapshot Operational(long generation) => new(
        Key,
        "Feature",
        true,
        FeatureStatusState.Operational,
        lifecycleGeneration: generation);

    private static FeatureStatusSnapshot Blocked(
        string summary,
        long generation) => new(
        Key,
        "Feature",
        true,
        FeatureStatusState.TemporarilyBlocked,
        new FeatureStatusReason(FeatureStatusReasonCode.NativeBusy, summary),
        generation);
}

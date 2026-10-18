using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using BepInEx.Logging;
using OrbAutomata;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.Journal.Outcomes;
using Xunit;

namespace OrbModding.Tests.Services;

public sealed class AutomataServiceCycleGameActionsTests
{
    private long _lifecycle = 1;

    [Fact]
    public void Composed_actions_rebind_in_creation_order_and_dispose_in_reverse()
    {
        var log = new List<string>();
        var runtime = Create(actions =>
        {
            actions.Add(new RecordingAction<First>(log));
            actions.Add(new RecordingAction<Second>(log));
            actions.Add(new RecordingAction<Third>(log));
        });
        log.Clear();

        _lifecycle++;
        runtime.InvalidateLifecycle();
        var rebound = log.ToArray();
        log.Clear();
        runtime.Dispose();

        Assert.Equal(new[]
        {
            "invalidate First", "bind First",
            "invalidate Second", "bind Second",
            "invalidate Third", "bind Third",
        }, rebound);
        Assert.Equal(new[] { "invalidate Third", "invalidate Second", "invalidate First" }, log);
    }

    [Fact]
    public void A_second_action_of_one_type_fails_composition_and_disposes_the_first()
    {
        var log = new List<string>();
        var startup = new ManualLogSource();

        var runtime = AutomataServiceCycleComposition.TryCreate(
            BepInExAutomataConfiguration.Bind(new ConfigFile()).Current,
            new ConfigGeneration(1),
            HostDependencies(),
            new IAutomataServiceCycleFeature[] { new StrategistServiceCycleFeature() },
            startup,
            actions =>
            {
                actions.Add(new RecordingAction<First>(log));
                actions.Add(new RecordingAction<First>(log));
            });

        Assert.Null(runtime);
        Assert.Contains(startup.Entries, entry =>
            entry.ToString()!.Contains("A second RecordingAction`1 was composed",
                StringComparison.Ordinal));
        Assert.Equal(new[] { "bind First", "bind First", "invalidate First" }, log);
    }

    [Fact]
    public void A_composition_that_fails_partway_disposes_what_it_built_in_reverse()
    {
        var log = new List<string>();

        Assert.Throws<InvalidOperationException>(() => Create(actions =>
        {
            actions.Add(new RecordingAction<First>(log));
            actions.Add(new RecordingAction<Second>(log));
            throw new InvalidOperationException("the third factory failed");
        }));

        Assert.Equal(new[]
        {
            "bind First", "bind Second", "invalidate Second", "invalidate First",
        }, log);
    }

    private AutomataServiceCycleRuntime Create(Action<LifecycleGameActions> compose) =>
        AutomataServiceCycleComposition.Create(
            BepInExAutomataConfiguration.Bind(new ConfigFile()).Current,
            new ConfigGeneration(1),
            HostDependencies(),
            new IAutomataServiceCycleFeature[] { new StrategistServiceCycleFeature() },
            new ManualLogSource(),
            compose);

    private AutomataServiceCycleHostDependencies HostDependencies() =>
        new(() => 1, () => _lifecycle, new ServiceActionOutcomeWindowRegistry());

    private sealed class First { }
    private sealed class Second { }
    private sealed class Third { }

    private sealed class RecordingAction<TName> : NativeGameAction<object>
    {
        private static readonly GameActionCodes Codes = new(
            CommonActionResultCodes.Committed,
            CommonActionResultCodes.Committed,
            CommonActionResultCodes.Committed,
            CommonActionResultCodes.Committed,
            CommonActionResultCodes.Committed);

        private readonly List<string> _log;

        internal RecordingAction(List<string> log)
            : base(typeof(TName).Name, "Test", Codes, static () => 0, static () => true,
                static () => string.Empty,
                (out object? bindings, out string reason) =>
                {
                    log.Add("bind " + typeof(TName).Name);
                    bindings = new object();
                    reason = string.Empty;
                    return true;
                }) =>
            _log = log;

        protected override void OnInvalidate() => _log.Add("invalidate " + typeof(TName).Name);
    }
}

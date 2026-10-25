using System;
using System.Threading;
using OrbAutomata;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Diagnostics;
using OrbModding.Common.Runtime.ServiceCycle.Execution;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.Strategy;
using OrbModding.Common.Runtime.World;
using OrbModding.Tests.Runtime.ServiceCycle.TestSupport;
using Xunit;

namespace OrbModding.Tests.Services.Mentor.Runtime.ServiceCycle;

public sealed class MentorServiceCompositionTests
{
    private static readonly Guid Source =
        Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Recipient =
        Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void CompositionForwardsNativeWarningsAndKeepsRepeatSuppression()
    {
        var warnings = new System.Collections.Generic.List<string>();
        using var status = new OrbAutomata.AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(
                new FeatureStatusKey(PluginIds.SuiteGuid, "mentor"),
                "Mentor", true, FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.GameplayNotReady, "waiting"), 1));
        var adapters = MentorServiceAdapterComposition.Create(new MentorFeatureDependencies(
            static () => throw new InvalidOperationException("mentor epoch unavailable"),
            static _ => true, status, warnings.Add));
        using var natives = adapters.Natives;
        var action = new MentorCycleAction(
            MasteryExperienceDomain.Spell, Recipient, new MentorAmount(1, 0), 5, 1);

        var first = adapters.Definition.TryExecute(action, Configuration(), default);
        var second = adapters.Definition.TryExecute(action, Configuration(), default);

        Assert.Equal(CommonActionResultCodes.AdapterFault, first.Code);
        Assert.Equal(ServiceActionDisposition.Faulted, second.Disposition);
        Assert.Contains("mentor epoch unavailable", Assert.Single(warnings));
    }

    [Fact]
    public void WorldInputIsPlannedOffThreadAndExecutedOnThePumpThread()
    {
        var actions = new ActionPort(Thread.CurrentThread.ManagedThreadId);
        var definition = MentorService.Define(actions);
        using var registry = new ServiceCycleRegistry(1, new LifecycleGeneration(1));
        registry.ConfigurationPublication.Publish(Configuration());
        using var registration = registry.Register(
            definition,
            ServiceActionDispatchPolicy.Single);
        registry.WorldPublication.Publish(World(), new WorldGeneration(2));
        registry.Seal();
        using var pump = new SuiteFramePump(registry);

        ServiceRunnerTestWait.ForWorkerReady(registration);
        Assert.Equal(1, pump.PumpFrame(1).CyclesStarted);
        ServiceRunnerTestWait.ForResponse(registration);
        pump.PumpFrame(2);
        pump.PumpFrame(3);

        Assert.Equal(1, actions.Calls);
        Assert.Equal(Recipient, actions.Recipient);
    }

    [Fact]
    public void DiagnosticsWaitForMentorsOwnCurrentSuccessAndRecoverFromItsWorkerFault()
    {
        var clock = new ThreadSafeTestClock(100);
        using var registry = new ServiceCycleRegistry(2, new LifecycleGeneration(1), clock);
        var configuration = registry.ConfigurationPublication.Publish(new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
        });
        var otherDefinition = new ExecutionServiceDefinition("test.other")
        {
            EvaluationWake = WakePolicy.OnPublication,
        };
        using var other = registry.Register(otherDefinition);
        var mentor = MentorService.Define(new ActionPort(Thread.CurrentThread.ManagedThreadId));
        var metadata = new AutomataServiceMetadata(
            mentor.ServiceId, WakePolicy.Immediate,
            new ServiceFaultRecoveryPolicy(new MonotonicDuration(10), new MonotonicDuration(80)));
        var definition = AutomataService.Define<MentorCycleState, MentorCycleAction>(
            in metadata, static () => new FailFirstMentorWorker(),
            mentor.ShouldStart, mentor.DescribeAction, mentor.TryExecute);
        using var registration = registry.Register(definition);
        registry.Seal();
        using var pump = new SuiteFramePump(registry);
        using var status = new AutomataFeatureStatusReporter(new FeatureStatusRegistry(),
            new FeatureStatusSnapshot(new FeatureStatusKey(PluginIds.SuiteGuid, "mentor"),
                "Mentor", true, FeatureStatusState.NotReady,
                new FeatureStatusReason(FeatureStatusReasonCode.Initializing, "Waiting."), 1));
        using var natives = new MentorNativeAdapter();
        var runtime = new MentorFeatureRuntime(
            new MentorFeatureDependencies(static () => 1, static _ => true, status),
            natives, registration, 1, configuration);
        runtime.ActivateDiagnostics();
        long frame = 3;

        var unrelated = RunCycle(pump, registry, other, ref frame);
        runtime.ObserveFrame(pump, in unrelated);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);
        otherDefinition.StartDecision = ServiceStartDecision.Wait(
            CommonServiceDecisionCodes.NotReady, WakePolicy.OnPublication);

        configuration = registry.ConfigurationPublication.Publish(Configuration());
        runtime.ObserveConfiguration(configuration);
        var failed = RunCycle(pump, registry, registration, ref frame);
        runtime.ObserveFrame(pump, in failed);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, status.Current.Reason.Code);
        Assert.Contains("retrying Evaluation", status.Current.Reason.Summary);

        clock.AdvanceTo(registration.Runner.Snapshot.NextWakeDue);
        var recovered = RunCycle(pump, registry, registration, ref frame);
        ObserveWhileContended(recovered);
        Assert.Equal(FeatureStatusReasonCode.RuntimeFailure, status.Current.Reason.Code);
        var quiet = pump.PumpFrame(frame++);
        Assert.Equal(0, quiet.ResponsesAcquired);
        runtime.ObserveFrame(pump, in quiet);
        Assert.Equal(FeatureStatusState.Operational, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.None, status.Current.Reason.Code);

        configuration = registry.ConfigurationPublication.Publish(Configuration());
        runtime.ObserveConfiguration(configuration);
        runtime.ActivateDiagnostics();
        runtime.ObserveFrame(pump, in recovered);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);
        var current = RunCycle(pump, registry, registration, ref frame);
        runtime.ObserveFrame(pump, in current);
        Assert.Equal(FeatureStatusState.Operational, status.Current.State);

        runtime.ObserveLifecycle(2, configuration);
        runtime.ObserveFrame(pump, in current);
        Assert.Equal(FeatureStatusState.NotReady, status.Current.State);
        Assert.Equal(FeatureStatusReasonCode.Initializing, status.Current.Reason.Code);

        void ObserveWhileContended(SuiteFramePumpReport report)
        {
            using var contention = new HandoffGateContention(registration.Runner);
            contention.Acquire();
            try
            {
                HeldGate.AssertReturnsWhileHeld(
                    () => runtime.ObserveFrame(pump, in report), contention.Release);
            }
            finally { contention.Release(); }
        }
    }

    private static SuiteFramePumpReport RunCycle<TState, TAction>(
        SuiteFramePump pump,
        ServiceCycleRegistry registry,
        ServiceRegistration<TState, TAction> registration,
        ref long frame)
    {
        TestWorldCollector.CollectedAt(registry, frame);
        ServiceRunnerTestWait.ForWorkerReady(registration);
        ServiceCyclePumpTestWait.UntilStart(pump, ref frame);
        ServiceRunnerTestWait.ForResponse(registration);
        var report = ServiceCyclePumpTestWait.UntilResponse(pump, ref frame);
        ServiceRunnerTestWait.ForHandback(registration.Runner);
        return report;
    }

    // Only the first evaluation is injected. Successful evaluation and projection use Mentor's
    // shipping worker, and the real pump records the fault and recovery observed by the runtime.
    private sealed class FailFirstMentorWorker :
        IServiceCycleWorkerDefinition<MentorCycleState, MentorCycleAction>
    {
        private readonly MentorWorkerDefinition _inner = new();
        private bool _failed;

        public MentorCycleState CreateState(LifecycleGeneration lifecycle) => _inner.CreateState(lifecycle);
        public void ReleaseState(ref MentorCycleState state) => _inner.ReleaseState(ref state);
        public void ProjectState(in MentorCycleState state, in ServiceProjectionContext context,
            ServiceStateProjectionBuilder output) => _inner.ProjectState(in state, in context, output);

        public WakePolicy Evaluate(in SuiteRuntimeConfiguration config, GameWorldState world,
            SuiteStrategy strategy, in ServiceCycleContext context, ref MentorCycleState state,
            ServiceActionWriter<MentorCycleAction> actions)
        {
            if (!_failed)
            {
                _failed = true;
                throw new InvalidOperationException("Mentor evaluator failed once.");
            }
            return _inner.Evaluate(in config, world, strategy, in context, ref state, actions);
        }
    }

    private static SuiteRuntimeConfiguration Configuration() =>
        new()
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Mentor = new MentorConfiguration
            {
                Mode = MentorOperationMode.Active,
                EconomyMode = MentorEconomyMode.PerRecipient,
                SpellSourcePolicy = MentorSpellSourcePolicy.EquippedSpells,
                SpellSharePercent = 10,
            },
        };

    private static GameWorldState World() =>
        new()
        {
            CollectedAtEpoch = 1,
            Views = Table(
                new WorldView(KnownEntities.MasteriesEnabled.Uuid, false, false, true),
                new WorldView(KnownEntities.MagicSpellbook.Uuid, false, false, true)),
            SpellRecipes = Table(Spell(Source, 5), Spell(Recipient, 1)),
            SpellSlots = Table(new WorldSpellSlot(
                0, Source, true, false, false, false, false, false, false, true, true,
                true, 1, 1, default)),
            MasteryExperience = Table(new WorldMasteryExperience(
                1, MasteryExperienceDomain.Spell, Source, 5, true, new BigDouble(10))),
        };

    private static WorldSpellRecipe Spell(Guid id, int mastery) =>
        new(id, true, 0, default, mastery, false, false, false, false, 0, 0, 0, false,
            default, default, default, default, default, default, false);

    private static PublicationTable<T> Table<T>(params T[] rows) where T : struct =>
        PublicationTable<T>.Create(rows, rows.Length);

    private sealed class ActionPort : IMentorCycleActionPort
    {
        private readonly int _ownerThread;

        internal ActionPort(int ownerThread) => _ownerThread = ownerThread;
        internal int Calls { get; private set; }
        internal Guid Recipient { get; private set; }

        public ServiceActionResult TryExecute(
            in MentorCycleAction action,
            in SuiteRuntimeConfiguration config,
            in ServiceActionContext context)
        {
            Assert.Equal(_ownerThread, Thread.CurrentThread.ManagedThreadId);
            Calls++;
            Recipient = action.RecipientId;
            return ServiceActionResult.Committed(
                CommonActionResultCodes.Committed,
                ServiceNativeMutationEvidence.Observed(
                    NativeMutationOutcome.Verified,
                    new NativeMutationCallOutcome(1, 1, 1)));
        }
    }
}

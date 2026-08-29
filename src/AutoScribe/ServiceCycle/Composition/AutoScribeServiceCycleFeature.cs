using System;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;

namespace OrbAutomata;

internal sealed class AutoScribeServiceCycleFeature : IAutomataServiceCycleFeature
{
    private static readonly ServiceActionDispatchPolicy Dispatch =
        ServiceActionDispatchPolicy.Bounded(1);
    private readonly AutoScribeFeatureDependencies _dependencies;

    internal AutoScribeServiceCycleFeature(AutoScribeFeatureDependencies dependencies) =>
        _dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context)
    {
        var adapters = AutoScribeServiceAdapterComposition.Create(_dependencies);
        var registration = context.Registry.Register(adapters.Definition, Dispatch);
        return new Runtime(
            _dependencies,
            adapters.GameAction,
            adapters.Health,
            registration,
            context.LifecycleValue,
            context.ConfigurationGeneration);
    }

    internal sealed class Runtime : IAutomataServiceCycleFeatureRuntime
    {
        private readonly AutoScribeFeatureDependencies _dependencies;
        private readonly AutoScribeOneShotCraftGameAction _gameAction;
        private readonly AutoScribeActionHealth _health;
        private readonly ServiceRegistration<AutoScribeCycleState, AutoScribeCycleAction> _registration;
        private readonly long _lifecycle;
        private readonly ConfigGeneration _configuration;
        private AutoScribeServiceCycleDiagnosticsBridge? _diagnostics;

        internal Runtime(
            AutoScribeFeatureDependencies dependencies,
            AutoScribeOneShotCraftGameAction gameAction,
            AutoScribeActionHealth health,
            ServiceRegistration<AutoScribeCycleState, AutoScribeCycleAction> registration,
            long lifecycle,
            ConfigGeneration configuration)
        {
            _dependencies = dependencies;
            _gameAction = gameAction;
            _health = health;
            _registration = registration;
            _lifecycle = lifecycle;
            _configuration = configuration;
        }

        public void ActivateDiagnostics() =>
            _diagnostics = new AutoScribeServiceCycleDiagnosticsBridge(
                _dependencies,
                _gameAction,
                _health,
                _lifecycle,
                _configuration);

        public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) =>
            _diagnostics?.Observe(pump, in report);

        public void ObserveConfiguration(ConfigGeneration configurationGeneration) =>
            _diagnostics?.ObserveConfiguration(configurationGeneration);

        public void ObserveLifecycle(
            long nativeLifecycle,
            ConfigGeneration configurationGeneration)
        {
            _gameAction.InvalidateLifecycle();
            _health.InvalidateLifecycle();
            _diagnostics?.ObserveLifecycle(nativeLifecycle, configurationGeneration);
        }

        public void DisposeDiagnostics() => _diagnostics = null;

        internal CraftingPlayerSubmission TryExecuteGameMcp(in CraftingPlayerAction action) =>
            _gameAction.Submit(in action);

        internal bool PlayerCraftingBindingsAvailable =>
            _gameAction.PlayerCraftingBindingsAvailable;

        internal string PlayerCraftingBindingFailure =>
            _gameAction.PlayerCraftingBindingFailure;

        public void DisposeRegistration()
        {
            _registration.Dispose();
            _gameAction.Dispose();
        }
    }
}

internal sealed class AutoScribeUnavailableServiceCycleFeature : IAutomataServiceCycleFeature
{
    private readonly AutomataFeatureStatusReporter _featureStatus;

    internal AutoScribeUnavailableServiceCycleFeature(
        AutomataFeatureStatusReporter featureStatus) =>
        _featureStatus = featureStatus ??
            throw new ArgumentNullException(nameof(featureStatus));

    public IAutomataServiceCycleFeatureRuntime Register(
        in AutomataServiceCycleFeatureContext context) =>
        new Runtime(
            _featureStatus,
            context.LifecycleValue,
            context.ConfigurationGeneration);

    private sealed class Runtime : IAutomataServiceCycleFeatureRuntime
    {
        private readonly AutomataFeatureStatusReporter _status;
        private readonly long _lifecycle;
        private readonly ConfigGeneration _configuration;

        internal Runtime(
            AutomataFeatureStatusReporter status,
            long lifecycle,
            ConfigGeneration configuration)
        {
            _status = status;
            _lifecycle = lifecycle;
            _configuration = configuration;
        }

        public void ActivateDiagnostics() =>
            _status.ObserveRuntimeLifecycle(
                FeatureStatusState.ContractUnavailable,
                FeatureStatusReasonCode.IdentityMismatch,
                "No audited Auto Scribe identity profile exists for this game baseline.",
                _lifecycle,
                _configuration);

        public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report) { }
        public void ObserveConfiguration(ConfigGeneration configurationGeneration) { }
        public void ObserveLifecycle(long nativeLifecycle, ConfigGeneration configurationGeneration) { }
        public void DisposeDiagnostics() { }
        public void DisposeRegistration() { }
    }
}

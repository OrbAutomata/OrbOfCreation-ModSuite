using System;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.ServiceCycle.Observation.WorldCollection;
using OrbModding.Common.Runtime.ServiceCycle.Orchestration;
using OrbModding.Common.Runtime.ServiceCycle.Registration;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// World collection's contribution to the Automata ServiceCycle host.
/// </summary>
/// <remarks>
/// The simplest feature in the suite, and deliberately so: no native gates, no action family, and
/// no diagnostics bridge. It reads the game and publishes; there is nothing it can do wrong to a
/// save, so there is nothing for those layers to guard.
/// </remarks>
internal sealed class AutomataWorldCollectionFeature : IAutomataServiceCycleFeature
{
    private readonly Func<long> _readFrameIdentity;
    private readonly Func<long> _readLifecycleEpoch;
    private readonly Func<GameWorldCollector> _createCollector;
    private readonly Action<WorldCollectionReport>? _announce;
    private readonly WorldCollectionSpanRegistry _spans;

    /// <param name="readFrameIdentity">
    /// The same counter the host pumps with, so a snapshot's generation and a consumer's last-action
    /// frame are the same units and can simply be compared.
    /// </param>
    /// <param name="readLifecycleEpoch">
    /// The same counter the host replaces its lifecycle on, so a snapshot's epoch and a service's
    /// pinned lifecycle are the same units too.
    /// </param>
    internal AutomataWorldCollectionFeature(
        Func<long> readFrameIdentity,
        Func<long> readLifecycleEpoch,
        Action<WorldCollectionReport>? announce = null,
        Func<GameWorldCollector>? createCollector = null,
        WorldCollectionSpanRegistry? spans = null)
    {
        _readFrameIdentity = readFrameIdentity ?? throw new ArgumentNullException(nameof(readFrameIdentity));
        _readLifecycleEpoch = readLifecycleEpoch ?? throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _announce = announce;
        _createCollector = createCollector ?? (static () => GameWorldCollector.ForSession());
        _spans = spans ?? WorldCollectionSpanRegistry.Shared;
    }

    public IAutomataServiceCycleFeatureRuntime Register(in AutomataServiceCycleFeatureContext context)
    {
        // Constructed here rather than at plugin startup because binding compiles an accessor per
        // member per category against the loaded game assembly, which is only meaningful once the
        // runtime is being stood up for a playable lifecycle.
        var collector = _createCollector();
        // Said before anything records, because the session roster is written when a recording starts
        // and a span's identity is a number until something says what it was.
        _spans.PublishCategories(collector.CategoryNames());
        var definition = AutomataWorldCollectionService.Define(
            new AutomataWorldCapturePort(
                collector, _readFrameIdentity, _readLifecycleEpoch, _announce, _spans),
            context.Registry.WorldPublication);

        // No dispatch policy here: registering through the source path is the declaration, and one
        // publish per frame ahead of every mutating service is what that path means.
        return new AutomataWorldCollectionFeatureRuntime(
            context.Registry.RegisterSource(definition));
    }

}

/// <summary>The per-frame runtime for collection: a registration handle and nothing else.</summary>
internal sealed class AutomataWorldCollectionFeatureRuntime : IAutomataServiceCycleFeatureRuntime
{
    private readonly ServiceRegistration<
        AutomataWorldCollectionState,
        AutomataWorldCollectionAction> _registration;

    internal AutomataWorldCollectionFeatureRuntime(
        ServiceRegistration<
            AutomataWorldCollectionState,
            AutomataWorldCollectionAction> registration) =>
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));

    public void ActivateDiagnostics()
    {
    }

    public void ObserveFrame(SuiteFramePump pump, in SuiteFramePumpReport report)
    {
    }

    public void ObserveConfiguration(ConfigGeneration configurationGeneration)
    {
    }

    /// <summary>
    /// Nothing to invalidate. Collection holds no native handles between cycles — every accessor is
    /// resolved against a type, not an instance, and every instance is re-read from its registry each
    /// pass, so a save load simply yields a different set of entities next cycle.
    /// </summary>
    public void ObserveLifecycle(
        long nativeLifecycle,
        ConfigGeneration configurationGeneration)
    {
    }

    public void DisposeDiagnostics()
    {
    }

    public void DisposeRegistration() => _registration.Dispose();
}

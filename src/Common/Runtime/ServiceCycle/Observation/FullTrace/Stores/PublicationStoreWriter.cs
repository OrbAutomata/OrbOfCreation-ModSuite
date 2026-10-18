using System;
using System.Collections.Generic;
using System.Globalization;
using OrbModding.Common.Runtime.Tracing;

namespace OrbModding.Common.Runtime.ServiceCycle.Observation.FullTrace.Stores;

/// <summary>
/// The generation-keyed stores a recording session keeps beside its semantic segments.
/// </summary>
/// <remarks>
/// <para>
/// A decision event names the generations it acted on; a store says what those generations were. One
/// file per generation, written the first time the session sees it, so three services deciding on one
/// configuration cost one payload rather than three — the deduplication the artifact is built around.
/// </para>
/// <para>
/// The world store is not here. The world republishes up to four times a second and its payload is the whole
/// raw reading of the game, so storing one per generation is a different problem in kind from storing
/// one per settings save; it is deliberately left for its own decision rather than approximated here.
/// </para>
/// </remarks>
internal sealed class PublicationStoreWriter
{
    internal const string ConfigurationStore = "configuration";
    internal const string StrategyStore = "strategy";
    internal const string Extension = ".oscv";

    private readonly ISessionSideArtifactSink _sink;
    private readonly HashSet<ulong> _configurations = new();
    private readonly HashSet<ulong> _strategies = new();
    private bool _faulted;

    internal PublicationStoreWriter(ISessionSideArtifactSink sink) =>
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));

    internal int ConfigurationCount => _configurations.Count;
    internal int StrategyCount => _strategies.Count;

    /// <summary>
    /// True once a store write has failed. The session keeps recording: a missing side artifact costs
    /// a reader the settings behind a generation, and losing the events as well would cost them more.
    /// It does stop writing stores, and reports this in its snapshot, so the artifact is described as
    /// one whose generations cannot all be resolved rather than as a whole one.
    /// </summary>
    internal bool IsFaulted => _faulted;

    internal void ObserveConfiguration(ulong generation, object publication) =>
        Observe(_configurations, ConfigurationStore, generation, publication);

    internal void ObserveStrategy(ulong generation, object publication) =>
        Observe(_strategies, StrategyStore, generation, publication);

    internal static string FileName(string store, ulong generation) =>
        store + "-" + generation.ToString("x16", CultureInfo.InvariantCulture) + Extension;

    private void Observe(HashSet<ulong> written, string store, ulong generation, object publication)
    {
        if (_faulted || generation == 0 || publication is null || written.Contains(generation)) return;
        try
        {
            _sink.CommitSideArtifact(
                FileName(store, generation),
                PublicationValueFormat.Encode(store, generation, publication));
            // Recorded after the commit, not before it: the counts say how many stores a reader will
            // find, and a generation whose write threw left no file to find.
            written.Add(generation);
        }
        catch (Exception exception) when (!ExceptionDetail.IsProcessFatal(exception))
        {
            _faulted = true;
        }
    }
}

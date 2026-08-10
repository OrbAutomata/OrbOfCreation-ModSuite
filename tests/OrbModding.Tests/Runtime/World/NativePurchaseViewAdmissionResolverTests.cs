using System;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// What the owning-view admission resolver will and will not accept as evidence.
/// </summary>
/// <remarks>
/// The published snapshot is the only thing standing between a planned purchase and a native
/// payment, and it is process-wide. Every case here is about what may replace it: an epoch it can
/// never be asked for cannot, and a read that failed on the way must not take the last good one
/// down with it.
/// </remarks>
public sealed class NativePurchaseViewAdmissionResolverTests : IDisposable
{
    private const long Lifecycle = 41;

    public NativePurchaseViewAdmissionResolverTests() => ClearRegistries();

    public void Dispose() => ClearRegistries();

    [Fact]
    public void AnEpochlessReadDoesNotReplaceTheSnapshotALifecyclePublished()
    {
        var structure = Author();
        var resolver = Resolver();
        ReadAll(resolver, Lifecycle);
        Assert.True(resolver.TryGetCaptured(
            WorldPurchaseCandidateKind.Structure, structure, Lifecycle, out _));

        ReadAll(resolver, 0);

        Assert.Equal(Lifecycle, resolver.CapturedEpoch);
        Assert.True(resolver.TryGetCaptured(
            WorldPurchaseCandidateKind.Structure, structure, Lifecycle, out _));
    }

    /// <summary>
    /// The rows themselves are authored facts and are still worth collecting; only the publication
    /// is refused, so the caller's buffers fill either way.
    /// </summary>
    [Fact]
    public void AnEpochlessReadStillFillsTheCallersBuffers()
    {
        Author();
        var relations = new WorldRelationBuffer<WorldPurchaseViewRelation>();
        var routes = new WorldRelationBuffer<WorldPurchaseViewRoute>();

        var sampled = Resolver().ReadAll(0, relations, routes, out var unresolved, out var skipped);

        Assert.Equal(1, sampled);
        Assert.Equal(1, relations.Count);
        Assert.Equal(1, routes.Count);
        Assert.Equal(0, unresolved);
        Assert.Equal(0, skipped);
    }

    /// <summary>
    /// Nothing may be admitted under an epoch nobody named, which is why publishing one would be a
    /// snapshot no candidate could ever be found in.
    /// </summary>
    [Fact]
    public void NoCandidateIsAdmittedWithoutALifecycle()
    {
        var structure = Author();
        var resolver = Resolver();
        ReadAll(resolver, Lifecycle);

        Assert.False(resolver.TryGetCaptured(
            WorldPurchaseCandidateKind.Structure, structure, 0, out _));
    }

    private static NativePurchaseViewAdmissionResolver Resolver()
    {
        Assert.True(
            NativePurchaseViewAdmissionResolver.TryCreate(
                WorldNativeTypes.Resolve,
                out var resolver,
                out var failure),
            failure);
        return resolver!;
    }

    private static void ReadAll(NativePurchaseViewAdmissionResolver resolver, long lifecycleEpoch) =>
        resolver.ReadAll(
            lifecycleEpoch,
            new WorldRelationBuffer<WorldPurchaseViewRelation>(),
            new WorldRelationBuffer<WorldPurchaseViewRoute>(),
            out _,
            out _);

    private static Guid Author()
    {
        var structure = new global::StructureSO
        {
            uuid = Guid.NewGuid().ToString(),
            available = true,
            purchasable = true,
        };
        global::StructureSO.All.Add(structure);
        var owningView = new global::ViewSO { available = true };
        owningView.relevantLists.Add(
            new global::StructureListVariable { value = global::StructureSO.All });
        global::ViewSO.All.Add(owningView);
        return structure.GetGuid();
    }

    private static void ClearRegistries()
    {
        global::StructureSO.All.Clear();
        global::UpgradeSO.All.Clear();
        global::ViewSO.All.Clear();
    }
}

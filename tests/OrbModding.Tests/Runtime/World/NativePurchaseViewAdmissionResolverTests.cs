using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
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
        var memberships = new WorldRelationBuffer<WorldUpgradeListMembership>();

        var sampled = Resolver().ReadAll(
            0, relations, routes, memberships, out var unresolved, out var skipped, out _);

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
            new WorldRelationBuffer<WorldUpgradeListMembership>(),
            out _,
            out _,
            out _);

    /// <summary>
    /// The Scholar screen's list is named by no view anywhere in the game, so nothing the view walk
    /// can reach ever mentions its twenty-seven upgrades. Reading it through the identity it carries
    /// is the whole difference between the column saying <c>scholar</c> and saying nothing.
    /// </summary>
    [Fact]
    public void AnUpgradeListNoViewNamesIsStillReadThroughTheIdentityItCarries()
    {
        var upgrade = AuthorUpgrade();
        AuthorPinnedUpgradeLists(upgrade);
        var relations = new WorldRelationBuffer<WorldPurchaseViewRelation>();
        var routes = new WorldRelationBuffer<WorldPurchaseViewRoute>();
        var memberships = new WorldRelationBuffer<WorldUpgradeListMembership>();

        Resolver().ReadAll(
            Lifecycle, relations, routes, memberships, out _, out _, out var failure);

        Assert.Equal(string.Empty, failure);
        Assert.Equal(
            new[] { KnownEntities.UpgradesAll.Uuid, KnownEntities.UpgradesScholarScreen.Uuid }
                .OrderBy(id => id)
                .ToArray(),
            Rows(memberships)
                .Where(row => row.UpgradeId == upgrade.GetGuid())
                .Select(row => row.ListId)
                .OrderBy(id => id)
                .ToArray());
    }

    /// <summary>
    /// A pinned list that no longer answers to its identity withholds the whole table rather than
    /// publishing the part that read: a row absent from a partial table is indistinguishable from a
    /// row on no screen, and the column would word the second one as fact.
    /// </summary>
    [Fact]
    public void AnUnresolvablePinnedListWithholdsEveryMembershipAndNamesWhy()
    {
        var upgrade = AuthorUpgrade();
        AuthorPinnedUpgradeLists(upgrade);
        global::IdScriptableObject.RuntimeLookup.Remove(KnownEntities.UpgradesTimeScreen.Uuid);
        var memberships = new WorldRelationBuffer<WorldUpgradeListMembership>();

        Resolver().ReadAll(
            Lifecycle,
            new WorldRelationBuffer<WorldPurchaseViewRelation>(),
            new WorldRelationBuffer<WorldPurchaseViewRoute>(),
            memberships,
            out _,
            out _,
            out var failure);

        Assert.Equal(0, memberships.Count);
        Assert.Contains(
            KnownEntities.UpgradesTimeScreen.Uuid.ToString("D"), failure, StringComparison.Ordinal);
    }

    private static IReadOnlyList<WorldUpgradeListMembership> Rows(
        WorldRelationBuffer<WorldUpgradeListMembership> buffer)
    {
        var rows = new List<WorldUpgradeListMembership>(buffer.Count);
        for (var index = 0; index < buffer.Count; index++) rows.Add(buffer[index]);
        return rows;
    }

    private static global::UpgradeSO AuthorUpgrade()
    {
        var upgrade = new global::UpgradeSO { uuid = Guid.NewGuid().ToString() };
        global::UpgradeSO.All.Add(upgrade);
        return upgrade;
    }

    /// <summary>
    /// All nine authored panels, of which only the catch-all is on a view — exactly the shape the
    /// pinned build has, where the Scholar, aspect and Time lists are prefab-only.
    /// </summary>
    private static void AuthorPinnedUpgradeLists(global::UpgradeSO scholarly)
    {
        foreach (var listId in WorldUpgradeScreenLists.All)
        {
            var list = new global::UpgradeListVariable();
            list.SetGuid(listId);
            global::IdScriptableObject.RuntimeLookup[listId] = list;
            if (listId == KnownEntities.UpgradesAll.Uuid)
            {
                list.value.Add(scholarly);
                var view = new global::ViewSO { available = true };
                view.relevantLists.Add(list);
                global::ViewSO.All.Add(view);
            }
            else if (listId == KnownEntities.UpgradesScholarScreen.Uuid)
            {
                list.value.Add(scholarly);
            }
        }
    }

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
        global::IdScriptableObject.RuntimeLookup.Clear();
    }
}

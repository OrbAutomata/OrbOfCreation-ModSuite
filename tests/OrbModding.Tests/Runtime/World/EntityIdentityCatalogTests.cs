using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests.Runtime.World;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EntityIdentityCatalogCollection
{
    public const string Name = "Entity identity catalog registry isolation";
}

[Collection(EntityIdentityCatalogCollection.Name)]
public sealed class EntityIdentityCatalogTests : IDisposable
{
    private readonly Dictionary<Guid, IdScriptableObject> _originalRegistry =
        IdScriptableObject.RuntimeLookup;

    public EntityIdentityCatalogTests() =>
        IdScriptableObject.RuntimeLookup = new Dictionary<Guid, IdScriptableObject>();

    public void Dispose() => IdScriptableObject.RuntimeLookup = _originalRegistry;

    [Fact]
    public void SharedBindingOwnsTheExactRegistryAndStableIdentityMembers()
    {
        var uuid = Guid.NewGuid();
        var entity = new NamedEntity("Visible", "Asset");
        entity.SetGuid(uuid);
        IdScriptableObject.RuntimeLookup[uuid] = entity;
        try
        {
            var binding = Binding();

            var source = binding.Read();

            Assert.True(source.IsReady);
            Assert.Same(IdScriptableObject.RuntimeLookup, source.Registry);
            Assert.Equal(uuid, binding.ReadStableUuid(entity));
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(uuid);
        }
    }

    [Fact]
    public void FirstStablePlayingCapturePublishesOneSortedSnapshotAndReusesIt()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000002");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Register(first, new NamedEntity("Second", "AssetSecond"));
        Register(second, new NamedEntity("First", "AssetFirst"));
        EntityIdentityCatalogSnapshot? published = null;
        try
        {
            var catalog = Catalog(7, publish: value => published = value);
            catalog.Reset(7);

            var captured = catalog.Capture(7);
            var repeated = catalog.Capture(7);

            Assert.True(captured.IsBound);
            Assert.Equal(2, captured.Rows.Count);
            Assert.Equal(second, captured.Rows[0].EntityId);
            Assert.Equal(first, captured.Rows[1].EntityId);
            Assert.Equal("First", captured.Rows[0].DisplayName);
            Assert.Equal("AssetFirst", captured.Rows[0].AssetName);
            Assert.Equal(typeof(NamedEntity).FullName, captured.Rows[0].RuntimeType);
            Assert.Same(captured, repeated);
            Assert.Same(captured, published);

            var frame = new GameWorldCycleFrame { EntityIdentities = captured };
            var world = GameWorldFrameDeriver.Build(frame);
            Assert.Same(captured, world.EntityIdentities);
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(first);
            IdScriptableObject.RuntimeLookup.Remove(second);
        }
    }

    [Fact]
    public void LifecycleOrRegistryInstabilityDiscardsTheWholeCandidateAndRetries()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        Register(first, new NamedEntity("First", "AssetFirst"));
        var mutate = true;
        try
        {
            var catalog = Catalog(
                8,
                displayName: value =>
                {
                    if (mutate)
                    {
                        mutate = false;
                        Register(second, new NamedEntity("Second", "AssetSecond"));
                    }
                    return ((NamedEntity)value).GetName();
                });
            catalog.Reset(8);

            var discarded = catalog.Capture(8);
            var retried = catalog.Capture(8);

            Assert.Equal(EntityIdentityCatalogState.Unbound, discarded.State);
            Assert.True(retried.IsBound);
            Assert.Equal(2, retried.Rows.Count);
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(first);
            IdScriptableObject.RuntimeLookup.Remove(second);
        }
    }

    [Fact]
    public void GenerationChangeDuringPassDiscardsCandidateWithoutPublishingPartialRows()
    {
        var uuid = Guid.NewGuid();
        Register(uuid, new NamedEntity("Entity", "Asset"));
        var lifecycleReads = 0;
        try
        {
            var catalog = new EntityIdentityCatalog(
                Binding(),
                () => Playing(lifecycleReads++ == 0 ? 9 : 10),
                () => 4,
                publish: static _ => { },
                resetFormatter: static _ => { });
            catalog.Reset(9);

            var result = catalog.Capture(9);

            Assert.Equal(EntityIdentityCatalogState.Unbound, result.State);
            Assert.Equal(0, result.Rows.Count);
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(uuid);
        }
    }

    [Fact]
    public void PerEntityNameFailureKeepsIdentityAndIndependentAssetFallback()
    {
        var uuid = Guid.NewGuid();
        var entity = new NamedEntity("Display", "Asset");
        Register(uuid, entity);
        try
        {
            var catalog = Catalog(
                10,
                displayName: _ => throw new InvalidOperationException("localization failed"));
            catalog.Reset(10);

            var result = catalog.Capture(10);

            var row = Assert.Single(result.Rows.AsSpan().ToArray());
            Assert.Equal(uuid, row.EntityId);
            Assert.Equal(string.Empty, row.DisplayName);
            Assert.Equal("Asset", row.AssetName);
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(uuid);
        }
    }

    [Fact]
    public void IdentityContradictionPublishesEmptyFailureOnceAndNeverBlocksCaller()
    {
        var key = Guid.NewGuid();
        var entity = new NamedEntity("Display", "Asset");
        entity.SetGuid(Guid.NewGuid());
        IdScriptableObject.RuntimeLookup[key] = entity;
        var errors = new List<string>();
        try
        {
            var catalog = Catalog(11, errors.Add);
            catalog.Reset(11);

            var failed = catalog.Capture(11);
            var repeated = catalog.Capture(11);

            Assert.Equal(EntityIdentityCatalogState.ContractUnavailable, failed.State);
            Assert.Equal(0, failed.Rows.Count);
            Assert.Same(failed, repeated);
            Assert.Single(errors);
            Assert.Contains(key.ToString("D"), failed.FailureReason);
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(key);
        }
    }

    [Fact]
    public void ResetImmediatelyDropsThePreviousLifecycleReference()
    {
        var uuid = Guid.NewGuid();
        Register(uuid, new NamedEntity("Display", "Asset"));
        EntityIdentityCatalogSnapshot? published = null;
        try
        {
            var catalog = Catalog(12, publish: value => published = value);
            catalog.Reset(12);
            var old = catalog.Capture(12);

            catalog.Reset(13);

            Assert.NotSame(old, catalog.Current);
            Assert.Same(catalog.Current, published);
            Assert.Equal(13, catalog.Current.Generation);
            Assert.Equal(EntityIdentityCatalogState.Unbound, catalog.Current.State);
            Assert.Equal(0, catalog.Current.Rows.Count);
        }
        finally
        {
            IdScriptableObject.RuntimeLookup.Remove(uuid);
        }
    }

    private static RuntimeIdentityRegistryBinding Binding() =>
        new(() => typeof(IdScriptableObject));

    private static EntityIdentityCatalog Catalog(
        long generation,
        Action<string>? error = null,
        Func<object, string>? displayName = null,
        Action<EntityIdentityCatalogSnapshot>? publish = null) =>
        new(
            Binding(),
            () => Playing(generation),
            () => 4,
            displayName,
            value => ((UnityEngine.Object)value).name,
            error,
            publish ?? (static _ => { }),
            static _ => { });

    private static GameLifecycleSnapshot Playing(long generation) =>
        new(
            GameLifecycleState.Playing,
            generation,
            "Main",
            GameLifecycleTransitionKind.RuntimeReady,
            100);

    private static void Register(Guid uuid, NamedEntity entity)
    {
        entity.SetGuid(uuid);
        IdScriptableObject.RuntimeLookup[uuid] = entity;
    }

    private sealed class NamedEntity : TooltipableObject
    {
        internal NamedEntity(string display, string asset)
        {
            displayName = display;
            name = asset;
        }

        public override string GetName() => displayName;
    }
}

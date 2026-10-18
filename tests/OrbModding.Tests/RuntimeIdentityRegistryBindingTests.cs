using System;
using System.Collections.Generic;
using System.Threading;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class RuntimeIdentityRegistryBindingTests
{
    /// <summary>
    /// GetGuid is an instance method, so reading one object's stable identity needs the method and
    /// not the registry. A registry the game has not filled yet must not make an artifact the
    /// loadout already holds look like it has lost its GetGuid.
    /// </summary>
    [Fact]
    public void AStableIdentityReadsBeforeTheRegistryIsReady()
    {
        var registry = IdScriptableObject.RuntimeLookup;
        try
        {
            IdScriptableObject.RuntimeLookup = null!;
            var binding = new RuntimeIdentityRegistryBinding(() => typeof(IdScriptableObject));
            var view = new ViewSO();

            Assert.Equal(view.uuid, binding.ReadStableUuid(view));
        }
        finally
        {
            IdScriptableObject.RuntimeLookup = registry;
        }
    }

    /// <summary>
    /// When the identity type cannot be bound, the refusal names why, so the loadout that asked
    /// reports the audited reason and not a GetGuid that was never looked for.
    /// </summary>
    [Fact]
    public void AnUnboundIdentityTypeSaysWhyItCannotRead()
    {
        var binding = new RuntimeIdentityRegistryBinding(() => null);

        var refusal = Assert.Throws<InvalidOperationException>(() => binding.ReadStableUuid(new ViewSO()));

        Assert.Equal("native IdScriptableObject type is not loaded yet", refusal.Message);
    }

    [Fact]
    public void AnEntityAnswersThroughTheBoundIdentityContract()
    {
        var binding = new RuntimeIdentityRegistryBinding(() => typeof(IdScriptableObject));
        var structure = new StructureSO();

        var method = binding.StableUuidMethod(typeof(StructureSO));

        Assert.Equal(typeof(IdScriptableObject), method?.DeclaringType);
        Assert.Equal(structure.GetGuid(), method?.Invoke(structure, null));
    }

    /// <summary>
    /// A few plain game objects, such as a consumable usage or a player loadout, carry an identity
    /// without being an IdScriptableObject; they declare their own GetGuid.
    /// </summary>
    [Fact]
    public void APlainObjectAnswersThroughItsOwnGetGuid()
    {
        var binding = new RuntimeIdentityRegistryBinding(() => typeof(IdScriptableObject));
        var usage = new PlainIdentity();

        var method = binding.StableUuidMethod(typeof(PlainIdentity));

        Assert.Equal(usage.Id, method?.Invoke(usage, null));
        Assert.Null(binding.StableUuidMethod(typeof(object)));
    }

    /// <summary>
    /// The first identity bind can happen on two threads at once, as when parallel tests or a
    /// binder and a world read meet the shared binding. A thread arriving mid-bind either finishes
    /// the bind itself or sees it whole; it never sees GetGuid bound without its compiled read.
    /// </summary>
    [Fact]
    public void AConcurrentFirstBindNeverAnswersAHalfBoundIdentity()
    {
        var halfBound = 0;
        for (var attempt = 0; attempt < 2000; attempt++)
        {
            var binding = new RuntimeIdentityRegistryBinding();
            using var start = new Barrier(2);
            Func<object, Guid>? read = null;
            var binder = new Thread(() =>
            {
                start.SignalAndWait();
                binding.Read();
            });
            var reader = new Thread(() =>
            {
                start.SignalAndWait();
                read = binding.StableUuid(typeof(PlotNodeSO));
            });
            binder.Start();
            reader.Start();
            binder.Join();
            reader.Join();
            if (read is null) halfBound++;
        }

        Assert.Equal(0, halfBound);
    }

    [Fact]
    public void AnEntityHasNoIdentityWhileTheContractIsUnavailable()
    {
        Assert.Null(new RuntimeIdentityRegistryBinding(() => null).StableUuidMethod(typeof(StructureSO)));
        Assert.Null(new RuntimeIdentityRegistryBinding(() => typeof(MismatchedIdentity))
            .StableUuidMethod(typeof(MismatchedEntity)));
    }

    private sealed class PlainIdentity
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid GetGuid() => Id;
    }

    private class MismatchedIdentity
    {
        public static Dictionary<Guid, object> RuntimeLookup = new();
        public Guid GetGuid() => Guid.NewGuid();
    }

    private sealed class MismatchedEntity : MismatchedIdentity
    {
    }
}

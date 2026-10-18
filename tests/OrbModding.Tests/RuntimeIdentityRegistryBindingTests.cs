using System;
using System.Collections.Generic;
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
}

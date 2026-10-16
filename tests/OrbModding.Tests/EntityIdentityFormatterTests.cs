using System;
using System.Collections.Generic;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;
using Xunit;

namespace OrbModding.Tests;

public sealed class EntityIdentityFormatterTests : IDisposable
{
    public EntityIdentityFormatterTests() =>
        EntityIdentityFormatter.ConfigureDiagnostics(static _ => { }, static _ => { });

    public void Dispose() =>
        EntityIdentityFormatter.ConfigureDiagnostics(static _ => { }, static _ => { });

    [Fact]
    public void LiveDisplayThenAssetThenBootstrapThenBareUuidIsTheExactLadder()
    {
        var displayId = Guid.NewGuid();
        var assetId = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var live = Snapshot(
            new EntityIdentityName(displayId, "ResearchSO", "Visible", "Asset"),
            new EntityIdentityName(assetId, "ResourceSO", string.Empty, "ResourceAsset"));

        Assert.Equal(
            $"Visible [Asset] ({displayId:D})",
            EntityIdentityFormatter.Format(displayId, live));
        Assert.Equal(
            $"ResourceAsset ({assetId:D})",
            EntityIdentityFormatter.Format(assetId, live));
        Assert.Equal(
            $"BulkDevelopment ({KnownEntities.BulkDevelopment.Uuid:D})",
            EntityIdentityFormatter.Format(
                KnownEntities.BulkDevelopment.Uuid,
                EntityIdentityCatalogSnapshot.Unbound(4)));
        Assert.Equal(
            unknown.ToString("D"),
            EntityIdentityFormatter.Format(
                unknown,
                EntityIdentityCatalogSnapshot.Unbound(4)));
    }

    [Fact]
    public void EqualLiveNamesAreNotRepeatedAndKnownBootstrapIsPreBindOnly()
    {
        var known = KnownEntities.BulkDevelopment.Uuid;
        var live = Snapshot(new EntityIdentityName(
            known, "IntVariable", "Bulk Development", "Bulk Development"));

        Assert.Equal(
            $"Bulk Development ({known:D})",
            EntityIdentityFormatter.Format(known, live));

        var boundMiss = Snapshot();
        Assert.Equal(known.ToString("D"), EntityIdentityFormatter.Format(known, boundMiss));
    }

    [Fact]
    public void PostBindMissWarnsOncePerGenerationAndWarningIsUuidOnly()
    {
        var uuid = Guid.NewGuid();
        var warnings = new List<string>();
        EntityIdentityFormatter.ConfigureDiagnostics(warnings.Add, static _ => { });
        EntityIdentityFormatter.Reset(22);
        var snapshot = EntityIdentityCatalogSnapshot.Bound(
            22,
            Array.Empty<EntityIdentityName>());

        EntityIdentityFormatter.Format(uuid, snapshot);
        EntityIdentityFormatter.Format(uuid, snapshot);

        var warning = Assert.Single(warnings);
        Assert.Contains(uuid.ToString("D"), warning);
        Assert.DoesNotContain("(", warning);
        Assert.DoesNotContain("catalog has no label for UUID Live", warning);
    }

    [Fact]
    public void ContractFailureAndMalformedInputsAlwaysReturnCanonicalUuid()
    {
        var uuid = Guid.NewGuid();
        var failed = EntityIdentityCatalogSnapshot.ContractUnavailable(30, "missing");

        var rendered = Record.Exception(
            () => EntityIdentityFormatter.Format(uuid, failed));

        Assert.Null(rendered);
        Assert.Equal(uuid.ToString("D"), EntityIdentityFormatter.Format(uuid, failed));
    }

    [Fact]
    public void BootstrapCoversEveryGeneratedKnownEntityExactlyOnce()
    {
        Assert.Equal(62, KnownEntityBootstrap.Count);
        Assert.True(KnownEntityBootstrap.TryGet(
            KnownEntities.ActiveActionables.Uuid,
            out var first));
        Assert.Equal(KnownEntities.ActiveActionables.DiagnosticName, first);
        Assert.True(KnownEntityBootstrap.TryGet(
            KnownEntities.WorldAspectSlots.Uuid,
            out var last));
        Assert.Equal(KnownEntities.WorldAspectSlots.DiagnosticName, last);
    }

    private static EntityIdentityCatalogSnapshot Snapshot(
        params EntityIdentityName[] rows)
    {
        Array.Sort(rows, static (left, right) => left.EntityId.CompareTo(right.EntityId));
        return EntityIdentityCatalogSnapshot.Bound(22, rows);
    }
}

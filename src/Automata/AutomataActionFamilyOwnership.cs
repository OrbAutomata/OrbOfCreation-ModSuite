using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
#endif

namespace OrbAutomata;

internal sealed class AutomataActionFamilyOwnership : IDisposable
{
    internal const string KnownAutoBuyPluginGuid = "IngoH.OrbOfCreation.AutoBuyOrb";

    private static readonly AutomationActionFamily[] KnownExternalFamilies =
        { AutomationActionFamily.StructurePurchase, AutomationActionFamily.NativeMultiBuyOverride };

    private readonly ActionFamilyOwnershipRegistry _registry;
    private readonly Dictionary<AutomationActionFamily, DemandSlot> _claims;
#if SERVICE_CYCLE_PROFILE
    private ActionFamilyLeaseSet? _gameMcpOperationLease;
    private AutomationActionFamily[] _gameMcpOperationFamilies =
        Array.Empty<AutomationActionFamily>();
#endif
    private IDisposable? _knownExternal;
    private int _pluginInventoryCount = -1;
    internal int ClaimAttempts { get; private set; }
    public bool KnownAutoBuyLoaded { get; private set; }

    public AutomataActionFamilyOwnership(ActionFamilyOwnershipRegistry? registry = null)
    {
        _registry = registry ?? ActionFamilyOwnershipRegistry.Shared;
        _claims = AutomataFeatureCatalog.All.SelectMany(feature => feature.OwnershipDemands)
            .GroupBy(demand => demand.Family)
            .ToDictionary(group => group.Key, group => new DemandSlot(group.ToArray()));
    }

    internal GameActionFamilyHandle Family(GameActionFamily family) => new(this, family);

#if SERVICE_CYCLE_PROFILE
    internal GameActionFamilyHandle Family(GameMcpCommandKind kind)
    {
        foreach (var route in AutomataGameMcpActionFamilies.Routes)
            if (route.Kind == kind && route.Mode is null) return new(this, route.Family);
        throw new InvalidOperationException(
            "command " + kind + " names no single gameplay action family");
    }
#endif

    internal bool OwnsPermanently(AutomationActionFamily family) =>
        PermanentLease(family)?.IsHeld == true;

    internal bool OwnsFamily(AutomationActionFamily family) =>
        PermanentLease(family)?.IsHeld == true || OwnsForCurrentOperation(family);

    internal bool Owns(GameActionFamily family)
    {
        foreach (var member in family.Members)
            if (!OwnsFamily(member)) return false;
        return true;
    }

    internal bool TryCaptureFamily(AutomationActionFamily family) =>
        PermanentLease(family)?.TryCaptureMutationPermit() == true ||
        TryCaptureGameMcpOperationPermit(family);

    internal bool TryCapture(GameActionFamily family)
    {
        foreach (var member in family.Members)
            if (!TryCaptureFamily(member))
                return false;
        return true;
    }

    internal string Failure(GameActionFamily family)
    {
        foreach (var member in family.Members)
        {
            var claimFailure = ClaimFailure(member);
            if (claimFailure.Length != 0) return claimFailure;
        }
        return family.NotHeld;
    }

    public void RefreshLoadedPluginInventory(int pluginCount, Func<string, bool> isLoaded)
    {
        if (pluginCount == _pluginInventoryCount) return;
        _pluginInventoryCount = pluginCount;
        var loaded = isLoaded(KnownAutoBuyPluginGuid);
        KnownAutoBuyLoaded = loaded;
        if (loaded && _knownExternal is null)
        {
            _knownExternal = _registry.RegisterKnownExternal(
                new ActionFamilyOwner(
                    new FeatureStatusKey(KnownAutoBuyPluginGuid, "AutoBuy"),
                    "AutobuyOrb"),
                KnownExternalFamilies);
        }
        else if (!loaded && _knownExternal is not null)
        {
            _knownExternal.Dispose();
            _knownExternal = null;
            ResetRetryFrames();
        }
    }

    public void Refresh(SuiteRuntimeConfiguration config, bool lifecycleReady, long frame = 0)
        => RefreshCore(config, lifecycleReady, frame);

#if SERVICE_CYCLE_PROFILE
    internal bool TryBeginGameMcpOperation(
        GameMcpCommandKind kind,
        string mode,
        out IDisposable scope,
        out string reason)
    {
        if (_gameMcpOperationLease is not null)
            throw new InvalidOperationException(
                "MCP gameplay operation ownership cannot be nested");
        var families = ClaimedFamilies(kind, mode);
        if (families.Length == 0)
        {
            scope = NoopGameMcpOperationScope.Instance;
            reason = "command " + kind + " has no gameplay action family";
            return false;
        }
        if (AlreadyOwns(families))
        {
            scope = NoopGameMcpOperationScope.Instance;
            reason = string.Empty;
            return true;
        }
        ClaimAttempts++;
        var missingFamilies = MissingFamilies(families);
        if (!_registry.TryClaimSet(
                new ActionFamilyOwner(
                    new FeatureStatusKey(
                        PluginIds.SuiteGuid,
                        "GameMcp." + kind),
                    "Game MCP " + kind + " operation"),
                missingFamilies,
                out _gameMcpOperationLease,
                out var conflict))
        {
            scope = NoopGameMcpOperationScope.Instance;
            reason = "could not claim " + conflict.Family + "; " +
                conflict.Owner.DisplayName + " currently owns it";
            return false;
        }
        _gameMcpOperationFamilies = families;
        scope = new GameMcpOperationScope(this);
        reason = string.Empty;
        return true;
    }
#endif

    private void RefreshCore(SuiteRuntimeConfiguration config, bool lifecycleReady, long frame)
    {
        var suiteReady = lifecycleReady && config.General.Enabled;
        foreach (var slot in _claims.Values)
            RefreshLease(slot, suiteReady && slot.Required(config), frame);
    }

    public void ReleaseLifecycleClaims()
    {
#if SERVICE_CYCLE_PROFILE
        EndGameMcpOperation();
#endif
        foreach (var slot in _claims.Values)
        {
            slot.Lease?.Dispose();
            slot.Lease = null;
            slot.Failure = slot.Metadata.OwnerName + " ownership was released for a lifecycle transition.";
        }
    }

    private void RefreshLease(DemandSlot slot, bool shouldOwn, long frame)
    {
        if (slot.Lease is not null && !slot.Lease.IsHeld)
        {
            slot.Lease.Dispose();
            slot.Lease = null;
            slot.Failure = slot.Metadata.OwnerName + " ownership was revoked.";
        }
        if (!shouldOwn)
        {
            slot.Lease?.Dispose();
            slot.Lease = null;
            slot.NextRetryFrame = 0;
            slot.Failure = slot.Metadata.DisabledReason;
            return;
        }
        if (slot.Lease is not null)
        {
            slot.Failure = string.Empty;
            return;
        }
        if (frame < slot.NextRetryFrame) return;
        ClaimAttempts++;
        if (!_registry.TryClaimSet(
                new ActionFamilyOwner(
                    new FeatureStatusKey(PluginIds.SuiteGuid, slot.Metadata.OwnerId),
                    slot.Metadata.OwnerName),
                new[] { slot.Metadata.Family }, out slot.Lease, out var conflict))
        {
            slot.Failure = $"{slot.Metadata.OwnerName} could not claim {conflict.Family}; " +
                $"{conflict.Owner.DisplayName} currently owns it.";
            slot.NextRetryFrame = frame + 60;
            return;
        }
        slot.Failure = string.Empty;
        slot.NextRetryFrame = 0;
    }

    private ActionFamilyLeaseSet? PermanentLease(AutomationActionFamily family) =>
        _claims.TryGetValue(family, out var slot) ? slot.Lease : null;

    private string ClaimFailure(AutomationActionFamily family) =>
        _claims.TryGetValue(family, out var slot) ? slot.Failure : string.Empty;

    private sealed class DemandSlot
    {
        private readonly AutomataActionFamilyDemand[] _demands;
        internal DemandSlot(AutomataActionFamilyDemand[] demands) => _demands = demands;
        internal AutomataActionFamilyDemand Metadata => _demands[0];
        internal ActionFamilyLeaseSet? Lease;
        internal long NextRetryFrame;
        internal string Failure = string.Empty;

        internal bool Required(SuiteRuntimeConfiguration config)
        {
            foreach (var demand in _demands)
                if (demand.IsRequired(config)) return true;
            return false;
        }
    }

    internal bool OwnsForCurrentOperation(AutomationActionFamily family)
    {
#if SERVICE_CYCLE_PROFILE
        if (_gameMcpOperationLease?.IsHeld != true) return false;
        for (var index = 0; index < _gameMcpOperationFamilies.Length; index++)
            if (_gameMcpOperationFamilies[index] == family) return true;
#endif
        return false;
    }

    private bool TryCaptureGameMcpOperationPermit(AutomationActionFamily family)
    {
#if SERVICE_CYCLE_PROFILE
        return OwnsForCurrentOperation(family) &&
            _gameMcpOperationLease?.TryCaptureMutationPermit() == true;
#else
        return false;
#endif
    }

#if SERVICE_CYCLE_PROFILE
    private static AutomationActionFamily[] ClaimedFamilies(GameMcpCommandKind kind, string mode)
    {
        foreach (var route in AutomataGameMcpActionFamilies.Routes)
            if (route.Kind == kind && (route.Mode is null || route.Mode == mode))
                return route.Family.Members;
        return Array.Empty<AutomationActionFamily>();
    }

    private bool AlreadyOwns(AutomationActionFamily[] families)
    {
        for (var index = 0; index < families.Length; index++)
            if (!OwnsPermanently(families[index])) return false;
        return true;
    }

    private AutomationActionFamily[] MissingFamilies(AutomationActionFamily[] families)
    {
        var missing = new AutomationActionFamily[families.Length];
        var count = 0;
        for (var index = 0; index < families.Length; index++)
            if (!OwnsPermanently(families[index])) missing[count++] = families[index];
        if (count == missing.Length) return missing;
        Array.Resize(ref missing, count);
        return missing;
    }

    private void EndGameMcpOperation()
    {
        _gameMcpOperationLease?.Dispose();
        _gameMcpOperationLease = null;
        _gameMcpOperationFamilies = Array.Empty<AutomationActionFamily>();
    }

    private sealed class GameMcpOperationScope : IDisposable
    {
        private AutomataActionFamilyOwnership? _owner;

        internal GameMcpOperationScope(AutomataActionFamilyOwnership owner) => _owner = owner;

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.EndGameMcpOperation();
        }
    }

    private sealed class NoopGameMcpOperationScope : IDisposable
    {
        internal static readonly NoopGameMcpOperationScope Instance = new();
        public void Dispose() { }
    }
#endif

    public void Dispose()
    {
        ReleaseLifecycleClaims();
        _knownExternal?.Dispose();
        _knownExternal = null;
    }

    private void ResetRetryFrames()
    {
        foreach (var slot in _claims.Values) slot.NextRetryFrame = 0;
    }
}

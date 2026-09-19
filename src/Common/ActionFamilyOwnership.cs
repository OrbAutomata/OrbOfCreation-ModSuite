using System;
using System.Collections.Generic;
using System.Threading;

namespace OrbModding.Common;

public enum AutomationActionFamily
{
    StructurePurchase = 100,
    UpgradePurchase = 101,
    NativeMultiBuyOverride = 102,
    SpellCast = 200,
    SpellLevelPurchase = 201,
    ConceptAssignment = 300,
    HarvestAction = 400,
    ConsumableUse = 500,
    CraftingQueueSubmission = 501,
    DiscoveryTreeOfferLifecycle = 502,
    SpellWorkbenchLifecycle = 503,
    SpellComposition = 504,
    SpellLoadout = 505,
    Targeting = 506,
    GenericDiscovery = 507,
    EquipmentLoadout = 508,
    ChallengeLifecycle = 509,
    PrestigeLifecycle = 510,
    ResearchLifecycle = 511,
    AlchemyLoadout = 512,
    RitualLifecycle = 513,
    GenericLevel = 514,
    PlayerLoadout = 515,
    HarvestLifecycle = 516,
    StructureLifecycle = 517,
    RunTransition = 518,
    SpellMasteryExperienceGrant = 600,
    ArtifactMasteryExperienceGrant = 601,
    AlchemyMasteryExperienceGrant = 602,
}

public readonly struct ActionFamilyOwner : IEquatable<ActionFamilyOwner>
{
    public ActionFamilyOwner(FeatureStatusKey featureKey, string displayName)
    {
        if (string.IsNullOrWhiteSpace(featureKey.PluginId) || string.IsNullOrWhiteSpace(featureKey.FeatureId))
            throw new ArgumentException("An initialized feature key is required.", nameof(featureKey));
        DisplayName = (displayName ?? string.Empty).Trim();
        if (DisplayName.Length == 0) throw new ArgumentException("An ownership display name is required.", nameof(displayName));
        FeatureKey = featureKey;
    }

    public FeatureStatusKey FeatureKey { get; }
    public string DisplayName { get; }

    public bool Equals(ActionFamilyOwner other) =>
        FeatureKey.Equals(other.FeatureKey) && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal);
    public override bool Equals(object? obj) => obj is ActionFamilyOwner other && Equals(other);
    public override int GetHashCode() => unchecked((FeatureKey.GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(DisplayName ?? string.Empty));
    public override string ToString() => DisplayName ?? string.Empty;
}

public readonly struct ActionFamilyConflict
{
    internal ActionFamilyConflict(AutomationActionFamily family, ActionFamilyOwner owner)
    {
        Family = family;
        Owner = owner;
    }

    public AutomationActionFamily Family { get; }
    public ActionFamilyOwner Owner { get; }
}

public sealed class ActionFamilyLeaseSet : IDisposable
{
    private ActionFamilyOwnershipRegistry? _registry;
    private readonly long _token;
    private readonly AutomationActionFamily[] _families;
    private int _held = 1;

    internal ActionFamilyLeaseSet(
        ActionFamilyOwnershipRegistry registry,
        long token,
        AutomationActionFamily[] families)
    {
        _registry = registry;
        _token = token;
        _families = families;
    }

    public bool IsHeld => Volatile.Read(ref _held) != 0;

    public bool Owns(AutomationActionFamily family)
    {
        if (!IsHeld) return false;
        for (var i = 0; i < _families.Length; i++)
            if (_families[i] == family) return true;
        return false;
    }

    /// <summary>
    /// Captures permission for one immediate synchronous native transaction.
    /// A later revocation blocks new transactions but does not split an
    /// already-started multi-step native mutation into a partially applied state.
    /// </summary>
    public bool TryCaptureMutationPermit() => IsHeld;

    internal long Token => _token;
    internal void Revoke() => Interlocked.Exchange(ref _held, 0);

    public void Dispose()
    {
        var registry = Interlocked.Exchange(ref _registry, null);
        if (registry is null) return;
        registry.Release(_token, this);
        Revoke();
    }
}

public sealed class ActionFamilyOwnershipRegistry
{
    private sealed class Claim
    {
        public Claim(long token, ActionFamilyOwner owner, ActionFamilyLeaseSet? lease, bool external)
        {
            Token = token;
            Owner = owner;
            Lease = lease;
            External = external;
        }

        public long Token { get; }
        public ActionFamilyOwner Owner { get; }
        public ActionFamilyLeaseSet? Lease { get; }
        public bool External { get; }
    }

    private sealed class ExternalRegistration : IDisposable
    {
        private ActionFamilyOwnershipRegistry? _registry;
        private readonly long _token;

        public ExternalRegistration(ActionFamilyOwnershipRegistry registry, long token)
        {
            _registry = registry;
            _token = token;
        }

        public void Dispose()
        {
            var registry = Interlocked.Exchange(ref _registry, null);
            registry?.ReleaseExternal(_token);
        }
    }

    private readonly object _gate = new();
    private readonly Dictionary<AutomationActionFamily, Claim> _claims = new();
    private long _nextToken;

    public static ActionFamilyOwnershipRegistry Shared { get; } = new();

    public bool TryClaimSet(
        ActionFamilyOwner owner,
        IReadOnlyList<AutomationActionFamily> families,
        out ActionFamilyLeaseSet? lease,
        out ActionFamilyConflict conflict)
    {
        var normalized = NormalizeFamilies(families);
        lock (_gate)
        {
            for (var i = 0; i < normalized.Length; i++)
            {
                if (!_claims.TryGetValue(normalized[i], out var existing)) continue;
                lease = null;
                conflict = new ActionFamilyConflict(normalized[i], existing.Owner);
                return false;
            }

            var token = checked(++_nextToken);
            lease = new ActionFamilyLeaseSet(this, token, normalized);
            for (var i = 0; i < normalized.Length; i++)
                _claims.Add(normalized[i], new Claim(token, owner, lease, external: false));
            conflict = default;
            return true;
        }
    }

    public IDisposable RegisterKnownExternal(
        ActionFamilyOwner owner,
        IReadOnlyList<AutomationActionFamily> families)
    {
        var normalized = NormalizeFamilies(families);
        lock (_gate)
        {
            for (var i = 0; i < normalized.Length; i++)
            {
                if (_claims.TryGetValue(normalized[i], out var existing) && existing.External)
                    throw new InvalidOperationException(
                        $"Action family {normalized[i]} is already registered by known external owner {existing.Owner.DisplayName}.");
            }
            var revokedTokens = new HashSet<long>();
            for (var i = 0; i < normalized.Length; i++)
            {
                if (!_claims.TryGetValue(normalized[i], out var existing)) continue;
                if (revokedTokens.Add(existing.Token)) RevokeCooperativeClaim(existing);
            }

            var token = checked(++_nextToken);
            for (var i = 0; i < normalized.Length; i++)
                _claims.Add(normalized[i], new Claim(token, owner, lease: null, external: true));
            return new ExternalRegistration(this, token);
        }
    }

    internal void Release(long token, ActionFamilyLeaseSet lease)
    {
        lock (_gate)
        {
            RemoveClaims(token, claim => !claim.External && ReferenceEquals(claim.Lease, lease));
        }
    }

    private void ReleaseExternal(long token)
    {
        lock (_gate) RemoveClaims(token, claim => claim.External);
    }

    private void RevokeCooperativeClaim(Claim claim)
    {
        claim.Lease?.Revoke();
        RemoveClaims(claim.Token, candidate => !candidate.External && ReferenceEquals(candidate.Lease, claim.Lease));
    }

    private void RemoveClaims(long token, Func<Claim, bool> predicate)
    {
        var remove = new List<AutomationActionFamily>();
        foreach (var pair in _claims)
            if (pair.Value.Token == token && predicate(pair.Value)) remove.Add(pair.Key);
        for (var i = 0; i < remove.Count; i++) _claims.Remove(remove[i]);
    }

    private static AutomationActionFamily[] NormalizeFamilies(IReadOnlyList<AutomationActionFamily> families)
    {
        if (families is null) throw new ArgumentNullException(nameof(families));
        if (families.Count == 0) throw new ArgumentException("At least one action family is required.", nameof(families));
        var unique = new HashSet<AutomationActionFamily>();
        var normalized = new AutomationActionFamily[families.Count];
        for (var i = 0; i < families.Count; i++)
        {
            var family = families[i];
            if (!Enum.IsDefined(typeof(AutomationActionFamily), family))
                throw new ArgumentOutOfRangeException(nameof(families), family, "Unknown action family.");
            if (!unique.Add(family)) throw new ArgumentException("Action families must be unique.", nameof(families));
            normalized[i] = family;
        }
        return normalized;
    }
}

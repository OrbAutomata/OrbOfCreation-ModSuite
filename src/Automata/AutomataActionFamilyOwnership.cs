using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common;
#if SERVICE_CYCLE_PROFILE
using OrbAutomata.GameMcp;
#endif

namespace OrbAutomata;

internal sealed class AutomataActionFamilyOwnership : IDisposable
{
    internal const string KnownAutoBuyPluginGuid = "IngoH.OrbOfCreation.AutoBuyOrb";

    internal static readonly GameActionFamily Harvest =
        new(GameActionFamily.McpOperation, AutomationActionFamily.HarvestAction);
    internal static readonly GameActionFamily Items = new(
        "Auto Items",
        AutomationActionFamily.ConsumableUse,
        AutomationActionFamily.NativeMultiBuyOverride);
    internal static readonly GameActionFamily Scribe =
        new("Auto Scribe", AutomationActionFamily.CraftingQueueSubmission);
#if SERVICE_CYCLE_PROFILE
    internal static readonly (GameMcpCommandKind Kind, string? Mode, GameActionFamily Family)[]
        GameMcpFamilies =
        {
            (GameMcpCommandKind.Purchase, "structure", Mcp(AutomationActionFamily.StructurePurchase)),
            (GameMcpCommandKind.Purchase, "upgrade", Mcp(
                AutomationActionFamily.UpgradePurchase,
                AutomationActionFamily.NativeMultiBuyOverride)),
            (GameMcpCommandKind.Cast, null, Mcp(AutomationActionFamily.SpellCast)),
            (GameMcpCommandKind.Concept, null, Mcp(AutomationActionFamily.ConceptAssignment)),
            (GameMcpCommandKind.SpellLevel, null, Mcp(AutomationActionFamily.SpellLevelPurchase)),
            (GameMcpCommandKind.Harvest, null, Harvest),
            (GameMcpCommandKind.DiscoveryTreeOffer, null,
                Mcp(AutomationActionFamily.DiscoveryTreeOfferLifecycle)),
            (GameMcpCommandKind.SpellWorkbench, null,
                Mcp(AutomationActionFamily.SpellWorkbenchLifecycle)),
            (GameMcpCommandKind.SpellComposition, null, Mcp(AutomationActionFamily.SpellComposition)),
            (GameMcpCommandKind.SpellLoadout, null, Mcp(AutomationActionFamily.SpellLoadout)),
            (GameMcpCommandKind.Targeting, null, Mcp(AutomationActionFamily.Targeting)),
            (GameMcpCommandKind.Consumable, null, Items),
            (GameMcpCommandKind.Crafting, null, Scribe),
            (GameMcpCommandKind.EquipmentLoadout, null, Mcp(AutomationActionFamily.EquipmentLoadout)),
            (GameMcpCommandKind.Challenge, null, Mcp(AutomationActionFamily.ChallengeLifecycle)),
            (GameMcpCommandKind.Prestige, null, Mcp(AutomationActionFamily.PrestigeLifecycle)),
            (GameMcpCommandKind.Research, null, Mcp(AutomationActionFamily.ResearchLifecycle)),
            (GameMcpCommandKind.AlchemyLoadout, null, Mcp(AutomationActionFamily.AlchemyLoadout)),
            (GameMcpCommandKind.RitualLifecycle, null, Mcp(AutomationActionFamily.RitualLifecycle)),
            (GameMcpCommandKind.GenericLevel, null, Mcp(AutomationActionFamily.GenericLevel)),
            (GameMcpCommandKind.Loadout, null, Mcp(AutomationActionFamily.PlayerLoadout)),
            (GameMcpCommandKind.HarvestLifecycle, null, Mcp(AutomationActionFamily.HarvestLifecycle)),
            (GameMcpCommandKind.StructureLifecycle, null,
                Mcp(AutomationActionFamily.StructureLifecycle)),
            (GameMcpCommandKind.ReturnToMenu, null, Mcp(AutomationActionFamily.RunTransition)),
        };

    private static GameActionFamily Mcp(params AutomationActionFamily[] members) =>
        new(GameActionFamily.McpOperation, members);
#endif

    private static readonly AutomationActionFamily[] KnownExternalFamilies =
        { AutomationActionFamily.StructurePurchase, AutomationActionFamily.NativeMultiBuyOverride };

    private readonly ActionFamilyOwnershipRegistry _registry;
    private ActionFamilyLeaseSet? _structures;
    private ActionFamilyLeaseSet? _upgrades;
    private ActionFamilyLeaseSet? _multiBuy;
    private ActionFamilyLeaseSet? _cast;
    private ActionFamilyLeaseSet? _concept;
    private ActionFamilyLeaseSet? _spellLevel;
    private ActionFamilyLeaseSet? _harvest;
    private ActionFamilyLeaseSet? _items;
    private ActionFamilyLeaseSet? _scribe;
#if SERVICE_CYCLE_PROFILE
    private ActionFamilyLeaseSet? _gameMcpOperationLease;
    private AutomationActionFamily[] _gameMcpOperationFamilies =
        Array.Empty<AutomationActionFamily>();
#endif
    private IDisposable? _knownExternal;
    private int _pluginInventoryCount = -1;
    private long _structuresRetryFrame;
    private long _upgradesRetryFrame;
    private long _multiBuyRetryFrame;
    private long _castRetryFrame;
    private long _conceptRetryFrame;
    private long _spellLevelRetryFrame;
    private long _harvestRetryFrame;
    private long _itemsRetryFrame;
    private long _scribeRetryFrame;
    private string _multiBuyClaimFailure = string.Empty;
    private string _itemsClaimFailure = string.Empty;
    private string _scribeClaimFailure = string.Empty;

    internal int ClaimAttempts { get; private set; }
    public bool KnownAutoBuyLoaded { get; private set; }

    public AutomataActionFamilyOwnership(ActionFamilyOwnershipRegistry? registry = null) =>
        _registry = registry ?? ActionFamilyOwnershipRegistry.Shared;

    public bool OwnsAutoBuy(AutoBuyCandidateKind kind) => kind switch
    {
        AutoBuyCandidateKind.Structure => _structures?.IsHeld == true ||
            OwnsGameMcpOperationFamily(AutomationActionFamily.StructurePurchase),
        AutoBuyCandidateKind.Upgrade =>
            (_upgrades?.IsHeld == true && _multiBuy?.IsHeld == true) ||
            (OwnsGameMcpOperationFamily(AutomationActionFamily.UpgradePurchase) &&
             OwnsGameMcpOperationFamily(AutomationActionFamily.NativeMultiBuyOverride)),
        _ => false,
    };

    public AutoBuyCandidateKinds EffectiveAutoBuyOwnership(AutoBuyConfiguration config)
    {
        var owned = AutoBuyCandidateKinds.None;
        if (!config.IncludeStructures || OwnsAutoBuy(AutoBuyCandidateKind.Structure))
            owned |= AutoBuyCandidateKinds.Structures;
        if (!config.IncludeUpgrades || OwnsAutoBuy(AutoBuyCandidateKind.Upgrade))
            owned |= AutoBuyCandidateKinds.Upgrades;
        return owned;
    }

    public bool OwnsCast => _cast?.IsHeld == true ||
        OwnsGameMcpOperationFamily(AutomationActionFamily.SpellCast);
    public bool OwnsConcept => _concept?.IsHeld == true ||
        OwnsGameMcpOperationFamily(AutomationActionFamily.ConceptAssignment);
    public bool OwnsSpellLevel => _spellLevel?.IsHeld == true ||
        OwnsGameMcpOperationFamily(AutomationActionFamily.SpellLevelPurchase);
    public bool OwnsHarvest => _harvest?.IsHeld == true ||
        OwnsGameMcpOperationFamily(AutomationActionFamily.HarvestAction);
    public bool OwnsItems =>
        (_items?.IsHeld == true ||
         OwnsGameMcpOperationFamily(AutomationActionFamily.ConsumableUse)) &&
        (_multiBuy?.IsHeld == true ||
         OwnsGameMcpOperationFamily(AutomationActionFamily.NativeMultiBuyOverride));
    public bool OwnsScribe => _scribe?.IsHeld == true;
    public bool OwnsDiscoveryTreeOffers =>
        OwnsGameMcpOperationFamily(AutomationActionFamily.DiscoveryTreeOfferLifecycle);

    internal GameActionFamilyHandle Family(GameActionFamily family) => new(this, family);

#if SERVICE_CYCLE_PROFILE
    internal GameActionFamilyHandle Family(GameMcpCommandKind kind)
    {
        foreach (var route in GameMcpFamilies)
            if (route.Kind == kind && route.Mode is null) return new(this, route.Family);
        throw new InvalidOperationException(
            "command " + kind + " names no single gameplay action family");
    }

    internal static bool NamesGameMcpKind(GameMcpCommandKind kind)
    {
        foreach (var route in GameMcpFamilies)
            if (route.Kind == kind) return true;
        return false;
    }
#endif

    internal bool TryCapture(GameActionFamily family)
    {
        foreach (var member in family.Members)
            if (PermanentLease(member)?.TryCaptureMutationPermit() != true &&
                !TryCaptureGameMcpOperationPermit(member))
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

    private void RefreshCore(
        SuiteRuntimeConfiguration config,
        bool lifecycleReady,
        long frame)
    {
        var suiteReady = lifecycleReady && config.General.Enabled;
        RefreshLease(ref _structures, ref _structuresRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.IncludeStructures,
            "AutoBuy.Structures", "Automata Auto Buy Structures",
            AutomationActionFamily.StructurePurchase);
        RefreshLease(ref _upgrades, ref _upgradesRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades,
            "AutoBuy.Upgrades", "Automata Auto Buy Upgrades",
            AutomationActionFamily.UpgradePurchase);
        RefreshLeaseWithReason(
            ref _multiBuy,
            ref _multiBuyRetryFrame,
            ref _multiBuyClaimFailure,
            frame,
            suiteReady &&
            (config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades ||
             config.CanStartAutoItemsActively &&
             AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems)),
            "NativeMultiBuy",
            "Automata Native Multi-Buy Scope",
            AutomationActionFamily.NativeMultiBuyOverride,
            "No enabled service currently requires NativeMultiBuyOverride.");
        RefreshLease(ref _cast, ref _castRetryFrame, frame,
            suiteReady && config.CanStartAutoCastActively,
            "AutoCast", "Automata Auto Cast", AutomationActionFamily.SpellCast);
        RefreshLease(ref _concept, ref _conceptRetryFrame, frame,
            suiteReady && config.CanStartAutoConceptActively,
            "AutoConcept", "Automata Auto Concept",
            AutomationActionFamily.ConceptAssignment);
        RefreshLease(ref _spellLevel, ref _spellLevelRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.AutoLevelSpells,
            "SpellLevel", "Automata Spell Leveling",
            AutomationActionFamily.SpellLevelPurchase);
        RefreshLease(ref _harvest, ref _harvestRetryFrame, frame,
            suiteReady && config.CanStartAutoHarvestActively &&
                (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees),
            "AutoHarvest", "Automata Auto Harvest",
            AutomationActionFamily.HarvestAction);
        RefreshLeaseWithReason(
            ref _items,
            ref _itemsRetryFrame,
            ref _itemsClaimFailure,
            frame,
            suiteReady &&
            config.CanStartAutoItemsActively &&
            AutoItemsConfigurationPolicy.HasEnabledFamily(config.AutoItems),
            "AutoItems",
            "Automata Auto Items",
            AutomationActionFamily.ConsumableUse,
            "Committed configuration no longer enables Auto Items consumable use.");
        RefreshLeaseWithReason(
            ref _scribe,
            ref _scribeRetryFrame,
            ref _scribeClaimFailure,
            frame,
            suiteReady &&
            AutoScribeConfigurationPolicy.IsOperational(config),
            "AutoScribe",
            "Automata Auto Scribe",
            AutomationActionFamily.CraftingQueueSubmission,
            "Committed configuration no longer enables Auto Scribe production.");
    }

    public void ReleaseLifecycleClaims()
    {
#if SERVICE_CYCLE_PROFILE
        EndGameMcpOperation();
#endif
        Release(ref _scribe);
        Release(ref _items);
        Release(ref _harvest);
        Release(ref _spellLevel);
        Release(ref _concept);
        Release(ref _cast);
        Release(ref _upgrades);
        Release(ref _multiBuy);
        Release(ref _structures);
        _itemsClaimFailure = "Auto Items ownership was released for a lifecycle transition.";
        _scribeClaimFailure =
            "Auto Scribe ownership was released for a lifecycle transition.";
        _multiBuyClaimFailure =
            "NativeMultiBuyOverride ownership was released for a lifecycle transition.";
    }

    private void RefreshLease(
        ref ActionFamilyLeaseSet? lease,
        ref long nextRetryFrame,
        long frame,
        bool shouldOwn,
        string featureId,
        string displayName,
        AutomationActionFamily family)
    {
        if (lease is not null && !lease.IsHeld)
        {
            lease.Dispose();
            lease = null;
        }
        if (!shouldOwn)
        {
            Release(ref lease);
            nextRetryFrame = 0;
            return;
        }
        if (lease is not null || frame < nextRetryFrame) return;
        ClaimAttempts++;
        if (!_registry.TryClaimSet(
            new ActionFamilyOwner(new FeatureStatusKey(PluginIds.SuiteGuid, featureId), displayName),
            new[] { family },
            out lease,
            out _))
            nextRetryFrame = frame + 60;
        else
            nextRetryFrame = 0;
    }

    private void RefreshLeaseWithReason(
        ref ActionFamilyLeaseSet? lease,
        ref long nextRetryFrame,
        ref string failureReason,
        long frame,
        bool shouldOwn,
        string featureId,
        string displayName,
        AutomationActionFamily family,
        string disabledReason)
    {
        if (lease is not null && !lease.IsHeld)
        {
            lease.Dispose();
            lease = null;
            failureReason = displayName + " ownership was revoked.";
        }
        if (!shouldOwn)
        {
            Release(ref lease);
            nextRetryFrame = 0;
            failureReason = disabledReason;
            return;
        }
        if (lease is not null)
        {
            failureReason = string.Empty;
            return;
        }
        if (frame < nextRetryFrame) return;

        ClaimAttempts++;
        if (!_registry.TryClaimSet(
                new ActionFamilyOwner(
                    new FeatureStatusKey(PluginIds.SuiteGuid, featureId),
                    displayName),
                new[] { family },
                out lease,
                out var conflict))
        {
            failureReason =
                $"{displayName} could not claim {conflict.Family}; " +
                $"{conflict.Owner.DisplayName} currently owns it.";
            nextRetryFrame = frame + 60;
            return;
        }

        failureReason = string.Empty;
        nextRetryFrame = 0;
    }

    private static void Release(ref ActionFamilyLeaseSet? lease)
    {
        lease?.Dispose();
        lease = null;
    }

    private ActionFamilyLeaseSet? PermanentLease(AutomationActionFamily family) => family switch
    {
        AutomationActionFamily.StructurePurchase => _structures,
        AutomationActionFamily.UpgradePurchase => _upgrades,
        AutomationActionFamily.NativeMultiBuyOverride => _multiBuy,
        AutomationActionFamily.SpellCast => _cast,
        AutomationActionFamily.ConceptAssignment => _concept,
        AutomationActionFamily.SpellLevelPurchase => _spellLevel,
        AutomationActionFamily.HarvestAction => _harvest,
        AutomationActionFamily.ConsumableUse => _items,
        AutomationActionFamily.CraftingQueueSubmission => _scribe,
        _ => null,
    };

    private string ClaimFailure(AutomationActionFamily family) => family switch
    {
        AutomationActionFamily.ConsumableUse => _itemsClaimFailure,
        AutomationActionFamily.NativeMultiBuyOverride => _multiBuyClaimFailure,
        AutomationActionFamily.CraftingQueueSubmission => _scribeClaimFailure,
        _ => string.Empty,
    };

    private bool OwnsGameMcpOperationFamily(AutomationActionFamily family)
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
        return OwnsGameMcpOperationFamily(family) &&
            _gameMcpOperationLease?.TryCaptureMutationPermit() == true;
#else
        return false;
#endif
    }

#if SERVICE_CYCLE_PROFILE
    private static AutomationActionFamily[] ClaimedFamilies(GameMcpCommandKind kind, string mode)
    {
        foreach (var route in GameMcpFamilies)
            if (route.Kind == kind && (route.Mode is null || route.Mode == mode))
                return route.Family.Members;
        return Array.Empty<AutomationActionFamily>();
    }

    private bool AlreadyOwns(AutomationActionFamily[] families)
    {
        for (var index = 0; index < families.Length; index++)
            if (!PermanentlyOwns(families[index])) return false;
        return true;
    }

    private AutomationActionFamily[] MissingFamilies(AutomationActionFamily[] families)
    {
        var missing = new AutomationActionFamily[families.Length];
        var count = 0;
        for (var index = 0; index < families.Length; index++)
            if (!PermanentlyOwns(families[index])) missing[count++] = families[index];
        if (count == missing.Length) return missing;
        Array.Resize(ref missing, count);
        return missing;
    }

    private bool PermanentlyOwns(AutomationActionFamily family) =>
        PermanentLease(family)?.IsHeld == true;

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
        _structuresRetryFrame = 0;
        _upgradesRetryFrame = 0;
        _multiBuyRetryFrame = 0;
        _castRetryFrame = 0;
        _conceptRetryFrame = 0;
        _spellLevelRetryFrame = 0;
        _harvestRetryFrame = 0;
        _itemsRetryFrame = 0;
        _scribeRetryFrame = 0;
    }
}

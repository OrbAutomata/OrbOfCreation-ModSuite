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

    private static readonly AutomationActionFamily[] StructureFamilies =
        { AutomationActionFamily.StructurePurchase };
    private static readonly AutomationActionFamily[] UpgradeFamilies =
        { AutomationActionFamily.UpgradePurchase };
    private static readonly AutomationActionFamily[] MultiBuyFamilies =
        { AutomationActionFamily.NativeMultiBuyOverride };
    private static readonly AutomationActionFamily[] CastFamilies =
        { AutomationActionFamily.SpellCast };
    private static readonly AutomationActionFamily[] ConceptFamilies =
        { AutomationActionFamily.ConceptAssignment };
    private static readonly AutomationActionFamily[] SpellLevelFamilies =
        { AutomationActionFamily.SpellLevelPurchase };
    private static readonly AutomationActionFamily[] HarvestFamilies =
        { AutomationActionFamily.HarvestAction };
    private static readonly AutomationActionFamily[] ItemFamilies =
        { AutomationActionFamily.ConsumableUse };
    private static readonly AutomationActionFamily[] ConsumableFamilies =
        { AutomationActionFamily.ConsumableUse, AutomationActionFamily.NativeMultiBuyOverride };
    private static readonly AutomationActionFamily[] ScribeFamilies =
        { AutomationActionFamily.CraftingQueueSubmission };
    private static readonly AutomationActionFamily[] DiscoveryTreeOfferFamilies =
        { AutomationActionFamily.DiscoveryTreeOfferLifecycle };
    private static readonly AutomationActionFamily[] SpellWorkbenchFamilies =
        { AutomationActionFamily.SpellWorkbenchLifecycle };
    private static readonly AutomationActionFamily[] SpellCompositionFamilies =
        { AutomationActionFamily.SpellComposition };
    private static readonly AutomationActionFamily[] SpellLoadoutFamilies =
        { AutomationActionFamily.SpellLoadout };
    private static readonly AutomationActionFamily[] TargetingFamilies =
        { AutomationActionFamily.Targeting };
    private static readonly AutomationActionFamily[] GenericDiscoveryFamilies =
        { AutomationActionFamily.GenericDiscovery };
    private static readonly AutomationActionFamily[] EquipmentLoadoutFamilies =
        { AutomationActionFamily.EquipmentLoadout };
    private static readonly AutomationActionFamily[] ChallengeFamilies =
        { AutomationActionFamily.ChallengeLifecycle };
    private static readonly AutomationActionFamily[] PrestigeFamilies =
        { AutomationActionFamily.PrestigeLifecycle };
    private static readonly AutomationActionFamily[] ResearchFamilies =
        { AutomationActionFamily.ResearchLifecycle };
    private static readonly AutomationActionFamily[] AlchemyLoadoutFamilies =
        { AutomationActionFamily.AlchemyLoadout };
    private static readonly AutomationActionFamily[] RitualLifecycleFamilies =
        { AutomationActionFamily.RitualLifecycle };
    private static readonly AutomationActionFamily[] GenericLevelFamilies =
        { AutomationActionFamily.GenericLevel };
    private static readonly AutomationActionFamily[] PlayerLoadoutFamilies =
        { AutomationActionFamily.PlayerLoadout };
    private static readonly AutomationActionFamily[] HarvestLifecycleFamilies =
        { AutomationActionFamily.HarvestLifecycle };
    private static readonly AutomationActionFamily[] StructureLifecycleFamilies =
        { AutomationActionFamily.StructureLifecycle };
    private static readonly AutomationActionFamily[] RunTransitionFamilies =
        { AutomationActionFamily.RunTransition };
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
    public bool TryCaptureHarvestMutationPermit() =>
        _harvest?.TryCaptureMutationPermit() == true ||
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.HarvestAction);
    public bool TryCaptureItemMutationPermit() =>
        TryCaptureFamilyPermit(AutomationActionFamily.ConsumableUse, _items) &&
        TryCaptureFamilyPermit(AutomationActionFamily.NativeMultiBuyOverride, _multiBuy);
    private bool TryCaptureFamilyPermit(
        AutomationActionFamily family,
        ActionFamilyLeaseSet? permanent)
    {
        if (permanent?.TryCaptureMutationPermit() == true) return true;
        return TryCaptureGameMcpOperationPermit(family);
    }
    public bool TryCaptureScribeMutationPermit() =>
        _scribe?.TryCaptureMutationPermit() == true ||
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.CraftingQueueSubmission);
    public bool TryCaptureDiscoveryTreeOfferMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.DiscoveryTreeOfferLifecycle);
    public bool TryCaptureSpellWorkbenchMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.SpellWorkbenchLifecycle);
    public bool TryCaptureSpellCompositionMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.SpellComposition);
    public bool TryCaptureSpellLoadoutMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.SpellLoadout);
    public bool TryCaptureTargetingMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.Targeting);
    public bool TryCaptureGenericDiscoveryMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.GenericDiscovery);
    public bool TryCaptureEquipmentLoadoutMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.EquipmentLoadout);
    public bool TryCaptureChallengeMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.ChallengeLifecycle);
    public bool TryCapturePrestigeMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.PrestigeLifecycle);
    public bool TryCaptureResearchMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.ResearchLifecycle);
    public bool TryCaptureAlchemyLoadoutMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.AlchemyLoadout);
    public bool TryCaptureRitualLifecycleMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.RitualLifecycle);
    public bool TryCaptureGenericLevelMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.GenericLevel);
    public bool TryCapturePlayerLoadoutMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.PlayerLoadout);
    public bool TryCaptureHarvestLifecycleMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.HarvestLifecycle);
    public bool TryCaptureStructureLifecycleMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.StructureLifecycle);
    public bool TryCaptureRunTransitionMutationPermit() =>
        TryCaptureGameMcpOperationPermit(AutomationActionFamily.RunTransition);
    public string ItemsOwnershipFailure =>
        _itemsClaimFailure.Length != 0
            ? _itemsClaimFailure
            : _multiBuyClaimFailure.Length != 0
                ? _multiBuyClaimFailure
                : "Auto Items does not hold ConsumableUse and NativeMultiBuyOverride.";
    public string ScribeOwnershipFailure =>
        _scribeClaimFailure.Length != 0
            ? _scribeClaimFailure
            : "Auto Scribe does not hold CraftingQueueSubmission.";
    public string DiscoveryTreeOfferOwnershipFailure =>
        "The current MCP operation does not hold DiscoveryTreeOfferLifecycle.";
    public string SpellWorkbenchOwnershipFailure =>
        "The current MCP operation does not hold SpellWorkbenchLifecycle.";
    public string SpellCompositionOwnershipFailure =>
        "The current MCP operation does not hold SpellComposition.";
    public string SpellLoadoutOwnershipFailure =>
        "The current MCP operation does not hold SpellLoadout.";
    public string TargetingOwnershipFailure =>
        "The current MCP operation does not hold Targeting.";
    public string GenericDiscoveryOwnershipFailure =>
        "The current MCP operation does not hold GenericDiscovery.";
    public string EquipmentLoadoutOwnershipFailure =>
        "The current MCP operation does not hold EquipmentLoadout.";
    public string ChallengeOwnershipFailure =>
        "The current MCP operation does not hold ChallengeLifecycle.";
    public string PrestigeOwnershipFailure =>
        "The current MCP operation does not hold PrestigeLifecycle.";
    public string ResearchOwnershipFailure =>
        "The current MCP operation does not hold ResearchLifecycle.";
    public string AlchemyLoadoutOwnershipFailure =>
        "The current MCP operation does not hold AlchemyLoadout.";
    public string RitualLifecycleOwnershipFailure =>
        "The current MCP operation does not hold RitualLifecycle.";
    public string GenericLevelOwnershipFailure =>
        "The current MCP operation does not hold GenericLevel.";
    public string PlayerLoadoutOwnershipFailure =>
        "The current MCP operation does not hold PlayerLoadout.";
    public string HarvestOwnershipFailure =>
        "The current MCP operation does not hold HarvestAction.";
    public string HarvestLifecycleOwnershipFailure =>
        "The current MCP operation does not hold HarvestLifecycle.";
    public string StructureLifecycleOwnershipFailure =>
        "The current MCP operation does not hold StructureLifecycle.";
    public string RunTransitionOwnershipFailure =>
        "The current MCP operation does not hold RunTransition.";

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
        var families = GameMcpFamilies(kind, mode);
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
            "AutoBuy.Structures", "Automata Auto Buy Structures", StructureFamilies);
        RefreshLease(ref _upgrades, ref _upgradesRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.IncludeUpgrades,
            "AutoBuy.Upgrades", "Automata Auto Buy Upgrades", UpgradeFamilies);
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
            MultiBuyFamilies,
            "No enabled service currently requires NativeMultiBuyOverride.");
        RefreshLease(ref _cast, ref _castRetryFrame, frame,
            suiteReady && config.CanStartAutoCastActively,
            "AutoCast", "Automata Auto Cast", CastFamilies);
        RefreshLease(ref _concept, ref _conceptRetryFrame, frame,
            suiteReady && config.CanStartAutoConceptActively,
            "AutoConcept", "Automata Auto Concept", ConceptFamilies);
        RefreshLease(ref _spellLevel, ref _spellLevelRetryFrame, frame,
            suiteReady && config.CanStartAutoBuyActively && config.AutoBuy.AutoLevelSpells,
            "SpellLevel", "Automata Spell Leveling", SpellLevelFamilies);
        RefreshLease(ref _harvest, ref _harvestRetryFrame, frame,
            suiteReady && config.CanStartAutoHarvestActively &&
                (config.AutoHarvest.CollectFruitTrees || config.AutoHarvest.CollectTreasureTrees),
            "AutoHarvest", "Automata Auto Harvest", HarvestFamilies);
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
            ItemFamilies,
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
            ScribeFamilies,
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
        AutomationActionFamily[] families)
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
            families,
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
        AutomationActionFamily[] families,
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
                families,
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
    private static AutomationActionFamily[] GameMcpFamilies(
        GameMcpCommandKind kind,
        string mode) => kind switch
    {
        GameMcpCommandKind.Purchase when mode == "structure" => StructureFamilies,
        GameMcpCommandKind.Purchase when mode == "upgrade" => new[]
        {
            AutomationActionFamily.UpgradePurchase,
            AutomationActionFamily.NativeMultiBuyOverride,
        },
        GameMcpCommandKind.Cast => CastFamilies,
        GameMcpCommandKind.Concept => ConceptFamilies,
        GameMcpCommandKind.SpellLevel => SpellLevelFamilies,
        GameMcpCommandKind.Harvest => HarvestFamilies,
        GameMcpCommandKind.DiscoveryTreeOffer => DiscoveryTreeOfferFamilies,
        GameMcpCommandKind.SpellWorkbench => SpellWorkbenchFamilies,
        GameMcpCommandKind.SpellComposition => SpellCompositionFamilies,
        GameMcpCommandKind.SpellLoadout => SpellLoadoutFamilies,
        GameMcpCommandKind.Targeting => TargetingFamilies,
        GameMcpCommandKind.Consumable => ConsumableFamilies,
        GameMcpCommandKind.Crafting => ScribeFamilies,
        GameMcpCommandKind.GenericDiscovery => GenericDiscoveryFamilies,
        GameMcpCommandKind.EquipmentLoadout => EquipmentLoadoutFamilies,
        GameMcpCommandKind.Challenge => ChallengeFamilies,
        GameMcpCommandKind.Prestige => PrestigeFamilies,
        GameMcpCommandKind.Research => ResearchFamilies,
        GameMcpCommandKind.AlchemyLoadout => AlchemyLoadoutFamilies,
        GameMcpCommandKind.RitualLifecycle => RitualLifecycleFamilies,
        GameMcpCommandKind.GenericLevel => GenericLevelFamilies,
        GameMcpCommandKind.CraftingStation => ScribeFamilies,
        GameMcpCommandKind.Loadout => PlayerLoadoutFamilies,
        GameMcpCommandKind.HarvestLifecycle => HarvestLifecycleFamilies,
        GameMcpCommandKind.StructureLifecycle => StructureLifecycleFamilies,
        GameMcpCommandKind.ReturnToMenu => RunTransitionFamilies,
        _ => Array.Empty<AutomationActionFamily>(),
    };

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

    private bool PermanentlyOwns(AutomationActionFamily family) => family switch
    {
        AutomationActionFamily.StructurePurchase => _structures?.IsHeld == true,
        AutomationActionFamily.UpgradePurchase => _upgrades?.IsHeld == true,
        AutomationActionFamily.NativeMultiBuyOverride => _multiBuy?.IsHeld == true,
        AutomationActionFamily.SpellCast => _cast?.IsHeld == true,
        AutomationActionFamily.ConceptAssignment => _concept?.IsHeld == true,
        AutomationActionFamily.SpellLevelPurchase => _spellLevel?.IsHeld == true,
        AutomationActionFamily.HarvestAction => _harvest?.IsHeld == true,
        AutomationActionFamily.ConsumableUse => _items?.IsHeld == true,
        AutomationActionFamily.CraftingQueueSubmission => _scribe?.IsHeld == true,
        AutomationActionFamily.DiscoveryTreeOfferLifecycle => false,
        AutomationActionFamily.SpellWorkbenchLifecycle => false,
        AutomationActionFamily.SpellComposition => false,
        AutomationActionFamily.SpellLoadout => false,
        AutomationActionFamily.Targeting => false,
        AutomationActionFamily.GenericDiscovery => false,
        AutomationActionFamily.EquipmentLoadout => false,
        AutomationActionFamily.ChallengeLifecycle => false,
        AutomationActionFamily.PrestigeLifecycle => false,
        AutomationActionFamily.ResearchLifecycle => false,
        AutomationActionFamily.AlchemyLoadout => false,
        AutomationActionFamily.RitualLifecycle => false,
        AutomationActionFamily.GenericLevel => false,
        AutomationActionFamily.PlayerLoadout => false,
        AutomationActionFamily.HarvestLifecycle => false,
        AutomationActionFamily.StructureLifecycle => false,
        AutomationActionFamily.RunTransition => false,
        _ => false,
    };

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

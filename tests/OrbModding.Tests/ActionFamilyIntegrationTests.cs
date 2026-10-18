using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbMentor;
using OrbModding.Common;
using Xunit;

namespace OrbModding.Tests;

public sealed class ActionFamilyIntegrationTests
{
    [Fact]
    public void ExactAutobuyOrbGuidBlocksOnlyOverlappingAutomataFamilies()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        config.AutoConceptMode.Value = AutoConceptOperationMode.Active;
        config.AutoLevelSpells.Value = true;
        config.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure));
        Assert.False(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
        Assert.True(ownership.OwnsCast);
        Assert.True(ownership.OwnsConcept);
        Assert.True(ownership.OwnsSpellLevel);
        Assert.True(ownership.OwnsHarvest);
    }

    [Fact]
    public void SimilarUnknownGuidDoesNotClaimKnownExternalFamilies()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.RefreshLoadedPluginInventory(1, guid => guid == "IngoH.OrbOfCreation.AutoBuyOrb.Fork");
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Structure));
        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
    }

    [Fact]
    public void UnselectedAutoBuyKindsCountAsSatisfiedRuntimeOwnership()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = false;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.Equal(
            AutoBuyCandidateKinds.Upgrades,
            ownership.EffectiveAutoBuyOwnership(config.Current.AutoBuy));
    }

    [Fact]
    public void PersistentKnownConflictRetriesClaimsOnlyAtBoundedIntervals()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyStructures.Value = true;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.RefreshLoadedPluginInventory(
            1,
            guid => guid == AutomataActionFamilyOwnership.KnownAutoBuyPluginGuid);
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 0);
        var initialAttempts = ownership.ClaimAttempts;

        for (var frame = 1; frame < 60; frame++)
            ownership.Refresh(config.Current, lifecycleReady: true, frame);

        Assert.Equal(initialAttempts, ownership.ClaimAttempts);
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 60);
        Assert.Equal(initialAttempts + 2, ownership.ClaimAttempts);
    }

    [Fact]
    public void AutomataConfigurationDisableReleasesClaims()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoCastMode.Value = AutoCastOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);
        Assert.True(ownership.OwnsCast);

        config.Enabled.Value = false;
        ownership.Refresh(config.Current, lifecycleReady: true);

        using var replacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-cast"),
            AutomationActionFamily.SpellCast);
        Assert.True(replacement.IsHeld);
    }

    [Fact]
    public void HarvestClaimIsRevokedBeforeAnotherNativeTransactionCanStart()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.True(ownership.OwnsHarvest);
        Assert.True(ownership.Family(AutomataActionFamilyOwnership.Harvest).TryCapture());

        using var external = registry.RegisterKnownExternal(
            new ActionFamilyOwner(
                new FeatureStatusKey("tests.external", "Harvest"),
                "External Harvest"),
            new[] { AutomationActionFamily.HarvestAction });

        Assert.False(ownership.OwnsHarvest);
        Assert.False(ownership.Family(AutomataActionFamilyOwnership.Harvest).TryCapture());
    }

    [Fact]
    public void AutoItemsClaimFailurePreservesTheExactConflictingFamilyAndOwner()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        using var external = registry.RegisterKnownExternal(
            new ActionFamilyOwner(
                new FeatureStatusKey("tests.external", "Items"),
                "External Items"),
            new[] { AutomationActionFamily.ConsumableUse });
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.OwnsItems);
        Assert.Contains("ConsumableUse", ownership.Family(AutomataActionFamilyOwnership.Items).Failure());
        Assert.Contains("External Items", ownership.Family(AutomataActionFamilyOwnership.Items).Failure());
    }

    [Fact]
    public void AutoItemsMasterDisableReleasesItsLeaseEvenWhenAutoBuyKeepsMultiBuyAlive()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoBuyUpgrades.Value = true;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);
        Assert.True(ownership.OwnsItems);

        config.AutoItemsMode.Value = AutoItemsOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.OwnsItems);
        Assert.False(ownership.Family(AutomataActionFamilyOwnership.Items).TryCapture());
        Assert.Contains(
            "Committed configuration no longer enables Auto Items",
            ownership.Family(AutomataActionFamilyOwnership.Items).Failure());
        using var replacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-items"),
            AutomationActionFamily.ConsumableUse);
        Assert.True(replacement.IsHeld);
    }

    [Fact]
    public void AutoScribeClaimRequiresScrollConsumptionAndReleasesOnMasterDisable()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        config.AutoScribeMode.Value = AutoScribeOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.True(ownership.OwnsScribe);
        Assert.True(ownership.Family(AutomataActionFamilyOwnership.Scribe).TryCapture());

        config.AutoScribeMode.Value = AutoScribeOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.OwnsScribe);
        Assert.False(ownership.Family(AutomataActionFamilyOwnership.Scribe).TryCapture());
        Assert.Contains(
            "Committed configuration no longer enables Auto Scribe",
            ownership.Family(AutomataActionFamilyOwnership.Scribe).Failure());
        using var replacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-scribe"),
            AutomationActionFamily.CraftingQueueSubmission);
        Assert.True(replacement.IsHeld);
    }

    [Fact]
    public void A_feature_lease_revoked_before_the_next_refresh_refuses_with_its_family_sentence()
    {
        var harvestRegistry = new ActionFamilyOwnershipRegistry();
        var harvestConfig = BepInExAutomataConfiguration.Bind(new ConfigFile());
        harvestConfig.AutoHarvestMode.Value = AutoHarvestOperationMode.Active;
        using var harvest = new AutomataActionFamilyOwnership(harvestRegistry);
        harvest.Refresh(harvestConfig.Current, lifecycleReady: true);
        using var harvestExternal = External(harvestRegistry, AutomationActionFamily.HarvestAction);
        Assert.False(harvest.Family(AutomataActionFamilyOwnership.Harvest).TryCapture());
        Assert.Equal(
            "The current MCP operation does not hold HarvestAction.",
            harvest.Family(AutomataActionFamilyOwnership.Harvest).Failure());

        var itemsRegistry = new ActionFamilyOwnershipRegistry();
        var itemsConfig = BepInExAutomataConfiguration.Bind(new ConfigFile());
        itemsConfig.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        using var items = new AutomataActionFamilyOwnership(itemsRegistry);
        items.Refresh(itemsConfig.Current, lifecycleReady: true);
        Assert.True(items.Family(AutomataActionFamilyOwnership.Items).TryCapture());
        using var itemsExternal = External(itemsRegistry, AutomationActionFamily.ConsumableUse);
        Assert.False(items.Family(AutomataActionFamilyOwnership.Items).TryCapture());
        Assert.Equal(
            "Auto Items does not hold ConsumableUse and NativeMultiBuyOverride.",
            items.Family(AutomataActionFamilyOwnership.Items).Failure());

        var scribeRegistry = new ActionFamilyOwnershipRegistry();
        var scribeConfig = BepInExAutomataConfiguration.Bind(new ConfigFile());
        scribeConfig.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        scribeConfig.AutoScribeMode.Value = AutoScribeOperationMode.Active;
        using var scribe = new AutomataActionFamilyOwnership(scribeRegistry);
        scribe.Refresh(scribeConfig.Current, lifecycleReady: true);
        Assert.True(scribe.Family(AutomataActionFamilyOwnership.Scribe).TryCapture());
        using var scribeExternal = External(scribeRegistry, AutomationActionFamily.CraftingQueueSubmission);
        Assert.False(scribe.Family(AutomataActionFamilyOwnership.Scribe).TryCapture());
        Assert.Equal(
            "Auto Scribe does not hold CraftingQueueSubmission.",
            scribe.Family(AutomataActionFamilyOwnership.Scribe).Failure());
    }

    [Fact]
    public void Auto_items_names_its_own_claim_failure_before_the_multi_buy_one()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var consumableExternal = External(registry, AutomationActionFamily.ConsumableUse);
        using var multiBuyExternal = External(registry, AutomationActionFamily.NativeMultiBuyOverride);
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);

        ownership.Refresh(config.Current, lifecycleReady: true);
        Assert.Equal(
            "Automata Auto Items could not claim ConsumableUse; External ConsumableUse currently owns it.",
            ownership.Family(AutomataActionFamilyOwnership.Items).Failure());

        consumableExternal.Dispose();
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 60);
        Assert.False(ownership.Family(AutomataActionFamilyOwnership.Items).TryCapture());
        Assert.Equal(
            "Automata Native Multi-Buy Scope could not claim NativeMultiBuyOverride; " +
            "External NativeMultiBuyOverride currently owns it.",
            ownership.Family(AutomataActionFamilyOwnership.Items).Failure());
    }

    private static IDisposable External(
        ActionFamilyOwnershipRegistry registry,
        AutomationActionFamily family) =>
        registry.RegisterKnownExternal(
            new ActionFamilyOwner(
                new FeatureStatusKey("tests.external", family.ToString()),
                "External " + family),
            new[] { family });

    [Fact]
    public void MentorDomainClaimsAreIndependentAndReleaseOnLifecycleTeardown()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = MentorConfig.Bind(new ConfigFile());
        config.Mode.Value = MentorOperationMode.Active;
        config.ArtifactsEnabled.Value = true;
        config.AlchemyEnabled.Value = true;
        using var ownership = new MentorActionFamilyOwnership(registry);
        ownership.Refresh(config, lifecycleReady: true, frame: 1);

        using var artifactExternal = registry.RegisterKnownExternal(
            new ActionFamilyOwner(new FeatureStatusKey("external", "artifacts"), "External artifacts"),
            new[] { AutomationActionFamily.ArtifactMasteryExperienceGrant });
        ownership.Refresh(config, lifecycleReady: true, frame: 2);

        Assert.True(ownership.IsHeld(MentorDomain.Spells));
        Assert.False(ownership.IsHeld(MentorDomain.Artifacts));
        Assert.True(ownership.IsHeld(MentorDomain.Alchemy));

        ownership.ReleaseLifecycleClaims();
        using var spellReplacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-spells"),
            AutomationActionFamily.SpellMasteryExperienceGrant);
        using var alchemyReplacement = Claim(
            registry,
            new FeatureStatusKey("tests", "replacement-alchemy"),
            AutomationActionFamily.AlchemyMasteryExperienceGrant);
        Assert.True(spellReplacement.IsHeld);
        Assert.True(alchemyReplacement.IsHeld);
    }

    private static ActionFamilyLeaseSet Claim(
        ActionFamilyOwnershipRegistry registry,
        FeatureStatusKey key,
        AutomationActionFamily family)
    {
        Assert.True(registry.TryClaimSet(
            new ActionFamilyOwner(key, key.FeatureId),
            new[] { family },
            out var lease,
            out _));
        return lease!;
    }
}

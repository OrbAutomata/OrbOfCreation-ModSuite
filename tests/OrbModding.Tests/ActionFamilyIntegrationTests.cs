using BepInEx.Configuration;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using OrbAutomata;
using OrbMentor;
using OrbModding.Common;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
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
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.SpellCast));
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.ConceptAssignment));
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.SpellLevelPurchase));
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.HarvestAction));
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
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.SpellCast));

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

        Assert.True(ownership.OwnsFamily(AutomationActionFamily.HarvestAction));
        Assert.True(ownership.Family(AutoHarvestFeatureContribution.ActionFamily).TryCapture());

        using var external = registry.RegisterKnownExternal(
            new ActionFamilyOwner(
                new FeatureStatusKey("tests.external", "Harvest"),
                "External Harvest"),
            new[] { AutomationActionFamily.HarvestAction });

        Assert.False(ownership.OwnsFamily(AutomationActionFamily.HarvestAction));
        Assert.False(ownership.Family(AutoHarvestFeatureContribution.ActionFamily).TryCapture());
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

        Assert.False(ownership.Owns(AutoItemsFeatureContribution.ActionFamily));
        Assert.Contains("ConsumableUse", ownership.Family(AutoItemsFeatureContribution.ActionFamily).Failure());
        Assert.Contains("External Items", ownership.Family(AutoItemsFeatureContribution.ActionFamily).Failure());
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
        Assert.True(ownership.Owns(AutoItemsFeatureContribution.ActionFamily));

        config.AutoItemsMode.Value = AutoItemsOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.Owns(AutoItemsFeatureContribution.ActionFamily));
        Assert.False(ownership.Family(AutoItemsFeatureContribution.ActionFamily).TryCapture());
        Assert.Contains(
            "Committed configuration no longer enables Auto Items",
            ownership.Family(AutoItemsFeatureContribution.ActionFamily).Failure());
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

        Assert.True(ownership.OwnsPermanently(AutomationActionFamily.CraftingQueueSubmission));
        Assert.True(ownership.Family(AutoScribeFeatureContribution.ActionFamily).TryCapture());

        config.AutoScribeMode.Value = AutoScribeOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true);

        Assert.False(ownership.OwnsPermanently(AutomationActionFamily.CraftingQueueSubmission));
        Assert.False(ownership.Family(AutoScribeFeatureContribution.ActionFamily).TryCapture());
        Assert.Contains(
            "Committed configuration no longer enables Auto Scribe",
            ownership.Family(AutoScribeFeatureContribution.ActionFamily).Failure());
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
        Assert.False(harvest.Family(AutoHarvestFeatureContribution.ActionFamily).TryCapture());
        Assert.Equal(
            "The current MCP operation does not hold HarvestAction.",
            harvest.Family(AutoHarvestFeatureContribution.ActionFamily).Failure());

        var itemsRegistry = new ActionFamilyOwnershipRegistry();
        var itemsConfig = BepInExAutomataConfiguration.Bind(new ConfigFile());
        itemsConfig.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        using var items = new AutomataActionFamilyOwnership(itemsRegistry);
        items.Refresh(itemsConfig.Current, lifecycleReady: true);
        Assert.True(items.Family(AutoItemsFeatureContribution.ActionFamily).TryCapture());
        using var itemsExternal = External(itemsRegistry, AutomationActionFamily.ConsumableUse);
        Assert.False(items.Family(AutoItemsFeatureContribution.ActionFamily).TryCapture());
        Assert.Equal(
            "Auto Items does not hold ConsumableUse and NativeMultiBuyOverride.",
            items.Family(AutoItemsFeatureContribution.ActionFamily).Failure());

        var scribeRegistry = new ActionFamilyOwnershipRegistry();
        var scribeConfig = BepInExAutomataConfiguration.Bind(new ConfigFile());
        scribeConfig.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        scribeConfig.AutoScribeMode.Value = AutoScribeOperationMode.Active;
        using var scribe = new AutomataActionFamilyOwnership(scribeRegistry);
        scribe.Refresh(scribeConfig.Current, lifecycleReady: true);
        Assert.True(scribe.Family(AutoScribeFeatureContribution.ActionFamily).TryCapture());
        using var scribeExternal = External(scribeRegistry, AutomationActionFamily.CraftingQueueSubmission);
        Assert.False(scribe.Family(AutoScribeFeatureContribution.ActionFamily).TryCapture());
        Assert.Equal(
            "Auto Scribe does not hold CraftingQueueSubmission.",
            scribe.Family(AutoScribeFeatureContribution.ActionFamily).Failure());
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
            ownership.Family(AutoItemsFeatureContribution.ActionFamily).Failure());

        consumableExternal.Dispose();
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 60);
        Assert.False(ownership.Family(AutoItemsFeatureContribution.ActionFamily).TryCapture());
        Assert.Equal(
            "Automata Native Multi-Buy Scope could not claim NativeMultiBuyOverride; " +
            "External NativeMultiBuyOverride currently owns it.",
            ownership.Family(AutoItemsFeatureContribution.ActionFamily).Failure());
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
        using var ownership = new AutomataActionFamilyOwnership(registry);
        var configuration = new SuiteRuntimeConfiguration
        {
            General = new SuiteGeneralConfiguration { Enabled = true },
            Mentor = MentorConfiguration.Read(config),
        };
        ownership.Refresh(configuration, lifecycleReady: true, frame: 1);

        using var artifactExternal = registry.RegisterKnownExternal(
            new ActionFamilyOwner(new FeatureStatusKey("external", "artifacts"), "External artifacts"),
            new[] { AutomationActionFamily.ArtifactMasteryExperienceGrant });
        ownership.Refresh(configuration, lifecycleReady: true, frame: 2);

        Assert.True(ownership.OwnsFamily(AutomationActionFamily.SpellMasteryExperienceGrant));
        Assert.False(ownership.OwnsFamily(AutomationActionFamily.ArtifactMasteryExperienceGrant));
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.AlchemyMasteryExperienceGrant));

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

    [Fact]
    public void SharedMultiBuyRemainsHeldUntilBothBuyAndItemsStopDemandingIt()
    {
        var registry = new ActionFamilyOwnershipRegistry();
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        using var ownership = new AutomataActionFamilyOwnership(registry);
        ownership.Refresh(config.Current, lifecycleReady: true);
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.NativeMultiBuyOverride));

        config.AutoBuyMode.Value = AutoBuyOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 1);
        Assert.True(ownership.Owns(AutoItemsFeatureContribution.ActionFamily));
        Assert.True(ownership.OwnsFamily(AutomationActionFamily.NativeMultiBuyOverride));

        config.AutoBuyMode.Value = AutoBuyOperationMode.Active;
        config.AutoItemsMode.Value = AutoItemsOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 2);
        Assert.True(ownership.OwnsAutoBuy(AutoBuyCandidateKind.Upgrade));
        Assert.False(ownership.OwnsFamily(AutomationActionFamily.ConsumableUse));

        config.AutoBuyMode.Value = AutoBuyOperationMode.Disabled;
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 3);
        Assert.False(ownership.OwnsFamily(AutomationActionFamily.NativeMultiBuyOverride));
        using var replacement = Claim(registry, new FeatureStatusKey("tests", "multi-buy"),
            AutomationActionFamily.NativeMultiBuyOverride);
        Assert.True(replacement.IsHeld);
    }

    [Fact]
    public void TemporaryItemApprovalAloneEnablesItemsStatusAndOwnership()
    {
        var config = BepInExAutomataConfiguration.Bind(new ConfigFile());
        config.AutoBuyMode.Value = AutoBuyOperationMode.Disabled;
        config.AutoItemsMode.Value = AutoItemsOperationMode.Active;
        config.AutoItemsUseScrolls.Value = false;
        config.AutoItemsUseRelics.Value = false;
        config.AutoItemsTemporaryItemAllowlist.Value = Guid.NewGuid().ToString();
        using var statuses = new AutomataFeatureStatuses(config.Current, 1, new FeatureStatusRegistry());
        using var ownership = new AutomataActionFamilyOwnership(new ActionFamilyOwnershipRegistry());

        ownership.Refresh(config.Current, lifecycleReady: true);

        var status = statuses.Get(AutoItemsFeatureContribution.FeatureId).Current;
        Assert.True(status.ConfiguredEnabled);
        Assert.Equal(FeatureStatusReasonCode.GameplayNotReady, status.Reason.Code);
        Assert.True(ownership.Owns(AutoItemsFeatureContribution.ActionFamily));
        Assert.True(ownership.Family(AutoItemsFeatureContribution.ActionFamily).TryCapture());

        config.AutoItemsTemporaryItemAllowlist.Value = string.Empty;
        ownership.Refresh(config.Current, lifecycleReady: true, frame: 1);
        statuses.ObserveConfiguration(config.Current, new ConfigGeneration(2));
        Assert.True(statuses.Get(AutoItemsFeatureContribution.FeatureId).Current.ConfiguredEnabled);
        Assert.Equal(FeatureStatusReasonCode.ConfigurationDisabled,
            statuses.Get(AutoItemsFeatureContribution.FeatureId).Current.Reason.Code);
        Assert.False(ownership.Owns(AutoItemsFeatureContribution.ActionFamily));
        Assert.False(ownership.OwnsFamily(AutomationActionFamily.NativeMultiBuyOverride));
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

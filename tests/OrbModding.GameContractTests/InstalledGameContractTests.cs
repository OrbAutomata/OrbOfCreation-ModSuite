using System;
using System.Linq;
using Xunit;

namespace OrbModding.GameContractTests;

public sealed class InstalledGameContractTests
{
    [GameAssemblyFact]
    public void PlayerAndSaveHooks_MatchRuntimeContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertMethod(assembly, "Player", "ManagerStart", false, "System.Void");
        AssertMethod(assembly, "Player", "GetAchievementLevel", true, "IntVariable");
        AssertMethod(assembly, "GameManager", "InitGame", false, "System.Void");
        AssertMethod(assembly, "GameManager", "ResetGameState", true, "System.Void");
        AssertMethod(assembly, "PersistentResetManager", "PersistentResetLogic", false, "System.Void");
        AssertMethod(assembly, "SaveStateManager", "CollectJsonData", false, "System.String");
        AssertMethod(assembly, "SaveStateManager", "ImplementLoadedJson", false, "System.Void");
        AssertMethod(assembly, "SaveStateManager", "StartGame", false, "System.Void");
        AssertMethod(
            assembly,
            "SaveStateManager",
            "WriteFileAndBackupAsync",
            false,
            "System.Threading.Tasks.Task",
            "System.String",
            "System.String",
            "System.String");
    }

    [GameAssemblyFact]
    public void ResearchAutomation_MatchesReadAndActionContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertMethod(assembly, "ResearchSO", "CanDevelop", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "GetDevelopError", false, "System.String");
        AssertMethod(assembly, "ResearchSO", "GetDevelopmentCost", false, "ResourceCostList");
        AssertMethod(assembly, "ResearchSO", "Develop", false, "System.Void");
        AssertMethod(assembly, "ResearchSO", "IsVisible", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "IsComplete", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "IsDeveloping", false, "System.Boolean");
        AssertMethod(assembly, "ResearchSO", "GetQueuedLevels", false, "System.Int32");
        AssertMethod(assembly, "ResourceCostList", "GetEntries", false, "System.Collections.Generic.List`1<ResourceTuple>");
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
    }

    [GameAssemblyFact]
    public void AutoBuy_MatchesNativePurchaseContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.Collections.Generic.List`1<StructureSO>", assembly.GetFieldType("StructureSO", "All"));
        AssertMethod(assembly, "StructureSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "StructureSO", "CanPurchase", false, "System.Boolean");
        AssertMethod(assembly, "StructureSO", "GetPurchaseCost", false, "ResourceCostList");
        AssertMethod(assembly, "StructureSO", "Purchase", false, "System.Void", "System.Boolean");
        AssertMethod(assembly, "StructureSO", "GetPurchaseLevel", false, "System.Int32");
        AssertMethod(assembly, "StructureSO", "GetQueuedQuantity", false, "System.Int32");
        AssertMethod(assembly, "StructureSO", "QueueBuild", false, "System.Void", "System.Int32");

        Assert.Equal("System.Collections.Generic.List`1<UpgradeSO>", assembly.GetFieldType("UpgradeSO", "All"));
        AssertMethod(assembly, "UpgradeSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "CanPurchase", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseCost", false, "ResourceCostList");
        AssertMethod(assembly, "UpgradeSO", "GetLeveledCostList", false, "ResourceCostList", "System.Int32");
        AssertMethod(assembly, "UpgradeSO", "Purchase", false, "System.Void");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseLevel", false, "System.Int32");
        AssertMethod(assembly, "UpgradeSO", "GetQueuedPurchaseLevel", false, "System.Int32");
        AssertMethod(assembly, "UpgradeSO", "HasFiniteLevels", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "IsMaxLevel", false, "System.Boolean");
        AssertMethod(assembly, "UpgradeSO", "IsMaxQueuedLevel", false, "System.Boolean");

        Assert.Equal("ActionManager", assembly.GetFieldType("ActionManager", "instance"));
        Assert.Equal("ActionableListVariable", assembly.GetFieldType("ActionManager", "actionableItems"));
        Assert.Equal("IntVariable", assembly.GetFieldType("ActionableListVariable", "maxQueuedItems"));
        AssertMethod(assembly, "ActionManager", "GetRemainingRoom", true, "System.Int32");
        AssertMethod(assembly, "GlobalVariables", "GetMultiBuy", true, "IntVariable");
        AssertMethod(assembly, "Player", "GetBulkDevelopment", true, "IntVariable");
        AssertMethod(assembly, "IntVariable", "AsInt", false, "System.Int32");
        AssertMethod(assembly, "IntVariable", "SetValue", false, "System.Void", "System.Int32");
        Assert.Equal("System.Collections.Generic.List`1<ResourceTuple>", assembly.GetFieldType("ResourceCostList", "costs"));
        AssertMethod(assembly, "ResourceCostList", "GetEntries", false, "System.Collections.Generic.List`1<ResourceTuple>");
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetQuantity", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueSpend", false, "BigDouble", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetAttributeCostMod", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "ResourceSO", "IsBandwidthResource", false, "System.Boolean");
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "TooltipableObject", "GetName", false, "System.String");
        Assert.Equal("ValueModifierRecord", assembly.GetFieldType("ResourceSO", "quality"));
        Assert.Equal("ValueModifierRecord", assembly.GetFieldType("ResourceSO", "maxQuantity"));
        AssertMethod(assembly, "ValueModifierRecord", "GetValue", false, "BigDouble");
    }

    [GameAssemblyFact]
    public void PlayerFacingAttributes_AreStructurePurchaseRows()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(0x06002763, assembly.GetMethodToken(
            "UIStructureList", "PurchaseStructure", "StructureSO"));
        Assert.Equal(
            0x06001783,
            assembly.GetMethodToken("StructureSO", "Purchase", Array.Empty<string>()));
        AssertMethod(
            assembly,
            "UIStructureList",
            "PurchaseStructure",
            false,
            "System.Void",
            "StructureSO");
        AssertMethod(assembly, "StructureSO", "Purchase", false, "System.Void");
        Assert.Contains(
            assembly.GetMethodBodyDefinitionReferences(
                "UIStructureList", "PurchaseStructure", "StructureSO"),
            reference => reference.DeclaringType == "StructureSO" &&
                reference.MemberName == "Purchase");
        Assert.Empty(assembly.GetMethods("AttributeSO", "Purchase"));
    }

    [GameAssemblyFact]
    public void AutoBuyAuthoredRouteAdmission_MatchesCompleteNativeBindingSet()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("TooltipableObject", assembly.GetBaseType("AbstractListVariable"));
        Assert.Equal(
            "System.Collections.Generic.List`1<AbstractListVariable>",
            assembly.GetFieldType("ViewSO", "relevantLists"));
        Assert.Equal(
            "System.Collections.Generic.List`1<AbstractListVariable>",
            assembly.GetFieldType("ViewSO", "availableLists"));
        Assert.Equal("StructureTypeSO", assembly.GetFieldType("StructureSO", "structureType"));
        Assert.Equal(
            "System.Collections.Generic.List`1<StructureSO>",
            assembly.GetFieldType("StructureTypeSO", "structures"));
        Assert.Equal(
            "GenericListVariable`1<StructureSO>",
            assembly.GetBaseType("StructureListVariable"));
        Assert.Equal(
            "GenericListVariable`1<UpgradeSO>",
            assembly.GetBaseType("UpgradeListVariable"));
        Assert.Equal(
            "System.Collections.Generic.List`1<!0>",
            assembly.GetFieldType("AbstractListVariable`1", "value"));
        AssertMethod(assembly, "ViewSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "StructureSO", "IsAvailable", false, "System.Boolean");

        Assert.Equal(
            "System.Collections.Generic.List`1<ViewListVariable+ListTuple>",
            assembly.GetFieldType("UpgradeSO", "viewListAdditions"));
        Assert.Equal("GenericListVariable`1<ViewSO>", assembly.GetBaseType("ViewListVariable"));
        Assert.Equal(
            "GenericListVariable`1+AdditionTuple`1<ViewSO,ViewListVariable>",
            assembly.GetBaseType("ViewListVariable+ListTuple"));
        Assert.Equal("!1", assembly.GetFieldType("GenericListVariable`1+AdditionTuple`1", "list"));
        Assert.Equal("!0", assembly.GetFieldType("GenericListVariable`1+AdditionTuple`1", "element"));
        Assert.Equal("IntVariable", assembly.GetFieldType("AbstractListVariable`1", "maxSizeVariable"));
        AssertMethod(assembly, "GenericListVariable`1", "HasEmptySpot", false, "System.Boolean");
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
    }

    /// <summary>
    /// Reaching an authored upgrade panel that no view names, and reading who is on it.
    /// </summary>
    /// <remarks>
    /// Three of the game's nine <c>UpgradeListVariable</c> assets are named only by prefab swapper
    /// data, so the identity registry is the one door to them: the pinned uuid goes in, an
    /// <c>UpgradeListVariable</c> has to come out, and its <c>value</c> has to be the upgrades it
    /// holds. Every member of that path is asserted here against the audited copy, because a screen
    /// word derived through a path the build no longer has would be a confident lie rather than a
    /// missing cell.
    /// </remarks>
    [GameAssemblyFact]
    public void UpgradeScreenMembership_MatchesPinnedListResolutionContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2<System.Guid,IdScriptableObject>",
            assembly.GetFieldType("IdScriptableObject", "RuntimeLookup"));
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        Assert.True(assembly.HasType("UpgradeListVariable"));
        Assert.Equal(
            "GenericListVariable`1<UpgradeSO>",
            assembly.GetBaseType("UpgradeListVariable"));
        Assert.Equal(
            "System.Collections.Generic.List`1<!0>",
            assembly.GetFieldType("AbstractListVariable`1", "value"));

        // The panel the lists are swapped into, and the swap that makes membership a screen fact.
        // Nothing reads these — the map they walk is prefab data — but a build that stopped
        // grouping upgrades this way would make the pinned words describe a UI that is gone.
        Assert.Equal(
            "ListViewSwapper`1<UpgradeListVariable>",
            assembly.GetFieldType("UIUpgradeList", "listViews"));
        AssertMethod(
            assembly, "ListViewSwapper`1", "GetActiveList", false, "!0");
    }

    [GameAssemblyFact]
    public void AutoCast_MatchesNativeLoadoutCastResourceAndTargetContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("SpellManager", assembly.GetFieldType("SpellManager", "instance"));
        Assert.Equal("SpellListVariable", assembly.GetFieldType("SpellManager", "activeSpells"));
        AssertMethod(assembly, "SpellManager", "FireSpellIndex", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "SpellManager", "CanCastASpell", true, "System.Boolean");
        AssertMethod(assembly, "SpellManager", "AddSpell", false, "System.Void", "Spell");
        AssertMethod(assembly, "SpellManager", "RemoveSpell", false, "System.Void", "Spell");
        AssertMethod(assembly, "SpellManager", "MoveSpell", false, "System.Void", "Spell");

        AssertMethod(assembly, "Spell", "Fire", false, "System.Void");
        AssertMethod(assembly, "Spell", "CanCast", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "CanFire", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsEmpty", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsCasting", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsChanneled", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsToggledSpell", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "CanCharge", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "SetChargeInput", false, "System.Void", "System.String", "System.Boolean");
        AssertMethod(assembly, "Spell", "IsAttuning", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "IsChargeAvailable", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "GetCurrSpellCharges", false, "System.Int32");
        AssertMethod(assembly, "Spell", "GetMaxSpellCharges", false, "System.Int32");
        AssertMethod(assembly, "Spell", "GetCooldownTimeRemaining", false, "BigDouble");
        AssertMethod(assembly, "Spell", "HasEnoughResources", false, "System.Boolean");
        AssertMethod(assembly, "Spell", "GetCost", false, "ResourceCostList");
        AssertMethod(assembly, "Spell", "GetDrainCost", false, "ResourceCostList");
        AssertMethod(assembly, "Spell", "GetScalingInfo", false, "ScalingInfo");
        Assert.Equal("IdObjectRef`1<SpellRecipeSO>", assembly.GetFieldType("Spell", "referenceObj"));
        AssertMethod(assembly, "Spell", "get_reference", false, "SpellRecipeSO");

        Assert.Equal("SpellRecipeSO+CastType", assembly.GetFieldType("SpellRecipeSO", "castType"));
        Assert.Equal("System.Collections.Generic.List`1<InstantEffectBlock>", assembly.GetFieldType("SpellRecipeSO", "onCastEffects"));
        Assert.Equal("System.Collections.Generic.List`1<PersistentEffectBlock>", assembly.GetFieldType("SpellRecipeSO", "toggledEffects"));
        Assert.Equal("Targeting.TargetSelectOptions", assembly.GetFieldType("RequestTargetEffectScript", "targetOptions"));
        AssertMethod(assembly, "Targeting.TargetSelectOptions", "HasValidTargetsLeft", false, "System.Boolean", "ScalingInfo");
        AssertMethod(assembly, "Targeting.TargetSelectOptions", "GetRandom", false, "Targeting.ITargetable", "ScalingInfo");
        AssertMethod(assembly, "TargetingManager", "IsTargeting", true, "System.Boolean");
        AssertMethod(assembly, "TargetingManager", "GetTargetingLink", true, "TargetingManager+TargetLink");
        AssertMethod(assembly, "TargetingManager", "SubmitTarget", true, "System.Void", "Targeting.ITargetable");
        AssertMethod(assembly, "TargetingManager+TargetLink", "GetRandom", false, "Targeting.ITargetable");

        Assert.Equal("BoolVariable", assembly.GetFieldType("AutoBuyManager", "autoBuyEnabled"));
        Assert.Equal("BoolVariable", assembly.GetFieldType("UIToggleButton", "isOnVariable"));
        Assert.Equal("UnityEngine.UI.Image", assembly.GetFieldType("UIToggleButton", "iconImage"));
        Assert.Equal("TMPro.TextMeshProUGUI", assembly.GetFieldType("UIToggleButton", "textElement"));
        Assert.Equal("UnityEngine.Sprite", assembly.GetFieldType("UIToggleButton", "onButtonSprite"));
        AssertMethod(assembly, "UIToggleButton", "SetState", false, "System.Void", "System.Boolean");
        AssertMethod(assembly, "TooltipNode", ".ctor", false, "System.Void", "System.String", "UnityEngine.Color");
        AssertMethod(
            assembly,
            "UIPopupText",
            "CreateOn",
            true,
            "UIPopupText",
            "System.Collections.Generic.List`1<TooltipNode>",
            "UnityEngine.RectTransform",
            "UnityEngine.Vector2");
    }

    [GameAssemblyFact]
    public void AutoSpellLevel_MatchesNativePrerequisiteCostAndCapabilityContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("SpellRecipeListVariable", assembly.GetFieldType("SpellManager", "availableSpellRecipes"));
        AssertMethod(assembly, "SpellManager", "TryLevelAllSpells", false, "System.Void");
        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("SpellRecipeSO", "levelingPrerequisites"));
        AssertMethod(assembly, "Prerequisites+Container", "Check", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "GetLevelCost", false, "ResourceCostList");
        AssertMethod(assembly, "SpellRecipeSO", "IsReadyToLevelMastery", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "PurchaseLevel", false, "System.Void");
        AssertMethod(assembly, "ResourceCostList", "HasEnough", false, "System.Boolean");
        AssertMethod(assembly, "ResourceCostList", "PerformCost", false, "System.Void");
        AssertMethod(assembly, "UpgradeSO", "GetPurchaseLevel", false, "System.Int32");
    }

    [GameAssemblyFact]
    public void AutoConcept_MatchesPublishedWorldAndActionBoundaryContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2<System.Guid,IdScriptableObject>",
            assembly.GetFieldType("IdScriptableObject", "RuntimeLookup"));
        Assert.Equal("System.Collections.Generic.List`1<!0>", assembly.GetFieldType("AbstractListVariable`1", "value"));
        Assert.Equal("System.Collections.Generic.List`1<AlchemyTypeSO>", assembly.GetFieldType("AlchemyRecipeSO", "alchemyTypes"));
        Assert.Equal("ResourceCostList", assembly.GetFieldType("AlchemyRecipeSO", "drainCost"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyRecipeSO", "masteryLevel"));
        AssertMethod(assembly, "AlchemyRecipeSO", "GetRequiredExperience", false, "BigDouble");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetMaxUsageSlots", false, "System.Int32");
        AssertMethod(assembly, "AlchemyRecipeSO", "GetCoreType", false, "AlchemyTypeSO");
        AssertMethod(assembly, "AlchemyRecipeSO", "IsDiscovered", false, "System.Boolean");

        AssertMethod(assembly, "AlchemyInstanceListVariable", "CanAddInstance", false, "System.Boolean", "AlchemyRecipeSO");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "GetNumEmptyTypelessSlots", false, "System.Int32");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "GetSlotsOnlyForType", false, "System.Int32", "AlchemyTypeSO");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "GetNumOfType", false, "System.Int32", "AlchemyTypeSO");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "AddAlchemyInstances", false, "System.Void", "AlchemyRecipeSO", "System.Int32");
        AssertMethod(assembly, "AlchemyInstanceListVariable", "RemoveAlchemyInstances", false, "System.Void", "AlchemyRecipeSO", "System.Int32");
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyInstance", "quantity"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyInstance", "queuedQuantity"));
        Assert.Equal("ResourceDrain", assembly.GetFieldType("AlchemyInstance", "resourceDrain"));
        AssertMethod(assembly, "AlchemyInstance", ".ctor", false, "System.Void", "AlchemyRecipeSO");
        AssertMethod(assembly, "AlchemyInstance", "GetDrainCostMod", false, "BigDouble");
        AssertMethod(assembly, "ResourceDrain", "GetCurrentDrain", false, "ResourceCostList");
        Assert.Equal("System.Boolean", assembly.GetFieldType("ResourceDrain", "isDrainApplied"));
        Assert.Equal("BigDouble", assembly.GetFieldType("ResourceDrain", "currentRatio"));
        Assert.Equal("BigDouble", assembly.GetFieldType("ResourceDrain", "usageRatio"));
        AssertMethod(assembly, "ResourceCostList", "Subtract", false, "ResourceCostList", "ResourceCostList");
        AssertMethod(assembly, "ResourceCostList", "Multiply", false, "ResourceCostList", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueSpend", false, "BigDouble", "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueRate", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetModdedDrain", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetQuantity", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "IsAtZero", false, "System.Boolean");
        AssertMethod(assembly, "ResourceSO", "GetTrueSoftCap", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "HasMaxQuantity", false, "System.Boolean");
    }

    [GameAssemblyFact]
    public void WorldAlchemyRecipeCapture_MatchesResolvedUsageLimitContract()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        AssertMethod(
            assembly,
            "AlchemyRecipeSO",
            "GetMaxUsageSlots",
            false,
            "System.Int32");
        AssertMethod(
            assembly,
            "AlchemyRecipeSO",
            "GetRequiredExperience",
            false,
            "BigDouble");
    }

    [GameAssemblyFact]
    public void AutoItems_MatchesWorldAndActionBoundaryContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.HasType("ConsumableTypeSO"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ConsumableSO>",
            assembly.GetFieldType("ConsumableSO", "All"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ConsumableTypeSO>",
            assembly.GetFieldType("ConsumableSO", "consumableTypes"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ConsumableUsage>",
            assembly.GetFieldType("ConsumableSO", "consumableUsages"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ConsumableCount>",
            assembly.GetFieldType("ConsumableSO", "consumableCounts"));
        Assert.Equal("ResourceCostList", assembly.GetFieldType("ConsumableSO", "consumeCost"));
        Assert.Equal("ResourceCostList", assembly.GetFieldType("ConsumableSO", "usageCost"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ResourceTuple>",
            assembly.GetFieldType("ResourceCostList", "costs"));
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        Assert.Equal("BigDouble", assembly.GetFieldType("ResourceTuple", "valueBig"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("ConsumableSO", "hasDuration"));
        Assert.Equal("System.Double", assembly.GetFieldType("ConsumableSO", "durationBase"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("ConsumableSO", "visible"));
        Assert.Equal("System.Int32", assembly.GetFieldType("ConsumableSO", "quantity"));
        Assert.Equal("UpgradeableObject", assembly.GetBaseType("ConsumableTypeSO"));
        Assert.Equal("UpgradeableObject", assembly.GetBaseType("ResourceSO"));
        Assert.Equal("TooltipableObject", assembly.GetBaseType("UpgradeableObject"));
        Assert.Equal("IdScriptableObject", assembly.GetBaseType("TooltipableObject"));
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "TooltipableObject", "GetName", false, "System.String");
        AssertMethod(
            assembly,
            "TooltipableObject",
            "GetIcon",
            false,
            "UnityEngine.Sprite");
        Assert.Equal("IntVariable", assembly.GetFieldType("ConsumableTypeSO", "maximumCarryLoad"));

        Assert.Equal("System.Boolean", assembly.GetFieldType("ConsumableUsage", "en"));
        Assert.Equal("BigDouble", assembly.GetFieldType("ConsumableUsage", "dr"));
        Assert.Equal("BigDouble", assembly.GetFieldType("ConsumableUsage", "maxDr"));
        Assert.Equal("ScalingInfo", assembly.GetFieldType("ConsumableUsage", "baseSi"));
        AssertMethod(assembly, "ConsumableUsage", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "ScalingInfo", "GetLevelInt", false, "System.Int32");
        Assert.Equal("System.Int32", assembly.GetFieldType("ConsumableCount", "fr"));
        AssertMethod(assembly, "ConsumableCount", "GetLevel", false, "System.Int32");
        AssertMethod(assembly, "ConsumableCount", "GetQuantity", false, "System.Int32");

        AssertMethod(assembly, "ConsumableSO", "CanFire", false, "System.Boolean");
        AssertMethod(assembly, "ConsumableSO", "IsVisible", false, "System.Boolean");
        AssertMethod(assembly, "ConsumableSO", "GetQuantity", false, "System.Int32");
        AssertMethod(assembly, "ConsumableSO", "GetQueued", false, "System.Int32");
        AssertMethod(assembly, "ConsumableSO", "IsRandomized", false, "System.Boolean");
        AssertMethod(assembly, "ConsumableSO", "SelectAndFire", false, "System.Void");
        AssertMethod(
            assembly,
            "ConsumableSO",
            "SetRandomization",
            false,
            "System.Void",
            "System.Boolean");
        AssertMethod(assembly, "Inventory", "CanUseConsumable", true, "System.Boolean");

        Assert.True(assembly.HasType("ScalingInfo"));
        Assert.True(assembly.HasType("IInstantEffectScript"));
        Assert.True(assembly.HasType("Targeting.ITargetable"));
        Assert.True(assembly.HasType("Targeting.BaseTargetSelection"));
        Assert.True(assembly.HasType("Targeting.TargetStructure"));
        Assert.Equal(
            "System.Collections.Generic.List`1<InstantEffectBlock>",
            assembly.GetFieldType("ConsumableSO", "onUseEffects"));
        Assert.Equal(
            "System.Collections.Generic.List`1<IInstantEffectScript>",
            assembly.GetFieldType("InstantEffectBlock", "effectScripts"));
        Assert.Equal(
            "Targeting.TargetSelectOptions",
            assembly.GetFieldType("RequestTargetEffectScript", "targetOptions"));
        AssertMethod(
            assembly,
            "ConsumableSO",
            "GetStrongest",
            false,
            "ConsumableCount");
        AssertMethod(
            assembly,
            "ConsumableSO",
            "GetStrongestLevel",
            false,
            "System.Int32");
        AssertMethod(
            assembly,
            "ConsumableSO",
            "GetCountScalingInfo",
            false,
            "ScalingInfo",
            "ConsumableCount");
        AssertMethod(
            assembly,
            "Targeting.TargetSelectOptions",
            "GetTargeting",
            false,
            "Targeting.BaseTargetSelection");
        AssertMethod(
            assembly,
            "Targeting.TargetStructure",
            "GetRandomList",
            false,
            "System.Collections.Generic.List`1<Targeting.ITargetable>",
            "ScalingInfo");
    }

    [GameAssemblyFact]
    public void AutoScribe_MatchesCompleteLifecycleBindingSet()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.True(assembly.HasType("CraftingRecipeSO"));
        Assert.True(assembly.HasType("CraftingRecipeListVariable"));
        Assert.True(assembly.HasType("CraftingInstance"));
        Assert.True(assembly.HasType("CraftingInstanceListVariable"));
        Assert.True(assembly.HasType("EnchantmentSO"));
        Assert.True(assembly.HasType("EnchantmentSO+EnchantTable"));
        Assert.True(assembly.HasType("EnchantmentInstance"));
        Assert.True(assembly.HasType("ConsumableSO+ConsumableGainEffect"));
        Assert.True(assembly.HasType("EnchantmentSO+EnchantItemScript"));

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2<System.Guid,IdScriptableObject>",
            assembly.GetFieldType("IdScriptableObject", "RuntimeLookup"));
        Assert.Equal(
            "System.Collections.Generic.List`1<!0>",
            assembly.GetFieldType("AbstractListVariable`1", "value"));
        Assert.Equal(
            "System.Collections.Generic.List`1<CraftingRecipeTypeSO>",
            assembly.GetFieldType("CraftingRecipeSO", "craftingTypes"));
        Assert.Equal(
            "System.Collections.Generic.List`1<InstantEffectBlock>",
            assembly.GetFieldType("CraftingRecipeSO", "completeEffects"));
        Assert.Equal(
            "System.Boolean",
            assembly.GetFieldType("CraftingRecipeSO", "useQuantityAsLevel"));
        Assert.Equal(
            "System.Boolean",
            assembly.GetFieldType("CraftingRecipeTypeSO", "isLevelType"));
        Assert.Equal(
            "System.Int32",
            assembly.GetFieldType("CraftingRecipeTypeSO", "maxStartingLevel"));
        Assert.Equal(
            "System.Collections.Generic.List`1<IInstantEffectScript>",
            assembly.GetFieldType("InstantEffectBlock", "effectScripts"));
        Assert.Equal(
            "ConsumableSO",
            assembly.GetFieldType("ConsumableSO+ConsumableGainEffect", "consumable"));
        Assert.Equal(
            "System.Collections.Generic.List`1<InstantEffectBlock>",
            assembly.GetFieldType("ConsumableSO", "onUseEffects"));
        Assert.Equal(
            "Targeting.TargetSelectOptions",
            assembly.GetFieldType("RequestTargetEffectScript", "targetOptions"));
        Assert.Equal(
            "EnchantmentSO",
            assembly.GetFieldType("EnchantmentSO+EnchantItemScript", "enchantment"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ConsumableCount>",
            assembly.GetFieldType("ConsumableSO", "consumableCounts"));
        Assert.Equal(
            "System.Collections.Generic.List`1<ResourceTuple>",
            assembly.GetFieldType("ResourceCostList", "costs"));
        Assert.Equal("ResourceSO", assembly.GetFieldType("ResourceTuple", "resource"));
        Assert.Equal(
            "System.Boolean",
            assembly.GetFieldType("CraftingInstanceListVariable", "isAutoList"));
        Assert.Equal(
            "System.Collections.Generic.List`1<StructureSO>",
            assembly.GetFieldType("StructureSO", "All"));
        Assert.Equal(
            "EnchantmentSO+EnchantTable",
            assembly.GetFieldType("StructureSO", "enchantTable"));
        Assert.Equal(
            "System.Collections.Generic.List`1<EnchantmentInstance>",
            assembly.GetFieldType("EnchantmentSO+EnchantTable", "enchantments"));

        AssertMethod(assembly, "CraftingRecipeSO", "IsVisible", false, "System.Boolean");
        AssertMethod(
            assembly,
            "CraftingRecipeSO",
            "CanBuyAt",
            false,
            "System.Boolean",
            "BigDouble");
        AssertMethod(
            assembly,
            "CraftingRecipeSO",
            "GetTotalCost",
            false,
            "ResourceCostList",
            "BigDouble",
            "BigDouble");
        AssertMethod(
            assembly,
            "CraftingRecipeSO",
            "GetMainType",
            false,
            "CraftingRecipeTypeSO");
        AssertMethod(
            assembly,
            "CraftingRecipeSO",
            "PurchaseQuantity",
            false,
            "System.Void",
            "BigDouble",
            "BigDouble");
        AssertMethod(assembly, "ResourceCostList", "HasEnough", false, "System.Boolean");
        AssertMethod(assembly, "ResourceTuple", "GetValue", false, "BigDouble");
        AssertMethod(assembly, "ResourceSO", "GetTrueQuantity", false, "BigDouble");
        AssertMethod(assembly, "AbstractListVariable`1", "GetMax", false, "System.Int32");
        AssertMethod(assembly, "GenericListVariable`1", "HasEmptySpot", false, "System.Boolean");
        AssertMethod(
            assembly,
            "GenericListVariable`1",
            "Add",
            false,
            "System.Void",
            "!0");
        AssertMethod(
            assembly,
            "AbstractRefInstance`1",
            "GetGuidReference",
            false,
            "System.Guid");
        AssertMethod(
            assembly,
            "CraftingInstance",
            ".ctor",
            false,
            "System.Void",
            "CraftingRecipeSO",
            "BigDouble");
        AssertMethod(assembly, "CraftingInstance", "GetQuantity", false, "BigDouble");
        AssertMethod(assembly, "CraftingInstance", "IsAuto", false, "System.Boolean");
        AssertMethod(assembly, "CraftingInstance", "IsExpired", false, "System.Boolean");
        AssertMethod(assembly, "CraftingInstance", "Initiate", false, "System.Void");
        AssertMethod(
            assembly,
            "CraftingInstance",
            "CheckInstantCraft",
            false,
            "System.Boolean");
        AssertMethod(assembly, "CraftingInstance", "InstantCraft", false, "System.Void");
        AssertMethod(assembly, "ConsumableCount", "GetLevel", false, "System.Int32");
        AssertMethod(assembly, "ConsumableCount", "GetQuantity", false, "System.Int32");
        AssertMethod(
            assembly,
            "ScalingInfo",
            "Basic",
            true,
            "ScalingInfo",
            "BigDouble");
        AssertMethod(
            assembly,
            "Targeting.TargetSelectOptions",
            "GetTargeting",
            false,
            "Targeting.BaseTargetSelection");
        AssertMethod(
            assembly,
            "Targeting.TargetStructure",
            "GetRandomList",
            false,
            "System.Collections.Generic.List`1<Targeting.ITargetable>",
            "ScalingInfo");
        AssertMethod(
            assembly,
            "EnchantmentInstance",
            "GetLevel",
            false,
            "System.Int32");
    }

    [GameAssemblyFact]
    public void CraftingRecipeWorldCapture_MatchesConcreteRecipeContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.List`1<CraftingRecipeSO>",
            assembly.GetFieldType("CraftingRecipeSO", "All"));
        Assert.Equal("ResourceCostList", assembly.GetFieldType("CraftingRecipeSO", "recipeCost"));
        Assert.Equal(
            "ResourceCostList",
            assembly.GetFieldType("CraftingRecipeSO", "generatedResources"));
        Assert.Equal(
            "System.Collections.Generic.List`1<PersistentEffectBlock>",
            assembly.GetFieldType("CraftingRecipeSO", "engagementEffects"));
        Assert.Equal("System.Double", assembly.GetFieldType("CraftingRecipeSO", "timeToComplete"));
        AssertMethod(
            assembly,
            "CraftingRecipeSO",
            "GetStartingQuantity",
            false,
            "BigDouble");
        AssertMethod(
            assembly,
            "ResourceCostList",
            "IsWithinCapacity",
            false,
            "System.Boolean");
        AssertMethod(
            assembly,
            "EffectBlock",
            "GetEffectNecessaryDrainRatio",
            false,
            "BigDouble");
    }

    [GameAssemblyFact]
    public void AlchemyGameplayDomainClassifier_MatchesStableIdentityTypeAndRegistryContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal(
            "System.Collections.Generic.Dictionary`2<System.Guid,IdScriptableObject>",
            assembly.GetFieldType("IdScriptableObject", "RuntimeLookup"));
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        Assert.True(assembly.HasType("AlchemyRecipeSO"));
        Assert.True(assembly.HasType("AlchemyTypeSO"));
        Assert.True(assembly.HasType("AlchemyRecipeListVariable"));
        Assert.Equal("System.Collections.Generic.List`1<AlchemyTypeSO>", assembly.GetFieldType("AlchemyRecipeSO", "alchemyTypes"));
        Assert.Equal("System.Collections.Generic.List`1<!0>", assembly.GetFieldType("AbstractListVariable`1", "value"));
    }

    [GameAssemblyFact]
    public void BigDouble_MatchesPrecisionBridgeContract()
    {
        using var firstPass = new GameAssemblyMetadata(GameAssemblyPaths.Require().FirstPass);

        Assert.Equal("System.Double", firstPass.GetFieldType("BigDouble", "mantissa"));
        Assert.Equal("System.Int64", firstPass.GetFieldType("BigDouble", "exponent"));
        Assert.Equal("BigDouble", firstPass.GetFieldType("BigDouble", "One"));
        AssertMethod(firstPass, "BigDouble", ".ctor", false, "System.Void", "System.Double");
        AssertMethod(firstPass, "BigDouble", ".ctor", false, "System.Void", "System.Double", "System.Int64");
        AssertMethod(firstPass, "BigDouble", "op_Implicit", true, "BigDouble", "System.Double");
    }

    [GameAssemblyFact]
    public void OrbMentor_MatchesNativeMasteryCatalogSaveAndTypeXpContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Collections.Generic.List`1<SpellRecipeSO>", assembly.GetFieldType("SpellRecipeSO", "All"));
        Assert.Equal("System.Int32", assembly.GetFieldType("SpellRecipeSO", "masteryLevel"));
        Assert.Equal("BigDouble", assembly.GetFieldType("SpellRecipeSO", "masteryExperience"));
        AssertMethod(assembly, "SpellRecipeSO", "GainMasteryExp", false, "System.Void", "BigDouble");
        AssertMethod(assembly, "SpellRecipeSO", "IsDiscovered", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "IsReadyToLevelMastery", false, "System.Boolean");
        AssertMethod(assembly, "SpellRecipeSO", "PurchaseLevel", false, "System.Void");
        AssertMethod(assembly, "SpellRecipeSO", "CollectSaveData", false, "JsonSaveData");
        AssertMethod(assembly, "IdScriptableObject", "GetGuid", false, "System.Guid");
        AssertMethod(assembly, "IdScriptableObject", "GetId", false, "System.Guid");
        Assert.Equal("System.Boolean", assembly.GetFieldType("SpellRecipeSO+SpellRecipeSaveData", "discovered"));
        Assert.Equal("BigDouble", assembly.GetFieldType("SpellRecipeSO+SpellRecipeSaveData", "masteryExperience"));
        Assert.Equal("System.Int32", assembly.GetFieldType("SpellRecipeSO+SpellRecipeSaveData", "masteryLevel"));
        AssertMethod(assembly, "SpellTypeSO", "GainTypeXp", false, "System.Void", "BigDouble", "System.Collections.Generic.List`1<TooltipNode>");
    }

    [GameAssemblyFact]
    public void OrbMentorWorldCollection_MatchesNativeViewAvailabilityContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("System.Collections.Generic.List`1<ViewSO>", assembly.GetFieldType("ViewSO", "All"));
        Assert.Equal("Prerequisites+Container", assembly.GetFieldType("ViewSO", "prerequisites"));
        AssertMethod(assembly, "ViewSO", "IsAvailable", false, "System.Boolean");
    }

    [GameAssemblyFact]
    public void OrbMentorAlchemy_MatchesNativeMasteryContracts()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Collections.Generic.List`1<AlchemyRecipeSO>", assembly.GetFieldType("AlchemyRecipeSO", "All"));
        Assert.Equal("BigDouble", assembly.GetFieldType("AlchemyRecipeSO", "masteryXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyRecipeSO", "masteryLevel"));
        AssertMethod(assembly, "AlchemyRecipeSO", "GainMasteryXp", false, "System.Void", "BigDouble");
        AssertMethod(assembly, "AlchemyRecipeSO", "IsAvailable", false, "System.Boolean");
        AssertMethod(assembly, "AlchemyRecipeSO", "IsDiscoveredRecipe", false, "System.Boolean");
        Assert.Equal("BigDouble", assembly.GetFieldType("AlchemyRecipeSO+AlchemyRecipeSaveData", "masteryXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("AlchemyRecipeSO+AlchemyRecipeSaveData", "masteryLevel"));
    }

    [GameAssemblyFact]
    public void OrbMentorArtifacts_MatchNativeProgressionSequence()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);
        Assert.Equal("System.Collections.Generic.List`1<EquipmentSO>", assembly.GetFieldType("EquipmentSO", "All"));
        Assert.Equal("BigDouble", assembly.GetFieldType("EquipmentSO", "masteryXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("EquipmentSO", "masteryLevel"));
        Assert.Equal("System.Boolean", assembly.GetFieldType("EquipmentSO", "isCreated"));
        Assert.Equal("ExperienceContainer", assembly.GetFieldType("EquipmentSO", "experienceContainer"));
        AssertMethod(assembly, "EquipmentSO", "IncrementActive", false, "System.Void", "System.Double");
        AssertMethod(assembly, "EquipmentSO", "GainMasteryLevels", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "EquipmentSO", "GetExperienceElement", false, "IExperienceElement");
        AssertMethod(assembly, "ExperienceContainer", "GainExperience", false, "System.Void", "BigDouble");
        AssertMethod(assembly, "ExperienceContainer", "GetGainedLevels", false, "System.Int32");
        AssertMethod(assembly, "ExperienceContainer", "GetExperience", false, "BigDouble");
        AssertMethod(assembly, "ExperienceContainer", "GetLevel", false, "System.Int32");
        AssertMethod(assembly, "ExperienceContainer", "Clone", false, "ExperienceContainer");
        Assert.Equal("BigDouble", assembly.GetFieldType("EquipmentSO+EquipmentSaveData", "mXp"));
        Assert.Equal("System.Int32", assembly.GetFieldType("EquipmentSO+EquipmentSaveData", "mLv"));
    }

    [GameAssemblyFact]
    public void ModConfigNavigation_MatchesKnownNativeLayout()
    {
        using var assembly = new GameAssemblyMetadata(GameAssemblyPaths.Require().AssemblyCSharp);

        Assert.Equal("ViewListVariable", assembly.GetFieldType("CoreViewManager", "coreViews"));
        Assert.Equal("ViewListVariable", assembly.GetFieldType("ViewManager", "coreViews"));
        AssertMethod(assembly, "CoreViewManager", "ChangeCoreView", false, "System.Void", "System.Int32");
        AssertMethod(assembly, "CoreViewManager", "GetSubViews", false, "ViewListVariable", "ViewSO");
        AssertMethod(
            assembly,
            "CoreViewManager",
            "SubmitViewLayer",
            false,
            "System.Void",
            "ViewListVariable",
            "ViewSO");
        AssertMethod(assembly, "ViewManager", "SwapCoreView", false, "System.Void", "ViewSO");
        AssertMethod(assembly, "ViewSO", "SetActive", false, "System.Void", "System.Boolean");
        AssertMethod(assembly, "ViewSO", "IsActive", false, "System.Boolean");
        Assert.Equal("ViewSO", assembly.GetFieldType("ManagedView", "viewReference"));
        Assert.Equal("UIGenericItem`1<ViewSO>", assembly.GetBaseType("UIViewRadioButton"));
        Assert.Equal("TMPro.TextMeshProUGUI", assembly.GetFieldType("UIViewRadioButton", "viewText"));
        Assert.Equal("UnityEngine.UI.Image", assembly.GetFieldType("UIViewRadioButton", "viewImage"));
        Assert.Equal("UnityEngine.Sprite", assembly.GetFieldType("UIViewRadioButton", "activeImage"));
        Assert.Equal("UnityEngine.UI.Image", assembly.GetFieldType("UIViewRadioButton", "buttonImage"));
        Assert.Equal("UnityEngine.Sprite", assembly.GetFieldType("UIViewRadioButton", "baseImage"));
        Assert.Equal(
            "UnityEngine.RectTransform",
            assembly.GetFieldType("UIContentArea", "canvas"));
        AssertMethod(
            assembly,
            "GlobalVariables",
            "GetGlobalStructureType",
            true,
            "StructureTypeSO");
        AssertMethod(
            assembly,
            "GlobalVariables",
            "GetCastingSpeedAttr",
            true,
            "AttributeSO");
        AssertMethod(
            assembly,
            "GlobalVariables",
            "GetHarvestSpeedAttr",
            true,
            "AttributeSO");
        AssertMethod(
            assembly,
            "GlobalVariables",
            "GetMasteryExpAttr",
            true,
            "AttributeSO");
        Assert.Equal("TooltipableObject", assembly.GetBaseType("AttributeSO"));
        Assert.Equal("UpgradeableObject", assembly.GetBaseType("StructureTypeSO"));
        Assert.Equal("TooltipableObject", assembly.GetBaseType("UpgradeableObject"));
        AssertMethod(
            assembly,
            "TooltipableObject",
            "GetIcon",
            false,
            "UnityEngine.Sprite");
    }

    [GameAssemblyFact]
    public void QuickControlUiObjects_MatchUnityRectTransformConstructionContract()
    {
        using var unity = new GameAssemblyMetadata(GameAssemblyPaths.Require().UnityCore);

        AssertMethod(
            unity,
            "UnityEngine.GameObject",
            ".ctor",
            false,
            "System.Void",
            "System.String",
            "System.Type[]");
        AssertMethod(
            unity,
            "UnityEngine.GameObject",
            "get_transform",
            false,
            "UnityEngine.Transform");
        Assert.Equal(
            "UnityEngine.Transform",
            unity.GetBaseType("UnityEngine.RectTransform"));
    }

    private static void AssertMethod(
        GameAssemblyMetadata assembly,
        string typeName,
        string methodName,
        bool isStatic,
        string returnType,
        params string[] parameterTypes)
    {
        var matches = assembly.GetMethods(typeName, methodName);
        Assert.Contains(matches, method =>
            method.IsStatic == isStatic &&
            method.ReturnType == returnType &&
            method.ParameterTypes.SequenceEqual(parameterTypes));
    }
}

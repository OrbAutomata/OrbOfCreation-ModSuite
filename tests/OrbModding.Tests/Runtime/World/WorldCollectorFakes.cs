using System;
using System.Collections.Generic;
using System.Linq;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

public abstract partial class GameWorldCollectorTestBase
{
    // The member shape each binder requires, stated once. Field names that a binder reads as fields
    // are spelled exactly as the game spells them; everything else is reached through an accessor, so
    // the property names here are free to read well.
    private protected sealed class FakeStructureType : FakeIdRegistry
    {
        public static readonly List<FakeStructureType> All = new();

        public List<FakeStructureType> subTypes = new();
        public int baseEffectLevel;
        public double baseBuildTime;
        public bool overrideRankDefault;
        public int overrideRank;
        public FakeModifierRecord activeCostMod = new(0d);
        public FakeModifierRecord attributeRankEffectMod = new(0d);
        public FakeModifierRecord bonusLevels = new(0d);
        public FakeModifierRecord buildSpeedMod = new(0d);
        public FakeModifierRecord costScalingMod = new(0d);
        public FakeModifierRecord drainCostMod = new(0d);
        public FakeModifierRecord echoBuildRating = new(0d);
        public FakeModifierRecord effectLevels = new(0d);
        public FakeModifierRecord passiveCostMod = new(0d);
        public FakeModifierRecord powerBuildRating = new(0d);
        public FakeModifierRecord structurePower = new(0d);
        public FakeModifierRecord structurePowerScaling = new(0d);
        public FakeModifierRecord structureSpeed = new(0d);
        internal static readonly FakeStructureType Shared = new()
        {
            structures = FakeStructure.All,
        };

        public List<FakeStructure> structures = new();
    }

    private protected sealed class FakeStructureListVariable : FakeAbstractListVariable<FakeStructure>
    {
        public List<FakeStructure> GetAll() => FakeStructure.All;
    }

    private protected sealed class FakeUpgradeListVariable : FakeAbstractListVariable<FakeUpgrade>
    {
        public List<FakeUpgrade> GetAll() => FakeUpgrade.All;
    }

    private protected sealed class FakeConsumableRefListVariable
    {
        public List<FakeConsumable?> value = new();
        public int Maximum = 4;
        public int GetMax() => Maximum;
    }

    private protected sealed class FakeConsumableInventory
    {
        internal static FakeConsumableInventory _instance = new();
        internal static bool CanUse = true;
        public FakeConsumableRefListVariable allConsumables = new();
        public FakeConsumableRefListVariable hotBar = new();
        public static bool CanUseConsumable() => CanUse;

        internal static void Reset()
        {
            _instance = new FakeConsumableInventory();
            CanUse = true;
        }
    }

    private protected sealed class FakeTargetingResultInfo { }

    private protected static class FakeTargetingManager
    {
        internal static TargetLink? Current;
        public static bool IsTargeting() => Current is not null;
        public static TargetLink? GetTargetingLink() => Current;
        internal static void Reset() => Current = null;

        internal sealed class TargetLink
        {
            private readonly List<IFakeTooltipable> targets;
            private readonly IFakeTooltipable owner;
            private readonly FakeScribeBaseTargetSelection selection = new();
            private readonly FakeTargetingResultInfo resultInfo = new();
            internal TargetLink(IFakeTooltipable owner, params IFakeTooltipable[] targets)
            {
                this.owner = owner;
                this.targets = new List<IFakeTooltipable>(targets);
            }
            public List<IFakeTooltipable> GetAllTargets() => new(targets);
            public IFakeTooltipable GetOwner() => owner;
            public FakeScribeBaseTargetSelection GetTargetSelection() => selection;
        }
    }

    private protected sealed class FakeStructure : FakeIdRegistry, IFakeTooltipable, IFakeScribeTargetable
    {
        public List<FakePermanentEffects> structureProperties = new();
        public static readonly List<FakeStructure> All = new();

        public FakeStructureType structureType = FakeStructureType.Shared;
        public List<FakeStructureType> structureSubTypes = new();
        public int Level;
        public int Queued;
        public bool Available = true;
        public int queuedEchos;
        public int completedEchos;
        public int selfBonusLevels;
        public BigDouble queueTimeLeft;
        public BigDouble currentBuildTime;
        public bool flagged;
        public int baseLevel;
        public float queueTimeTotal = 1f;
        public int quantity;
        public bool debugStructure;
        public bool disabled;
        public int observableId;
        public bool insufficientReqPenaltyActive;
        public int bufferDevelopedQuantity;
        public FakeModifierRecord power = new(100d);
        public FakeModifierRecord powerScaling = new(100d);
        public FakeModifierRecord speed = new(100d);
        public FakeModifierRecord passiveCostMod = new(100d);
        public FakeModifierRecord activeCostMod = new(100d);
        public FakeModifierRecord costScalingMod = new(100d);
        public FakeModifierRecord attributeRankEffectMod = new(100d);
        public FakeModifierRecord drainCostMod = new(100d);
        public FakeModifierRecord bonusLevels = new(0d);
        public FakeModifierRecord effectLevels = new(0d);
        public FakeModifierRecord buildSpeed = new(100d);
        public FakeModifierRecord echoBuildRating = new(0d);
        public FakeModifierRecord powerBuildRating = new(0d);
        public FakeCostList baseCost = new();
        public FakeModifierRef costPerQuantity = new();
        public FakePrerequisites prerequisitesPerLevel = new();
        public FakePrerequisites prerequisites = new();
        public FakeScribeEnchantTable enchantTable = new();

        public int GetPurchaseLevel() => Level;

        public int GetQueuedQuantity() => Queued;

        public bool IsAvailable() => Available;

        public string GetName() => "Structure " + Identity.ToString("D");
    }

    /// <summary>A structure's authored cost, shaped as the game shapes it: a list wrapper.</summary>
    private protected sealed class FakeCostList
    {
        public List<FakeCostEntry> costs = new();
    }

    /// <summary>
    /// One cost entry. The magnitude is the BigDouble field, not the serialized double beside it —
    /// that one is only what Unity writes to disk.
    /// </summary>
    private protected struct FakeCostEntry
    {
        public FakeReferencedEntity resource;
        public BigDouble valueBig;

        internal FakeCostEntry(Guid resourceId, double amount)
        {
            resource = new FakeReferencedEntity { Identity = resourceId };
            valueBig = new BigDouble(amount);
        }
    }

    /// <summary>A reference to a global modifier, which is what a structure holds rather than one.</summary>
    private protected sealed class FakeModifierRef
    {
        public FakeModifierVariable? variable;
    }

    private protected sealed class FakeUpgrade : FakeIdRegistry
    {
        public static readonly List<FakeUpgrade> All = new();

        public int Level;
        public int maxLevel;
        public bool Available = true;
        public int queuedLevels;
        public BigDouble buildTime;
        public double developmentTime = 5d;
        public int cachedCostLevel = -1;
        public FakeCostList resourceCost = new();
        public FakeModifierListRef resourceCostModPerLevel = new();
        public FakePrerequisites prerequisitesPerLevel = new();
        public FakePrerequisites prerequisites = new();

        /// <summary>What one level of this upgrade permanently applies.</summary>
        public FakePermanentEffects permanentEffects = new();

        /// <summary>Where a purchase places what it grants: the World > Aspects pedestals.</summary>
        public List<FakeViewListTuple> viewListAdditions = new();

        public int GetPurchaseLevel() => Level;

        public bool IsAvailable() => Available;
    }

    private protected sealed class FakeViewListTuple
    {
        public FakeViewList list = new();
    }

    private protected sealed class FakeViewList
    {
        public bool room = true;

        public bool HasEmptySpot() => room;
    }

    private protected sealed class FakePrerequisiteLink
    {
        public sealed class LinkDefinition
        {
            public FakePrerequisites prerequisites = new();
            public bool isActiveEnabled = true;
            public bool isPassiveEnabled;
            public long currentFrame = -1;
        }

        public static readonly List<FakePrerequisiteLink> All = new();
        public Guid Identity = Guid.NewGuid();
        public List<LinkDefinition> linkTiers = new();

        public Guid GetGuid() => Identity;
    }

    private protected static class FakeGameManager
    {
        public static long currentFrame;
    }

    /// <summary>
    /// What an upgrade holds instead of a modifier list: a reference that resolves either to one it
    /// names or to a shared standard. The resolution is behind GetValue() on both paths.
    /// </summary>
    private protected sealed class FakeModifierListRef
    {
        public FakeModifierList? variable;

        public FakeModifierList GetValue() => variable ?? Empty;

        private static readonly FakeModifierList Empty = new();
    }

    /// <summary>
    /// Two lists, not one. The exponents strengthen the modifiers before any of them touches a value,
    /// so flattening them together would change the arithmetic.
    /// </summary>
    private protected sealed class FakeModifierList
    {
        public List<FakeValueModifier> modifiers = new();
        public List<FakeValueModifier> exponents = new();
    }

    private protected sealed class FakeResearch
    {
        public List<FakeEffectBlock> researchEffectBlocks = new();
        public static readonly List<FakeResearch> All = new();

        public Guid Identity = Guid.NewGuid();
        public int level;
        public int queuedLevels;
        public int researchStage;
        public int selfBonusLevels;
        public int maxLevel = 1;
        public double researchTime = 60d;
        public bool isDeveloping;
        public bool isActive;
        public bool flagged;
        public bool Available = true;
        public FakePrerequisites levelPrerequisites = new();
        public FakePrerequisites visibilityPrerequisites = new();
        public FakePrerequisites levelVisibilityPrereq = new();
        public bool hiddenLevel;
        public int levelVisibilityRange = 2;
        public int requiredStagesCached;
        public BigDouble requiredTimeCached;
        public FakeModifierRecord requirementsAdjust = new(0d);
        public FakeModifierRecord bonusLevels = new(0d);
        public FakeModifierRecord baseLevels = new(0d);
        public FakeModifierRecord power = new(100d);
        public FakeModifierRecord maxLevelCap = new(0d);
        public FakeModifierRecord leewayPoints = new(0d);
        public List<FakeResearchType> researchTypes = new();
        public FakeCraftingResourceCostList researchCost = new();
        public FakeResearchFillList resourceFillList = new();

        public Guid GetGuid() => Identity;

        public bool IsAvailable() => Available;

        public bool IsVisible() => Available;

        public bool IsComplete() => maxLevel > 0 && GetBaseLevel() >= maxLevel;

        public bool CanDevelop() => IsWithinDevelopRange() && !isDeveloping;

        public bool IsWithinDevelopRange() =>
            !IsComplete() && MeetsLevelRequirements() && StillHasLeeway() &&
            IsBelowArtificialMaxLevel() && IsBelowMaxInvestmentLevel();

        public bool IsWithinDevelopRangeAt(int atLevel) => IsWithinDevelopRange();

        public bool HasMaxLevel() => maxLevel > 0;

        public bool MeetsLevelRequirements() =>
            levelPrerequisites.Check(new Requirements.ConditionInfo(GetRequirementLevel()));

        public bool StillHasLeeway() => true;

        public bool IsBelowArtificialMaxLevel() => true;

        public bool IsBelowMaxInvestmentLevel() => !IsComplete();

        public int GetPurchasedLevels() => level;

        public int GetBaseLevel() => level;

        public int GetBonusLevels() => 0;

        public int GetLevel() => GetBaseLevel() + GetBonusLevels();

        public int GetArtificialMaxLevel() => 0;

        public int GetRequirementLevel() => requirementsAdjust.AdjustRawLevel(GetBaseLevel());

        public int GetQueuedLevels() => queuedLevels + (isDeveloping ? 1 : 0);

        public int GetCurrentInvestmentLevel() => level + GetQueuedLevels();

        public BigDouble GetCurrentTime() => resourceFillList.GetAverageRatio() * GetRequiredTime();

        public BigDouble GetRemainingTime() =>
            (BigDouble.One - resourceFillList.GetLowestRatio()) * GetRequiredTime();

        public BigDouble GetTimeRatio() => GetCurrentTime() / GetRequiredTime();

        public BigDouble GetRequiredTime() => requiredTimeCached == BigDouble.Zero
            ? new BigDouble(researchTime)
            : requiredTimeCached;

        public bool CanApplyBonusLevels() =>
            researchTypes.Exists(type => type.GetRemainingFreeBonusLevels() > 0);

        public int GetFreeBonusLevelsLeft() => researchTypes.Count == 0
            ? 0
            : researchTypes.Max(type => type.GetRemainingFreeBonusLevels());

        public FakeCraftingResourceCostList GetDevelopmentCost() => researchCost;

        public FakeCraftingResourceCostList GetDevelopmentCostAtLevel(int atLevel) =>
            researchCost.Multiply(BigDouble.One);
    }

    private protected static class FakeSettingsManager
    {
        public static bool QueueMode;
        public static bool CancellableSpells = true;
        public static bool IsResearchQueueMode() => QueueMode;
        public static bool CanCancelSpells() => CancellableSpells;
    }

    private protected sealed class FakeResearchType
    {
        public static readonly List<FakeResearchType> All = new();

        public bool linkedDevelopCost;
        public bool linkedResourceCost;
        public bool linkedResearchTime;
        public bool ignoreWhenLevelDependent;
        public bool persistThroughReset;
        public int cachedTotalLevel;
        public int cachedPeakLevel;
        public int cachedQueuedLevel;
        public int cachedDevelopingLevel;
        public int cachedQueuedValue;
        public int cachedInvestmentLevel;
        public int cachedPurchasedLevel;
        public FakeModifierRecord freeBonusLevels = new(0d);
        public FakeModifierRecord levelRequirementAdjust = new(0d);
        public FakeModifierRecord maxInvestmentLevel = new(0d);
        public FakeModifierRecord maxLevelCap = new(0d);
        public FakeModifierRecord power = new(0d);
        public FakeModifierRecord usedBonusLevels = new(0d);
        public Guid Identity = Guid.NewGuid();
        public int RemainingFreeBonusLevels { get; set; }
        public int CurrentInvestmentLevel { get; set; }
        public int MaxInvestmentLevel { get; set; }
        public Guid GetGuid() => Identity;
        public int GetRemainingFreeBonusLevels() => RemainingFreeBonusLevels;
        public int GetCurrentInvestmentLevel() => CurrentInvestmentLevel;
        public int GetMaxInvestmentLevel() => MaxInvestmentLevel;
    }

    internal sealed class FakeResearchFillList
    {
        public List<ResourceFillEntry> entries = new();
        public BigDouble GetAverageRatio() => entries.Count == 0
            ? BigDouble.Zero
            : entries.Select(entry => entry.GetQuantity() / entry.GetCapacity())
                .Aggregate(BigDouble.Zero, (sum, value) => sum + value) / new BigDouble(entries.Count);
        public BigDouble GetLowestRatio() => entries.Count == 0
            ? BigDouble.Zero
            : entries.Select(entry => entry.GetQuantity() / entry.GetCapacity()).Min();

        internal sealed class ResourceFillEntry
        {
            public FakeResource resource = new();
            public BigDouble Quantity;
            public BigDouble Capacity = BigDouble.One;
            public FakeResource get_resource() => resource;
            public BigDouble GetQuantity() => Quantity;
            public BigDouble GetCapacity() => Capacity;
            public BigDouble GetRemaining() => BigDouble.Max(Capacity - Quantity, BigDouble.Zero);
        }
    }

    /// <summary>
    /// A global number variable. The two number registries hold the same shape but different
    /// instances, so they get a stand-in each — pointing both at one type would have the second walk
    /// re-offer entities the first already claimed.
    /// </summary>
    private protected sealed class FakeNumber
    {
        public List<FakeEffectBlock> persistentEffectBlocks = new();
        public static readonly List<FakeNumber> All = new();

        public Guid Identity = Guid.NewGuid();
        public FakeModifierRecord value = new(0d);
        public bool isPercentVariable;
        public bool isTimeVariable;
        public bool isTimeAccurateVariable;

        public Guid GetGuid() => Identity;
    }

    internal sealed class FakeCount
    {
        public List<FakeEffectBlock> persistentEffectBlocks = new();
        public static readonly List<FakeCount> All = new();

        public Guid Identity = Guid.NewGuid();
        public FakeModifierRecord value = new(0d);
        public bool isPercentVariable;
        public bool isTimeVariable;
        public bool isTimeAccurateVariable;

        internal FakeCount()
        {
        }

        internal FakeCount(int amount) => value = new FakeModifierRecord(amount);

        public Guid GetGuid() => Identity;
        public int AsInt() => (int)value.GetValue().ToDouble();
        public void SetValue(int amount) => value = new FakeModifierRecord(amount);
    }

    private protected static class FakeGlobalVariables
    {
        private static readonly FakeCount MultiBuy = new(1);

        public static FakeCount GetMultiBuy() => MultiBuy;

        public static void SetMultiBuy(int amount) => MultiBuy.SetValue(amount);
    }

    internal sealed class FakeCraftingPage : UnityEngine.Object
    {
        public FakeScribeRecipeList availableRecipes = new();
        public FakeScribeInstanceList craftingQueueInstances = new();
        public FakeScribeInstanceList craftingAutomationInstances =
            new() { isAutoList = true };
        public FakeCount craftMode = new(0);
        public FakeCraftingRecipeType mainCraftType = new();
    }

    private protected sealed class FakeFlag
    {
        public static readonly List<FakeFlag> All = new();

        public Guid Identity = Guid.NewGuid();
        public bool value;

        public Guid GetGuid() => Identity;
        public bool GetValue() => value;
        public void SetValue(bool next) => value = next;
        public bool initialValue;
        public bool isSaved;
        public int observerId;
    }

    private protected sealed class FakeChallengeList
    {
        public List<FakeChallenge> value = new();
        public int Maximum = 3;
        public int GetMax() => Maximum;
        public bool IsChallengeRestricted(FakeChallenge challenge) => false;
    }

    private protected sealed class FakeChallengeManager
    {
        public static FakeChallengeManager instance = new();
        public FakeChallengeList preferredChallenges = new();
        public FakeChallengeList activeChallenges = new();
    }

    private protected sealed class FakePersistentResetManager
    {
        public static FakePersistentResetManager instance = new();
        public FakeResource persistentResource = new();
        public FakeCount persistValue = new(0);
        public FakeCount persistValueNew = new(0);
        public FakeCount persistValueLast = new(0);
        public FakeCount persistentResetCount = new(0);
        public FakeChallengeList activeChallenges = new();
        public FakeCount challengeRerollsLeft = new(1);
        public FakeCount challengeRerollsMax = new(1);
        public FakeFlag hasCompleteWorldCycle = new() { value = true };
        public FakeFlag hasFetchedChallenges = new();
    }

    private protected sealed class FakeKeywordlessRitual
    {
        public static readonly List<FakeKeywordlessRitual> All = new();

        public Guid Identity = Guid.NewGuid();

        public Guid GetGuid() => Identity;
    }

    /// <summary>
    /// The player's static globals, shaped as the game holds them: an accessor returning a variable
    /// whose <c>value</c> record carries the number.
    /// </summary>
    /// <remarks>
    /// The attribute-quality bonus resets to zero because that is what the game authors it at — it is
    /// granted by research — and zero is the exponent that leaves the quality discount at one. So
    /// every test that does not speak about quality prices exactly as it did before the discount was
    /// read at all.
    /// </remarks>
    private protected sealed class FakePlayerGlobals
    {
        private static readonly FakePlayerGlobals _instance = new();
        private static FakeGlobalVariable _overflow = new(200d);
        private static FakeGlobalVariable _overflowLoss = new(100d);
        private static FakeGlobalVariable _resetTimePassed = new(60d);
        private static FakeGlobalVariable _structureCost = new(100d);
        private static FakeGlobalVariable _attributeQualityBonus = new(0d);
        private FakeCount spellOutputLevel = new(1);
        public FakeCount maxSpellOutputLevel = new(100);
        private FakeCount reserveLevel = new(1);
        public FakeCount maxReserveLevel = new(100);
        public FakeCount druidryOutputLevel = new(1);
        public FakeCount maxDruidryOutputLevel = new(1);
        public FakeCount alchemyOutputLevel = new(1);
        public FakeCount maxAlchemyOutputLevel = new(1);

        private FakePlayerGlobals()
        {
        }

        /// <summary>Back to the game's authored values, so one test cannot set another's globals.</summary>
        internal static void Reset()
        {
            _overflow = new FakeGlobalVariable(200d);
            _overflowLoss = new FakeGlobalVariable(100d);
            _resetTimePassed = new FakeGlobalVariable(60d);
            _structureCost = new FakeGlobalVariable(100d);
            _attributeQualityBonus = new FakeGlobalVariable(0d);
            _instance.spellOutputLevel = new FakeCount(1);
            _instance.maxSpellOutputLevel = new FakeCount(100);
            _instance.reserveLevel = new FakeCount(1);
            _instance.maxReserveLevel = new FakeCount(100);
            _instance.druidryOutputLevel = new FakeCount(1);
            _instance.maxDruidryOutputLevel = new FakeCount(1);
            _instance.alchemyOutputLevel = new FakeCount(1);
            _instance.maxAlchemyOutputLevel = new FakeCount(1);
        }

        internal static void SetOverflow(double percent) => _overflow = new FakeGlobalVariable(percent);

        internal static void SetStructureCost(double percent) =>
            _structureCost = new FakeGlobalVariable(percent);

        internal static void SetAttributeQualityBonus(double exponent) =>
            _attributeQualityBonus = new FakeGlobalVariable(exponent);

        internal static void SetMaximumSpellOutputLevel(FakeCount level) =>
            _instance.maxSpellOutputLevel = level;

        public static FakeGlobalVariable GetResourceOverflow() => _overflow;

        public static FakeGlobalVariable GetResourceOverflowLoss() => _overflowLoss;

        public static FakeGlobalVariable GetResetTimePassed() => _resetTimePassed;

        public static FakeGlobalVariable GetStructureCost() => _structureCost;

        public static FakeGlobalVariable GetAttributeQualityBonus() => _attributeQualityBonus;

        public static FakeCount GetSpellOutputLevel() => _instance.spellOutputLevel;
        public static FakeCount GetReserveLevel() => _instance.reserveLevel;

        // The four variables a glyph's critical and echo slots print against. They are serialized
        // on the singleton rather than assets in a registry, so the accessor is the only way to
        // learn which variable a slot means.
        internal static FakeNumber SpellCriticalCastRating = new();
        internal static FakeNumber SpellCriticalCastEffect = new();
        internal static FakeNumber SpellDoubleCastRating = new();
        internal static FakeNumber SpellDoubleCastEffect = new();

        public static FakeNumber GetSpellCriticalCastRating() => SpellCriticalCastRating;
        public static FakeNumber GetSpellCriticalCastEffect() => SpellCriticalCastEffect;
        public static FakeNumber GetSpellDoubleCastRating() => SpellDoubleCastRating;
        public static FakeNumber GetSpellDoubleCastEffect() => SpellDoubleCastEffect;
    }

    /// <summary>
    /// A build that renamed the fifth globals accessor and nothing else: every category still binds,
    /// so only the globals reader is short.
    /// </summary>
    private protected sealed class FakePlayerGlobalsMissingTheQualityBonus
    {
        private static readonly FakePlayerGlobalsMissingTheQualityBonus _instance = new();
        private FakeCount spellOutputLevel = new(1);
        public FakeCount maxSpellOutputLevel = new(100);
        private FakeCount reserveLevel = new(1);
        public FakeCount maxReserveLevel = new(100);
        public FakeCount druidryOutputLevel = new(1);
        public FakeCount maxDruidryOutputLevel = new(1);
        public FakeCount alchemyOutputLevel = new(1);
        public FakeCount maxAlchemyOutputLevel = new(1);

        private FakePlayerGlobalsMissingTheQualityBonus()
        {
        }

        public static FakeGlobalVariable GetResourceOverflow() =>
            FakePlayerGlobals.GetResourceOverflow();

        public static FakeGlobalVariable GetResourceOverflowLoss() =>
            FakePlayerGlobals.GetResourceOverflowLoss();

        public static FakeGlobalVariable GetResetTimePassed() =>
            FakePlayerGlobals.GetResetTimePassed();

        public static FakeGlobalVariable GetStructureCost() => FakePlayerGlobals.GetStructureCost();

        public static FakeCount GetSpellOutputLevel() => _instance.spellOutputLevel;

        public static FakeCount GetReserveLevel() => _instance.reserveLevel;

        public static FakeNumber GetSpellCriticalCastRating() =>
            FakePlayerGlobals.GetSpellCriticalCastRating();

        public static FakeNumber GetSpellCriticalCastEffect() =>
            FakePlayerGlobals.GetSpellCriticalCastEffect();

        public static FakeNumber GetSpellDoubleCastRating() =>
            FakePlayerGlobals.GetSpellDoubleCastRating();

        public static FakeNumber GetSpellDoubleCastEffect() =>
            FakePlayerGlobals.GetSpellDoubleCastEffect();
    }

    private protected sealed class FakeGlobalVariable
    {
        public FakeModifierRecord value;

        internal FakeGlobalVariable(double amount) => value = new FakeModifierRecord(amount);
    }

    /// <summary>A resource shape that lost two members, standing in for a game update that moved them.</summary>
    private protected sealed class PartialResource
    {
        public static readonly List<PartialResource> All = new();

        public FakeModifierRecord maxQuantity = new(-1d);

        public Guid GetGuid() => Guid.NewGuid();

        public BigDouble GetQuantity() => default;
    }
}

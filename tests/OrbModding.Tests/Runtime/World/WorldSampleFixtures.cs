using System;
using OrbAutomata;
using OrbModding.Common.Runtime.ServiceCycle.Contracts;
using OrbModding.Common.Runtime.World;

namespace OrbModding.Tests.Runtime.World;

/// <summary>
/// Builds world readings for tests, with every field a test is not about defaulted to the game's own
/// neutral value.
/// </summary>
/// <remarks>
/// The sample constructors are positional because a published shape may not carry settable
/// properties, and they are long because the collector reads everything the game persists. Neither
/// should make a test about headroom spell out thirteen modifier records. Defaults are the game's
/// identities — quality, gain rate and reservation at 100, level and effect modifiers at 0, capacity
/// at the negative "uncapped" sentinel — so a fixture that says nothing means nothing, rather than
/// meaning zero.
/// </remarks>
internal static class WorldSamples
{
    /// <summary>
    /// A resource's authored traits, with everything a test is not about at the game's own neutral
    /// value. Only the flags that change what a consumer decides are named.
    /// </summary>
    internal static RawResourceTraits Traits(bool bandwidthResource = false) =>
        new(
            rarityValue: 0d,
            rarityValueEnd: 0d,
            restEngageTime: 0d,
            pauseLossOnChange: false,
            canOverflow: false,
            noOverflowRubberBand: false,
            bandwidthResource: bandwidthResource,
            invertedResource: false,
            excludeFromGlobals: false,
            startVisible: true,
            appliedMaxQuantity: BigDouble.Zero,
            quantitySoftCapOrder: 0,
            quantitySoftCapMagnitude: 0,
            quantitySoftCapRatio: 0d,
            debugResource: false,
            currentLossRate: 0d,
            lastReservation: BigDouble.Zero,
            debouncedReplenish: BigDouble.Zero,
            debouncedReverberate: BigDouble.Zero,
            debouncedDecay: BigDouble.Zero,
            firstIncrement: false);

    internal static RawResourceSample Resource(
        Guid resourceId,
        double quantity = 0d,
        double capacity = -1d,
        double rate = 0d,
        bool visible = true,
        double lifetimeQuantity = 0d,
        double discoveryTime = 0d,
        double quality = 100d,
        double gainRate = 100d,
        double drain = 0d,
        double reservation = 100d,
        double usage = 0d,
        bool inLossMode = false,
        bool inRestMode = false,
        bool inRallyMode = false,
        long appliedLevels = 0L,
        Guid levelVariableId = default,
        RawResourceRateInputs rateInputs = default,
        RawResourceTraits traits = default,
        RawResourceModifiers modifiers = default) =>
        new(
            resourceId,
            new BigDouble(quantity),
            new BigDouble(capacity),
            visible,
            new BigDouble(lifetimeQuantity),
            new BigDouble(discoveryTime),
            new BigDouble(quality),
            new BigDouble(gainRate),
            new BigDouble(drain),
            new BigDouble(reservation),
            new BigDouble(usage),
            inLossMode,
            inRestMode,
            inRallyMode,
            appliedLevels,
            levelVariableId,
            Rate(rate, in rateInputs),
            in traits,
            in modifiers);

    /// <summary>
    /// The same, for magnitudes and NaNs a double literal cannot express. Defaults are nullable rather
    /// than <c>default</c>, because zero is a meaningful reading for every one of these — an omitted
    /// quality has to come out as parity, and a quality deliberately set to zero has to stay zero.
    /// </summary>
    internal static RawResourceSample Resource(
        Guid resourceId,
        BigDouble quantity,
        BigDouble capacity,
        BigDouble? rate = null,
        bool visible = true,
        BigDouble? lifetimeQuantity = null,
        BigDouble? discoveryTime = null,
        BigDouble? quality = null,
        BigDouble? gainRate = null,
        BigDouble? drain = null,
        BigDouble? reservation = null,
        BigDouble? usage = null,
        bool inLossMode = false,
        bool inRestMode = false,
        bool inRallyMode = false,
        long appliedLevels = 0L,
        Guid levelVariableId = default,
        RawResourceRateInputs rateInputs = default,
        RawResourceTraits traits = default,
        RawResourceModifiers modifiers = default) =>
        new(
            resourceId,
            quantity,
            capacity,
            visible,
            lifetimeQuantity ?? BigDouble.Zero,
            discoveryTime ?? BigDouble.Zero,
            quality ?? new BigDouble(100d),
            gainRate ?? new BigDouble(100d),
            drain ?? BigDouble.Zero,
            reservation ?? new BigDouble(100d),
            usage ?? BigDouble.Zero,
            inLossMode,
            inRestMode,
            inRallyMode,
            appliedLevels,
            levelVariableId,
            Rate((rate ?? BigDouble.Zero).ToDouble(), in rateInputs),
            in traits,
            in modifiers);

    private static RawResourceRateInputs Rate(
        double rate,
        in RawResourceRateInputs supplied) =>
        rate == 0d ? supplied : new RawResourceRateInputs(
            new BigDouble(rate),
            default,
            default,
            default,
            default,
            default,
            rateModifiers: 1,
            rateSplashModifiers: 0,
            rateMaxPercentModifiers: 0,
            rateInterestPercentModifiers: 0,
            rateMissingPercentModifiers: 0,
            rateLifetimePercentModifiers: 0,
            default,
            default,
            default,
            baseLoss: 0d);

    internal static RawStructureSample Structure(
        Guid structureId,
        double level = 0d,
        double queuedLevels = 0d,
        bool unlocked = true,
        int queuedEchos = 0,
        int completedEchos = 0,
        int selfBonusLevels = 0,
        double queueTimeLeft = 0d,
        double currentBuildTime = 0d,
        bool flagged = false,
        double power = 100d,
        double powerScaling = 100d,
        double speed = 100d,
        double passiveCostMod = 100d,
        double activeCostMod = 100d,
        double costScalingMod = 100d,
        double attributeRankEffectMod = 100d,
        double drainCostMod = 100d,
        double bonusLevels = 0d,
        double effectLevels = 0d,
        double buildSpeed = 100d,
        double echoBuildRating = 0d,
        double powerBuildRating = 0d,
        int baseLevel = 0,
        float queueTimeTotal = 1f,
        bool debugStructure = false,
        int observableId = 0,
        bool insufficientReqPenaltyActive = false,
        int bufferDevelopedQuantity = 0,
        Guid costPerQuantityId = default,
        bool disabled = false,
        Guid structureTypeId = default) =>
        new(
            structureId,
            structureTypeId,
            new BigDouble(level),
            new BigDouble(queuedLevels),
            unlocked,
            queuedEchos,
            completedEchos,
            selfBonusLevels,
            new BigDouble(queueTimeLeft),
            new BigDouble(currentBuildTime),
            flagged,
            baseLevel,
            queueTimeTotal,
            debugStructure,
            disabled,
            observableId,
            insufficientReqPenaltyActive,
            bufferDevelopedQuantity,
            costPerQuantityId,
            new RawStructureModifiers(
                new BigDouble(power),
                new BigDouble(powerScaling),
                new BigDouble(speed),
                new BigDouble(passiveCostMod),
                new BigDouble(activeCostMod),
                new BigDouble(costScalingMod),
                new BigDouble(attributeRankEffectMod),
                new BigDouble(drainCostMod),
                new BigDouble(bonusLevels),
                new BigDouble(effectLevels),
                new BigDouble(buildSpeed),
                new BigDouble(echoBuildRating),
                new BigDouble(powerBuildRating)));

    internal static RawUpgradeSample Upgrade(
        Guid upgradeId,
        int level = 0,
        int maxLevel = 1,
        bool available = true,
        int queuedLevels = 0,
        double buildTime = 0d,
        double developmentTime = 5d,
        int cachedCostLevel = -1) =>
        new(
            upgradeId,
            level,
            maxLevel,
            available,
            queuedLevels,
            new BigDouble(buildTime),
            developmentTime,
            cachedCostLevel);

    internal static WorldResearch Research(
        Guid researchId,
        int level = 0,
        int queuedLevels = 0,
        int researchStage = 0,
        int selfBonusLevels = 0,
        int maxLevel = 1,
        double researchTime = 60d,
        bool isDeveloping = false,
        bool isActive = false,
        bool flagged = false,
        bool available = true,
        double bonusLevels = 0d,
        double baseLevels = 0d,
        double power = 100d,
        double maxLevelCap = 0d,
        double leewayPoints = 0d,
        bool hiddenLevel = false,
        int levelVisibilityRange = 2,
        int requiredStagesCached = 0,
        double requiredTimeCached = 0d,
        int? baseRequirementLevel = null,
        int? effectiveRequirementLevel = null,
        WorldResearchRequirementAdjustment[]? requirementAdjustments = null) =>
        new(
            researchId,
            level,
            queuedLevels,
            researchStage,
            selfBonusLevels,
            maxLevel,
            researchTime,
            isDeveloping,
            isActive,
            flagged,
            available,
            visible: available,
            complete: maxLevel > 0 && level + (int)baseLevels >= maxLevel,
            canDevelop: available && !isDeveloping,
            withinDevelopRange: available,
            meetsLevelRequirements: true,
            stillHasLeeway: true,
            belowArtificialMaxLevel: true,
            belowMaxInvestmentLevel: maxLevel <= 0 || level + (int)baseLevels < maxLevel,
            purchasedLevels: level,
            baseLevel: level + (int)baseLevels,
            bonusLevel: (int)bonusLevels,
            totalLevel: level + (int)baseLevels + (int)bonusLevels,
            artificialMaxLevel: (int)maxLevelCap,
            hiddenLevel,
            levelVisibilityRange,
            requiredStagesCached,
            new BigDouble(requiredTimeCached),
            baseRequirementLevel ?? level,
            effectiveRequirementLevel ?? baseRequirementLevel ?? level,
            PublicationTable<WorldResearchRequirementAdjustment>.Create(
                requirementAdjustments ?? Array.Empty<WorldResearchRequirementAdjustment>()),
            new RawResearchModifiers(
                new BigDouble(bonusLevels),
                new BigDouble(baseLevels),
                new BigDouble(power),
                new BigDouble(maxLevelCap),
                new BigDouble(leewayPoints)));

    internal static WorldPlotNode PlotNode(
        Guid plotNodeId,
        bool visible = true,
        int remainingQuantity = 0,
        int remainingTotalQuantity = 0,
        int idleQuantity = 0,
        int totalQuantity = 0,
        double sizeMod = 100d) =>
        new(
            new RawPlotNodeSample(
                plotNodeId,
                visible,
                BigDouble.Zero,
                BigDouble.Zero,
                BigDouble.Zero,
                BigDouble.Zero,
                0,
                false,
                false,
                false,
                false,
                false,
                0,
                BigDouble.Zero,
                BigDouble.Zero,
                new BigDouble(100d),
                new BigDouble(100d),
                new BigDouble(100d),
                new BigDouble(100d),
                new BigDouble(100d),
                new BigDouble(100d),
                new BigDouble(100d),
                new BigDouble(sizeMod),
                new BigDouble(100d),
                new BigDouble(100d),
                BigDouble.Zero,
                BigDouble.Zero,
                0,
                idleQuantity,
                totalQuantity),
            remainingQuantity,
            remainingTotalQuantity);

    /// <summary>
    /// A published plot-and-action pair. The derived half is spelled out rather than recomputed, so a
    /// test can state a combination the deriver would never produce and still ask what a consumer
    /// does with it.
    /// </summary>
    internal static WorldPlotAction PlotAction(
        Guid plotNodeId,
        Guid plotNodeActionId,
        int offeredCount = 1,
        int instanceCount = 1,
        bool prerequisitesMet = true,
        int elementCost = 1,
        bool elementCostKnown = true,
        bool hasEnoughForOneInstance = true,
        int maximumRemainingInstances = 1) =>
        new(
            new RawPlotAction(
                plotNodeId,
                plotNodeActionId,
                offeredCount,
                instanceCount,
                prerequisitesMet
                    ? PlotActionPrerequisiteEvidence.NativeLatchedTrue
                    : PlotActionPrerequisiteEvidence.UnknownNeedsNativeValidation),
            elementCost,
            elementCostKnown,
            hasEnoughForOneInstance,
            maximumRemainingInstances);
}

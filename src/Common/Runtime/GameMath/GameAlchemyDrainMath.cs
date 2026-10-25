using System;

namespace OrbModding.Common.Runtime.GameMath;

/// <summary>The AlchemyInstance drain formula shared by Concept and ordinary-Alchemy projections.</summary>
internal static class GameAlchemyDrainMath
{
    internal static BigDouble AdjustedQuantity(BigDouble quantity, BigDouble rarityMultiplier, bool useRarity) =>
        !useRarity || quantity <= 0 ? quantity : quantity + rarityMultiplier * (quantity - BigDouble.One);

    /// <summary>The common AlchemyInstance drain formula, with level supplied by the caller.</summary>
    internal static BigDouble ComputeModifier(BigDouble drain, BigDouble completion,
        BigDouble penalizedSpeed, BigDouble requirementCost, BigDouble levelFactor,
        BigDouble instanceCostFactor, BigDouble instanceSpeedFactor, int quantity, int freeUses,
        BigDouble overdriveSpeedPercent, BigDouble overdriveDrainPercent)
    {
        var overdriveQuantity = new BigDouble(Math.Max(0, quantity - freeUses));
        var overdriveSpeed = new GameValueModifier(
                GameValueModifierType.MultiDiminishing,
                OrbGameMath.AsPercent(overdriveSpeedPercent) - BigDouble.One)
            .MultiplyScalar(overdriveQuantity).Adjust(BigDouble.One);
        var overdriveDrain = new GameValueModifier(
                GameValueModifierType.Reduction,
                BigDouble.One / OrbGameMath.AsPercent(overdriveDrainPercent) - BigDouble.One)
            .MultiplyScalar(overdriveQuantity).Adjust(BigDouble.One);
        var baseModifier = drain * OrbGameMath.AsPercent(completion) * OrbGameMath.AsPercent(penalizedSpeed) *
            requirementCost * levelFactor;
        // InstanceScalingRef.GetSpeedCostPercent multiplies its two factors before AlchemyInstance applies them.
        return baseModifier * (instanceCostFactor * instanceSpeedFactor) * overdriveSpeed * overdriveDrain;
    }

}

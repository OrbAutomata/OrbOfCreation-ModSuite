using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public class ResourceSO : UpgradeableObject
{
    private readonly Dictionary<Guid, BigDouble> activeUsage = new Dictionary<Guid, BigDouble>();
    /// <summary>The game's closed vocabulary of resource properties an effect can modify.</summary>
    public enum ModifiableType
    {
        Rate,
        MaxQuantity,
        MaxQuantityRate,
        Quality,
        GainRate,
        LossPercent,
        RestingRate,
        RateMaxPercent,
        AttributeCostMod,
        ReservationMod,
        RateInterestPercent,
        RateMissingPercent,
        MaxQuantityFunctional,
        RateLifetimePercent,
        RallyThreshold,
        RallyMod,
    }

    /// <summary>One structure's standing effect on one property of one resource.</summary>
    public sealed class PersistentEffect : IPersistentEffectScript
    {
        public ResourceSO? resource;
        public ModifiableType upgradeType;
        public ValueModifier modifier;
    }

    // The per-type registry the game keeps for every entity category, and the traversal entry point
    // world collection uses. Tests populate it directly.
    public static List<ResourceSO> All = new List<ResourceSO>();
    public List<ResourceTypeSO> resourceTypes = new List<ResourceTypeSO>();
    public new string name = "Resource";
    public BigDouble quantity = new BigDouble(1.0, 3);
    public BigDouble trueRate = new BigDouble(0.0, 0);
    public bool available = true;
    public bool visible = true;
    public ValueModifierRecord quality = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord maxQuantity = new ValueModifierRecord(new BigDouble(1.0, 4));

    // The rest of what world collection reads. Named exactly as the game names them, because the
    // collector binds by name and this file is the cheap early warning when a name moves.
    public BigDouble lifetimeQuantity;
    public BigDouble discoveryTime;
    public ValueModifierRecord gainRate = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord drain = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord reservationMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord usage = new ValueModifierRecord(new BigDouble(0.0, 0));

    public long appliedLevels;

    /// <summary>Where an experience resource pushes the levels it grants. Null for every other one.</summary>
    public IntVariable levelVariable;

    // The rate chain's own arguments.
    public ValueModifierRecord rate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateSplash = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateMaxPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateInterestPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateMissingPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rateLifetimePercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord lossPercent = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord displayRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public double baseLoss = 0.5;

    /// <summary>Private in the game too: it is recomputed rather than persisted.</summary>
    private BigDouble calcRarityValue;

    // The rest of what the runtime type carries. Enumerated from the ScriptableObject rather than
    // the save record, and not filtered by what writes them; see D17.
    public double rarityValue;
    public double rarityValueEnd;
    public double restEngageTime;
    public bool pauseLossOnChange;
    public bool canOverflow;
    public bool noOverflowRubberBand;
    public bool bandwidthResource;
    public bool invertedResource;
    public bool excludeFromGlobals;
    public bool startVisible;

    // What CheckVisibility() ANDs with startVisible to latch `visible`. The container the suite
    // publishes as a resource's unlock block, and the only authored gate a resource has.
    public Prerequisites.Container visiblePrerequisites = new Prerequisites.Container();
    public BigDouble appliedMaxQuantity;
    public int quantitySoftCapOrder;
    public int quantitySoftCapMagnitude;
    public double quantitySoftCapRatio;
    public bool debugResource;
    private double currentLossRate;
    private BigDouble lastReservation;
    private BigDouble debouncedReplenish;
    private BigDouble debouncedReverberate;
    private BigDouble debouncedDecay;
    private bool firstIncrement;
    public ValueModifierRecord maxQuantityRate = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord maxQuantityFunctional = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord restingRateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    // Authored at parity in the game, not at zero. A zero here reads as "the game has never
    // calculated this record" and withholds every price paid in this resource.
    public ValueModifierRecord attributeCostMod = new ValueModifierRecord(new BigDouble(1.0, 2));
    public ValueModifierRecord decayRatio = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord decayTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord replenishRatio = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord replenishTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord reverberateMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord reverberateTimeMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rallyThreshold = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord rallyMod = new ValueModifierRecord(new BigDouble(0.0, 0));
    public ValueModifierRecord usageDrainPenalty = new ValueModifierRecord(new BigDouble(0.0, 0));

    private bool inLossMode;
    private bool inRestMode;
    private bool inRallyMode;
    public override string GetName() => name;
    public BigDouble GetQuantity() => quantity;
    // The game's own formula. A settable field here would let the stub's GetTrueQuantity()
    // disagree with its own quantity and quality, which is precisely the drift that makes a
    // passing test mean nothing.
    public BigDouble GetTrueQuantity() =>
        quantity * BigDouble.Normalize(quality.GetValue().Mantissa, quality.GetValue().Exponent - 2);
    public BigDouble GetAttributeCostMod() => attributeCostMod.GetValue();
    public BigDouble GetTrueSpend(BigDouble amount) =>
        amount / BigDouble.Normalize(quality.GetValue().Mantissa, quality.GetValue().Exponent - 2);
    public Exception? TrueRateReadFailure { get; set; }
    public BigDouble GetTrueRate() => TrueRateReadFailure is { } failure ? throw failure : trueRate;
    public BigDouble GetModdedDrain() => drain.GetValue() / quality.GetValue().AsPercent();
    public bool IsAtZero() => quantity <= new BigDouble(0.01);
    public bool IsAvailable() => available;
    public bool IsVisible() => visible;
    public bool IsBandwidthResource() => bandwidthResource;
    public BigDouble GetMissing() => BigDouble.Max(maxQuantity.GetValue() - quantity, 0);
    public bool HasAmount(BigDouble amount) =>
        bandwidthResource ? GetMissing() >= amount : GetTrueQuantity() >= amount;
    public void AddUsage(Guid guid, BigDouble amount)
    {
        activeUsage[guid] = amount;
        if (bandwidthResource)
            quantity = activeUsage.Values.Aggregate(BigDouble.Zero, static (sum, value) => sum + value);
    }
    public void RemoveUsage(Guid guid)
    {
        activeUsage.Remove(guid);
        if (bandwidthResource)
            quantity = activeUsage.Values.Aggregate(BigDouble.Zero, static (sum, value) => sum + value);
    }
    public void Spend(BigDouble amount)
    {
        if (bandwidthResource)
        {
            quantity += amount;
            return;
        }

        var normalizedQuality = BigDouble.Normalize(
            quality.GetValue().Mantissa,
            quality.GetValue().Exponent - 2);
        quantity -= amount / normalizedQuality;
    }
    public BigDouble GetTrueAmount(BigDouble amount) => amount;
}

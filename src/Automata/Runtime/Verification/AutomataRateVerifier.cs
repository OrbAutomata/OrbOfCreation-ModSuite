using System;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;
using OrbModding.Common.Runtime.World;

namespace OrbAutomata;

/// <summary>
/// Compares the resource rate chain the suite computes against the chain the game computes, for real
/// resources in a live session.
/// </summary>
/// <remarks>
/// <para>
/// Every step is compared separately, not just the final <c>GetTrueRate()</c>. The game exposes each
/// intermediate as a public method, so a disagreement names the line that broke rather than the
/// chain that contains it. With a dozen inputs feeding seven steps, an end-only comparison would
/// report "the rate is wrong" and leave the search to a human.
/// </para>
/// <para>
/// <b>Input parity is the hard part here, and it did not arise for costs.</b> The suite folds every
/// modifier record itself rather than calling the accessor that would recalculate and write. The
/// game's own accessors do recalculate on read when their dirty flag is set. So the game's chain is
/// invoked once, untimed, before anything is read: that settles every dirty flag, after which both
/// sides provably compute from the same numbers — and the suite's side is read through the same fold
/// world collection uses, so this compares what the collector actually publishes rather than a
/// number only the verifier would ever see.
/// </para>
/// <para>
/// <b>Not a hot path.</b> This runs on demand for a bounded number of resources, so it reads through
/// plain reflection rather than compiled delegates. Verification code should be obviously correct
/// rather than fast, and it must not share machinery with the collector it is meant to check.
/// </para>
/// </remarks>
internal sealed class AutomataRateVerifier
{
    private readonly ResourceRateContract? _contract;

    internal AutomataRateVerifier(Type? resourceType, Type? playerType)
    {
        _contract = ResourceRateContract.TryResolve(resourceType, playerType);
    }

    /// <summary>Whether the native contract needed to verify at all was resolved.</summary>
    internal bool IsAvailable => _contract is not null;

    internal bool TryVerify(object resource, DifferentialRun run, out string failure) =>
        TryVerify(resource, run, timing: null, out failure);

    internal bool TryVerify(
        object resource,
        DifferentialRun run,
        DifferentialVerificationSession? timing,
        out string failure)
    {
        if (resource is null) throw new ArgumentNullException(nameof(resource));
        if (run is null) throw new ArgumentNullException(nameof(run));

        if (_contract is null)
        {
            failure = "The ResourceSO rate contract is unavailable on this build.";
            return false;
        }

        try
        {
            return TryVerifyCore(_contract, resource, run, timing, out failure);
        }
        catch (Exception ex)
        {
            failure = $"Reading a resource's rate inputs threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static bool TryVerifyCore(
        ResourceRateContract contract,
        object resource,
        DifferentialRun run,
        DifferentialVerificationSession? timing,
        out string failure)
    {
        var entityId = contract.ReadGuid(resource);

        // Settle every dirty flag before either side reads anything, so the comparison is about the
        // arithmetic rather than about which side saw a newer input. Untimed on purpose: this call
        // does work the suite's design exists to avoid, and charging it to the game would flatter the
        // measurement.
        contract.InvokeTrueRate(resource);

        var inputs = contract.ReadInputs(resource);

        var ourStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var ourRate = GameResourceRateMath.GetTrueRate(in inputs);
        var ourTicks = System.Diagnostics.Stopwatch.GetTimestamp() - ourStart;

        // Timed on a clean cache, so the game is charged for its arithmetic and not for the
        // recalculation the warm-up already absorbed. That understates the real margin, which is the
        // direction a self-interested measurement should err in.
        var theirStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var theirRate = contract.InvokeTrueRate(resource);
        var theirTicks = System.Diagnostics.Stopwatch.GetTimestamp() - theirStart;
        timing?.RecordTiming(ourTicks, theirTicks);

        run.Compare(entityId, "GetMissing", GameResourceRateMath.GetMissing(in inputs), contract.InvokeMissing(resource));
        run.Compare(entityId, "GetModdedDrain", GameResourceRateMath.GetModdedDrain(in inputs), contract.InvokeModdedDrain(resource));
        run.Compare(entityId, "GetLossRate", GameResourceRateMath.GetLossRate(in inputs), contract.InvokeLossRate(resource));
        run.Compare(entityId, "GetQuantityNegRate", GameResourceRateMath.GetQuantityNegRate(in inputs), contract.InvokeQuantityNegRate(resource));
        run.Compare(entityId, "GetDisplayRate", GameResourceRateMath.GetDisplayRate(in inputs), contract.InvokeDisplayRate(resource));
        run.Compare(entityId, "GetModdedFlatRate", GameResourceRateMath.GetModdedFlatRate(in inputs), contract.InvokeModdedFlatRate(resource));

        // Booleans travel as 1 and 0 so a branch disagreement is reported by the same machinery that
        // reports a numeric one. HasActiveRate decides which of GetTrueRate's three branches runs, so
        // it fails loudly here rather than as an unexplained rate mismatch.
        run.Compare(
            entityId,
            "HasActiveRate",
            GameResourceRateMath.HasActiveRate(in inputs) ? 1 : 0,
            contract.InvokeHasActiveRate(resource) ? 1 : 0);

        run.Compare(entityId, "GetTrueRate", ourRate, theirRate);

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// The reflected members required to read a resource's rate inputs and the game's own answers.
    /// Resolved once; a missing member makes the whole verifier unavailable rather than partial.
    /// </summary>
    private sealed class ResourceRateContract
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private const BindingFlags Static =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

        private readonly MethodInfo _getGuid;

        // Modifier records, read as fields and folded exactly as world collection folds them.
        private readonly FieldInfo[] _records;
        private readonly NativeModifierRecordAccess _record;
        private readonly MethodInfo _hasActiveElements;

        // Plain fields.
        private readonly FieldInfo _quantity;
        private readonly FieldInfo _lifetimeQuantity;
        private readonly FieldInfo _calcRarityValue;
        private readonly FieldInfo _baseLoss;
        private readonly FieldInfo _visible;
        private readonly FieldInfo _inLossMode;

        // The game's own answers.
        private readonly MethodInfo _getTrueRate;
        private readonly MethodInfo _getMissing;
        private readonly MethodInfo _getModdedDrain;
        private readonly MethodInfo _getLossRate;
        private readonly MethodInfo _getQuantityNegRate;
        private readonly MethodInfo _getDisplayRate;
        private readonly MethodInfo _getModdedFlatRate;
        private readonly MethodInfo _hasActiveRate;

        // Per-tick globals.
        private readonly MethodInfo _resourceOverflow;
        private readonly MethodInfo _resourceOverflowLoss;
        private readonly MethodInfo _resetTimePassed;
        private readonly FieldInfo _variableValue;

        // Index into _records, in the order the input struct wants them.
        private const int Rate = 0;
        private const int RateSplash = 1;
        private const int RateMaxPercent = 2;
        private const int RateInterestPercent = 3;
        private const int RateMissingPercent = 4;
        private const int RateLifetimePercent = 5;
        private const int MaxQuantity = 6;
        private const int Quality = 7;
        private const int GainRate = 8;
        private const int Drain = 9;
        private const int LossPercent = 10;
        private const int DisplayRate = 11;

        private static readonly string[] RecordNames =
        {
            "rate", "rateSplash", "rateMaxPercent", "rateInterestPercent", "rateMissingPercent",
            "rateLifetimePercent", "maxQuantity", "quality", "gainRate", "drain", "lossPercent",
            "displayRate",
        };

        private ResourceRateContract(
            MethodInfo getGuid,
            FieldInfo[] records,
            NativeModifierRecordAccess record,
            MethodInfo hasActiveElements,
            FieldInfo quantity,
            FieldInfo lifetimeQuantity,
            FieldInfo calcRarityValue,
            FieldInfo baseLoss,
            FieldInfo visible,
            FieldInfo inLossMode,
            MethodInfo getTrueRate,
            MethodInfo getMissing,
            MethodInfo getModdedDrain,
            MethodInfo getLossRate,
            MethodInfo getQuantityNegRate,
            MethodInfo getDisplayRate,
            MethodInfo getModdedFlatRate,
            MethodInfo hasActiveRate,
            MethodInfo resourceOverflow,
            MethodInfo resourceOverflowLoss,
            MethodInfo resetTimePassed,
            FieldInfo variableValue)
        {
            _getGuid = getGuid;
            _records = records;
            _record = record;
            _hasActiveElements = hasActiveElements;
            _quantity = quantity;
            _lifetimeQuantity = lifetimeQuantity;
            _calcRarityValue = calcRarityValue;
            _baseLoss = baseLoss;
            _visible = visible;
            _inLossMode = inLossMode;
            _getTrueRate = getTrueRate;
            _getMissing = getMissing;
            _getModdedDrain = getModdedDrain;
            _getLossRate = getLossRate;
            _getQuantityNegRate = getQuantityNegRate;
            _getDisplayRate = getDisplayRate;
            _getModdedFlatRate = getModdedFlatRate;
            _hasActiveRate = hasActiveRate;
            _resourceOverflow = resourceOverflow;
            _resourceOverflowLoss = resourceOverflowLoss;
            _resetTimePassed = resetTimePassed;
            _variableValue = variableValue;
        }

        internal static ResourceRateContract? TryResolve(Type? resourceType, Type? playerType)
        {
            if (resourceType is null || playerType is null) return null;

            var records = new FieldInfo[RecordNames.Length];
            for (var index = 0; index < RecordNames.Length; index++)
            {
                var field = resourceType.GetField(RecordNames[index], Instance);
                if (field is null) return null;
                records[index] = field;
            }

            var recordType = records[0].FieldType;
            var record = NativeModifierRecordAccess.For(recordType);
            var hasActiveElements = FindNoArg(recordType, "HasActiveElements");

            var getGuid = RuntimeIdentityRegistryBinding.Shared.StableUuidMethod(resourceType);
            var quantity = resourceType.GetField("quantity", Instance);
            var lifetimeQuantity = resourceType.GetField("lifetimeQuantity", Instance);
            var calcRarityValue = resourceType.GetField("calcRarityValue", Instance);
            var baseLoss = resourceType.GetField("baseLoss", Instance);
            var visible = resourceType.GetField("visible", Instance);
            var inLossMode = resourceType.GetField("inLossMode", Instance);

            var getTrueRate = FindNoArg(resourceType, "GetTrueRate");
            var getMissing = FindNoArg(resourceType, "GetMissing");
            var getModdedDrain = FindNoArg(resourceType, "GetModdedDrain");
            var getDisplayRate = FindNoArg(resourceType, "GetDisplayRate");
            var getModdedFlatRate = FindNoArg(resourceType, "GetModdedFlatRate");
            var hasActiveRate = FindNoArg(resourceType, "HasActiveRate");
            var getLossRate = FindOneBool(resourceType, "GetLossRate");
            var getQuantityNegRate = FindOneBool(resourceType, "GetQuantityNegRate");

            var resourceOverflow = FindStaticNoArg(playerType, "GetResourceOverflow");
            var resourceOverflowLoss = FindStaticNoArg(playerType, "GetResourceOverflowLoss");
            var resetTimePassed = FindStaticNoArg(playerType, "GetResetTimePassed");
            var variableValue = resourceOverflow?.ReturnType.GetField("value", Instance);

            if (record is null || hasActiveElements is null || getGuid is null ||
                quantity is null || lifetimeQuantity is null || calcRarityValue is null ||
                baseLoss is null || visible is null || inLossMode is null ||
                getTrueRate is null || getMissing is null || getModdedDrain is null ||
                getDisplayRate is null || getModdedFlatRate is null || hasActiveRate is null ||
                getLossRate is null || getQuantityNegRate is null ||
                resourceOverflow is null || resourceOverflowLoss is null ||
                resetTimePassed is null || variableValue is null)
            {
                return null;
            }

            return new ResourceRateContract(
                getGuid, records, record, hasActiveElements, quantity, lifetimeQuantity,
                calcRarityValue, baseLoss, visible, inLossMode, getTrueRate, getMissing,
                getModdedDrain, getLossRate, getQuantityNegRate, getDisplayRate, getModdedFlatRate,
                hasActiveRate, resourceOverflow, resourceOverflowLoss, resetTimePassed, variableValue);
        }

        internal Guid ReadGuid(object resource) =>
            _getGuid.Invoke(resource, null) is Guid guid ? guid : Guid.Empty;

        internal GameResourceRateInputs ReadInputs(object resource)
        {
            var inputs = default(GameResourceRateInputs);

            inputs.Rate = RecordValue(resource, Rate);
            inputs.RateSplash = RecordValue(resource, RateSplash);
            inputs.RateMaxPercent = RecordValue(resource, RateMaxPercent);
            inputs.RateInterestPercent = RecordValue(resource, RateInterestPercent);
            inputs.RateMissingPercent = RecordValue(resource, RateMissingPercent);
            inputs.RateLifetimePercent = RecordValue(resource, RateLifetimePercent);
            inputs.MaxQuantity = RecordValue(resource, MaxQuantity);
            inputs.Quality = RecordValue(resource, Quality);
            inputs.GainRate = RecordValue(resource, GainRate);
            inputs.Drain = RecordValue(resource, Drain);
            inputs.LossPercent = RecordValue(resource, LossPercent);
            inputs.DisplayRate = RecordValue(resource, DisplayRate);

            inputs.Quantity = ToBigDouble(_quantity.GetValue(resource));
            inputs.LifetimeQuantity = ToBigDouble(_lifetimeQuantity.GetValue(resource));
            inputs.CalcRarityValue = ToBigDouble(_calcRarityValue.GetValue(resource));
            inputs.BaseLoss = Convert.ToDouble(_baseLoss.GetValue(resource));
            inputs.Visible = _visible.GetValue(resource) is true;
            inputs.InLossMode = _inLossMode.GetValue(resource) is true;

            inputs.RateHasActive = RecordHasActive(resource, Rate);
            inputs.RateSplashHasActive = RecordHasActive(resource, RateSplash);
            inputs.RateMaxPercentHasActive = RecordHasActive(resource, RateMaxPercent);
            inputs.RateInterestPercentHasActive = RecordHasActive(resource, RateInterestPercent);
            inputs.RateMissingPercentHasActive = RecordHasActive(resource, RateMissingPercent);
            inputs.RateLifetimePercentHasActive = RecordHasActive(resource, RateLifetimePercent);

            // The globals are read through the same cached-field path, since DoubleVariable.AsPercent()
            // is another accessor that recalculates on a dirty record.
            inputs.ResourceOverflowPercent = OrbGameMath.AsPercent(GlobalValue(_resourceOverflow));
            inputs.ResourceOverflowLossPercent = OrbGameMath.AsPercent(GlobalValue(_resourceOverflowLoss));
            inputs.ResetTimePassed = GlobalValue(_resetTimePassed);
            inputs.FixedDeltaTime = UnityEngine.Time.fixedDeltaTime;

            return inputs;
        }

        internal BigDouble InvokeTrueRate(object resource) => ToBigDouble(_getTrueRate.Invoke(resource, null));
        internal BigDouble InvokeMissing(object resource) => ToBigDouble(_getMissing.Invoke(resource, null));
        internal BigDouble InvokeModdedDrain(object resource) => ToBigDouble(_getModdedDrain.Invoke(resource, null));
        internal BigDouble InvokeDisplayRate(object resource) => ToBigDouble(_getDisplayRate.Invoke(resource, null));
        internal BigDouble InvokeModdedFlatRate(object resource) => ToBigDouble(_getModdedFlatRate.Invoke(resource, null));
        internal bool InvokeHasActiveRate(object resource) => _hasActiveRate.Invoke(resource, null) is true;

        internal BigDouble InvokeLossRate(object resource) =>
            ToBigDouble(_getLossRate.Invoke(resource, FalseArgument));

        internal BigDouble InvokeQuantityNegRate(object resource) =>
            ToBigDouble(_getQuantityNegRate.Invoke(resource, FalseArgument));

        private static readonly object[] FalseArgument = { false };

        private BigDouble RecordValue(object resource, int index)
        {
            var record = _records[index].GetValue(resource);
            return record is null ? BigDouble.NaN : _record.Fold(record);
        }

        private bool RecordHasActive(object resource, int index)
        {
            var record = _records[index].GetValue(resource);
            return record is not null && _hasActiveElements.Invoke(record, null) is true;
        }

        private BigDouble GlobalValue(MethodInfo accessor)
        {
            var variable = accessor.Invoke(null, null);
            if (variable is null) return BigDouble.NaN;

            var record = _variableValue.GetValue(variable);
            return record is null ? BigDouble.NaN : _record.Fold(record);
        }

        private static MethodInfo? FindNoArg(Type type, string name) =>
            type.GetMethod(name, Instance, null, Type.EmptyTypes, null);

        private static MethodInfo? FindStaticNoArg(Type type, string name) =>
            type.GetMethod(name, Static, null, Type.EmptyTypes, null);

        private static MethodInfo? FindOneBool(Type type, string name) =>
            type.GetMethod(name, Instance, null, new[] { typeof(bool) }, null);

        private static BigDouble ToBigDouble(object? value) =>
            value is BigDouble big ? big : BigDouble.NaN;
    }
}

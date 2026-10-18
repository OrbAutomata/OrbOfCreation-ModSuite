using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata;

/// <summary>
/// Compares the cost the suite computes against the cost the game computes, for real entities in a
/// live session.
/// </summary>
/// <remarks>
/// <para>
/// This is the only check that can prove the ported math is right. The offline tests around
/// <see cref="GameCostMath"/> assert against values derived by hand from the decompiled source, so
/// a misreading of the original would be reproduced identically in both the port and its expected
/// value and would pass silently. The running game is the sole oracle for its own arithmetic.
/// </para>
/// <para>
/// <b>Scope.</b> It verifies the chain that was ported, not the sub-values feeding it. The
/// per-resource attribute modifier is read from the game and supplied as an input, because it was not
/// transcribed. <c>GetNextCostMod()</c> is ported, compared in its own right, and then fed to the
/// chain; what is under test is whether the same inputs run through the same sequence produce the
/// same result.
/// </para>
/// <para>
/// <b>Not a hot path.</b> This runs on demand for a bounded number of entities, so it reads through
/// plain reflection rather than compiled delegates. Verification code should be obviously correct
/// rather than fast, and it must not share machinery with the collector it is meant to check.
/// </para>
/// <para>
/// It fails closed: any member it cannot resolve or read aborts that entity as unverifiable rather
/// than counting as agreement. A verifier that quietly skips what it cannot read would report a
/// clean pass for a port nobody checked.
/// </para>
/// </remarks>
internal sealed class AutomataCostVerifier
{
    private readonly StructureCostContract? _contract;

    internal AutomataCostVerifier(Type structureType)
    {
        _contract = StructureCostContract.TryResolve(structureType);
    }

    /// <summary>Whether the native contract needed to verify at all was resolved.</summary>
    internal bool IsAvailable => _contract is not null;

    /// <summary>
    /// Verifies one structure, recording every per-resource comparison into <paramref name="run"/>.
    /// Returns false when the entity could not be read at all, which is distinct from disagreeing.
    /// </summary>
    internal bool TryVerify(object structure, DifferentialRun run, out string failure) =>
        TryVerify(structure, run, timing: null, out failure);

    /// <summary>
    /// Verifies one structure, optionally recording how long each side took into
    /// <paramref name="timing"/>.
    /// </summary>
    internal bool TryVerify(
        object structure,
        DifferentialRun run,
        DifferentialVerificationSession? timing,
        out string failure)
    {
        if (structure is null) throw new ArgumentNullException(nameof(structure));
        if (run is null) throw new ArgumentNullException(nameof(run));

        if (_contract is null)
        {
            failure = "The StructureSO cost contract is unavailable on this build.";
            return false;
        }

        try
        {
            return TryVerifyCore(_contract, structure, run, timing, out failure);
        }
        catch (Exception ex)
        {
            // A throwing accessor means the contract does not mean what we think on this build.
            failure = $"Reading the structure's cost inputs threw: {ex.GetBaseException().Message}";
            return false;
        }
    }

    private static bool TryVerifyCore(
        StructureCostContract contract,
        object structure,
        DifferentialRun run,
        DifferentialVerificationSession? timing,
        out string failure)
    {
        var entityId = contract.ReadGuid(structure);

        // Our side: rebuild the cost from the same raw inputs the game starts from.
        if (!contract.TryReadBaseCost(structure, out var ours, out var attributeMods, out failure)) return false;

        var perQuantity = contract.ReadCostPerQuantityModifier(structure);
        if (perQuantity is not { } modifier)
        {
            failure = "The per-quantity cost modifier could not be decomposed on this build.";
            return false;
        }

        var costScaling = contract.ReadCostScalingPercent(structure);
        var committed = contract.ReadCommittedQuantity(structure);

        // The multiplier is compared in its own right and then used, so the end-to-end cost below is
        // the one the suite actually publishes rather than one propped up by the game's answer.
        if (contract.ComputeNextCostMod(structure, in modifier) is not { } ourNextCostMod)
        {
            failure = "The player's structure cost multiplier could not be read.";
            return false;
        }

        run.Compare(entityId, "next-cost-mod", ourNextCostMod, contract.ReadNextCostMod(structure));
        var nextCostMod = OrbGameMath.AsPercent(ourNextCostMod);

        // Timed around the arithmetic only. Reading the inputs is excluded because the collector
        // will grab those once for every consumer, whereas the game recomputes on every ask —
        // measuring the read here would charge our side for work the design removes.
        var ourStart = System.Diagnostics.Stopwatch.GetTimestamp();
        GameCostMath.ComputeNextCost(
            ours,
            attributeMods,
            in modifier,
            costScalingModPercent: costScaling,
            committedQuantity: committed,
            nextCostModPercent: nextCostMod);
        var ourTicks = System.Diagnostics.Stopwatch.GetTimestamp() - ourStart;

        // Their side: ask the game, which is what this whole exercise exists to stop doing.
        //
        // Only the game's own call is timed. Decoding the returned list back into value rows is
        // this verifier's overhead — roughly three reflective reads per cost entry — and charging
        // the game for it would inflate the comparison in our favour. The measurement has to be
        // one we would still believe if it came out against us.
        var theirStart = System.Diagnostics.Stopwatch.GetTimestamp();
        var nativeCost = contract.InvokePurchaseCost(structure);
        var theirTicks = System.Diagnostics.Stopwatch.GetTimestamp() - theirStart;
        timing?.RecordTiming(ourTicks, theirTicks);

        if (!contract.TryDecodeCostList(nativeCost, out var theirs, out failure)) return false;

        if (theirs.Length != ours.Length)
        {
            failure = $"Cost entry count differs: ours={ours.Length} theirs={theirs.Length}.";
            return false;
        }

        for (var index = 0; index < ours.Length; index++)
        {
            // Positional comparison is valid because both sides derive from the same baseCost list
            // in the same order; a resource-identity mismatch means that assumption broke.
            if (ours[index].ResourceId != theirs[index].ResourceId)
            {
                failure = $"Cost entry {index} is a different resource on each side.";
                return false;
            }

            run.Compare(entityId, ours[index].ResourceId.ToString(), ours[index].Value, theirs[index].Value);
        }

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// The reflected members required to read a structure's cost inputs and its computed cost.
    /// Resolved once; a missing member makes the whole verifier unavailable rather than partial.
    /// </summary>
    private sealed class StructureCostContract
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private StructureCostContract(
            Type structureType,
            FieldInfo baseCost,
            FieldInfo costPerQuantity,
            FieldInfo costScalingMod,
            FieldInfo quantity,
            FieldInfo queuedQuantity,
            MethodInfo getNextCostMod,
            MethodInfo getPurchaseCost,
            MethodInfo getGuid,
            FieldInfo costEntries,
            FieldInfo tupleResource,
            MethodInfo tupleGetValue,
            MethodInfo resourceGetGuid,
            MethodInfo resourceAttributeCostMod,
            MethodInfo asPercent,
            MethodInfo getModifier,
            MethodInfo recordAsPercent,
            FieldInfo modifierType,
            FieldInfo modifierAmount,
            FieldInfo modifierOrder,
            FieldInfo passiveCostMod,
            FieldInfo activeCostMod,
            MethodInfo recordValue,
            MethodInfo playerStructureCost,
            MethodInfo variableAsPercent)
        {
            StructureType = structureType;
            _baseCost = baseCost;
            _costPerQuantity = costPerQuantity;
            _costScalingMod = costScalingMod;
            _quantity = quantity;
            _queuedQuantity = queuedQuantity;
            _getNextCostMod = getNextCostMod;
            _getPurchaseCost = getPurchaseCost;
            _getGuid = getGuid;
            _costEntries = costEntries;
            _tupleResource = tupleResource;
            _tupleGetValue = tupleGetValue;
            _resourceGetGuid = resourceGetGuid;
            _resourceAttributeCostMod = resourceAttributeCostMod;
            _asPercent = asPercent;
            _getModifier = getModifier;
            _recordAsPercent = recordAsPercent;
            _modifierType = modifierType;
            _modifierAmount = modifierAmount;
            _modifierOrder = modifierOrder;
            _passiveCostMod = passiveCostMod;
            _activeCostMod = activeCostMod;
            _recordValue = recordValue;
            _playerStructureCost = playerStructureCost;
            _variableAsPercent = variableAsPercent;
        }

        internal Type StructureType { get; }

        private readonly FieldInfo _baseCost;
        private readonly FieldInfo _costPerQuantity;
        private readonly FieldInfo _costScalingMod;
        private readonly FieldInfo _quantity;
        private readonly FieldInfo _queuedQuantity;
        private readonly MethodInfo _getNextCostMod;
        private readonly MethodInfo _getPurchaseCost;
        private readonly MethodInfo _getGuid;
        private readonly FieldInfo _costEntries;
        private readonly FieldInfo _tupleResource;
        private readonly MethodInfo _tupleGetValue;
        private readonly MethodInfo _resourceGetGuid;
        private readonly MethodInfo _resourceAttributeCostMod;
        private readonly MethodInfo _asPercent;
        private readonly MethodInfo _getModifier;
        private readonly MethodInfo _recordAsPercent;
        private readonly FieldInfo _modifierType;
        private readonly FieldInfo _modifierAmount;
        private readonly FieldInfo _modifierOrder;
        private readonly FieldInfo _passiveCostMod;
        private readonly FieldInfo _activeCostMod;
        private readonly MethodInfo _recordValue;
        private readonly MethodInfo _playerStructureCost;
        private readonly MethodInfo _variableAsPercent;

        internal static StructureCostContract? TryResolve(Type? structureType)
        {
            if (structureType is null) return null;

            var baseCost = structureType.GetField("baseCost", Instance);
            var costPerQuantity = structureType.GetField("costPerQuantity", Instance);
            var costScalingMod = structureType.GetField("costScalingMod", Instance);
            var quantity = structureType.GetField("quantity", Instance);
            var queuedQuantity = structureType.GetField("queuedQuantity", Instance);
            var getNextCostMod = FindNoArg(structureType, "GetNextCostMod");
            var getPurchaseCost = FindNoArg(structureType, "GetPurchaseCost");
            var getGuid = RuntimeIdentityRegistryBinding.Shared.StableUuidMethod(structureType);
            if (baseCost is null || costPerQuantity is null || costScalingMod is null ||
                quantity is null || queuedQuantity is null || getNextCostMod is null ||
                getPurchaseCost is null || getGuid is null)
            {
                return null;
            }

            var costListType = baseCost.FieldType;
            var costEntries = costListType.GetField("costs", Instance);
            if (costEntries is null) return null;

            var tupleType = ExtractElementType(costEntries.FieldType);
            var tupleResource = tupleType?.GetField("resource", Instance);
            var tupleGetValue = tupleType is null ? null : FindNoArg(tupleType, "GetValue");
            if (tupleResource is null || tupleGetValue is null) return null;

            var resourceType = tupleResource.FieldType;
            var resourceGetGuid = RuntimeIdentityRegistryBinding.Shared.StableUuidMethod(resourceType);
            var resourceAttributeCostMod = FindNoArg(resourceType, "GetAttributeCostMod");
            if (resourceGetGuid is null || resourceAttributeCostMod is null) return null;

            var asPercent = FindNoArg(resourceAttributeCostMod.ReturnType, "AsPercent");
            var getModifier = FindNoArg(costPerQuantity.FieldType, "GetModifier");
            var recordAsPercent = FindNoArg(costScalingMod.FieldType, "AsPercent");
            if (asPercent is null || getModifier is null || recordAsPercent is null) return null;

            var modifierType = getModifier.ReturnType;
            var typeField = modifierType.GetField("type", Instance);
            var amountField = modifierType.GetField("adjustReal", Instance);
            var orderField = modifierType.GetField("order", Instance);
            if (typeField is null || amountField is null || orderField is null) return null;

            // The inputs to the ported GetNextCostMod. Read through GetValue() rather than the cached
            // field: a verifier settles dirty records on purpose so both sides compare the same
            // numbers, which is the opposite of what collection does and right for the same reason.
            var passiveCostMod = structureType.GetField("passiveCostMod", Instance);
            var activeCostMod = structureType.GetField("activeCostMod", Instance);
            var recordValue = FindNoArg(costScalingMod.FieldType, "GetValue");
            if (passiveCostMod is null || activeCostMod is null || recordValue is null) return null;

            var player = ReflectionUtil.FindLoadedType("Player");
            var playerStructureCost = player is null ? null : FindStatic(player, "GetStructureCost");
            var variableAsPercent = playerStructureCost is null
                ? null
                : FindNoArg(playerStructureCost.ReturnType, "AsPercent");
            if (playerStructureCost is null || variableAsPercent is null) return null;

            return new StructureCostContract(
                structureType, baseCost, costPerQuantity, costScalingMod, quantity, queuedQuantity,
                getNextCostMod, getPurchaseCost, getGuid, costEntries, tupleResource, tupleGetValue,
                resourceGetGuid, resourceAttributeCostMod, asPercent, getModifier, recordAsPercent,
                typeField, amountField, orderField, passiveCostMod, activeCostMod, recordValue,
                playerStructureCost, variableAsPercent);
        }

        internal Guid ReadGuid(object structure) =>
            _getGuid.Invoke(structure, null) is Guid guid ? guid : Guid.Empty;

        internal BigDouble ReadCostScalingPercent(object structure) =>
            ToBigDouble(_recordAsPercent.Invoke(_costScalingMod.GetValue(structure), null));

        /// <summary>The game's own next-cost multiplier, unscaled — what the port must reproduce.</summary>
        internal BigDouble ReadNextCostMod(object structure) =>
            ToBigDouble(_getNextCostMod.Invoke(structure, null));

        /// <summary>
        /// The same multiplier computed by the port, from inputs read one by one. Null when the
        /// player global is not there to read, which leaves the structure unverified.
        /// </summary>
        internal BigDouble? ComputeNextCostMod(object structure, in GameValueModifier costPerQuantity)
        {
            var global = _playerStructureCost.Invoke(null, null);
            if (global is null) return null;

            return GameCostMath.ComputeNextCostMod(
                ToBigDouble(_recordValue.Invoke(_passiveCostMod.GetValue(structure), null)),
                ToBigDouble(_recordValue.Invoke(_activeCostMod.GetValue(structure), null)),
                in costPerQuantity,
                ReadCommittedQuantity(structure),
                ToBigDouble(_variableAsPercent.Invoke(global, null)));
        }

        internal BigDouble ReadCommittedQuantity(object structure)
        {
            var owned = Convert.ToInt64(_quantity.GetValue(structure));
            var queued = Convert.ToInt64(_queuedQuantity.GetValue(structure));
            return new BigDouble(owned + queued);
        }

        internal GameValueModifier? ReadCostPerQuantityModifier(object structure)
        {
            var reference = _costPerQuantity.GetValue(structure);
            if (reference is null) return null;

            var modifier = _getModifier.Invoke(reference, null);
            if (modifier is null) return null;

            var rawType = _modifierType.GetValue(modifier);
            if (rawType is null) return null;

            var amount = ToBigDouble(_modifierAmount.GetValue(modifier));
            var order = Convert.ToInt32(_modifierOrder.GetValue(modifier));

            // The game's enum ordering is the one GameValueModifierType was ported from, so the
            // numeric value maps across directly. An unknown member fails closed rather than
            // defaulting to Raw, which would silently change the arithmetic.
            var ordinal = Convert.ToInt32(rawType);
            if (!Enum.IsDefined(typeof(GameValueModifierType), ordinal)) return null;

            return new GameValueModifier((GameValueModifierType)ordinal, amount, order);
        }

        internal bool TryReadBaseCost(
            object structure,
            out GameResourceCost[] costs,
            out BigDouble[] attributeModPercents,
            out string failure) =>
            TryReadCostList(_baseCost.GetValue(structure), readAttributeMods: true, out costs, out attributeModPercents, out failure);

        /// <summary>Calls the game's own cost computation and nothing else, so it can be timed alone.</summary>
        internal object? InvokePurchaseCost(object structure) => _getPurchaseCost.Invoke(structure, null);

        /// <summary>Decodes a native cost list into value rows. Verifier overhead, deliberately untimed.</summary>
        internal bool TryDecodeCostList(object? costList, out GameResourceCost[] costs, out string failure) =>
            TryReadCostList(costList, readAttributeMods: false, out costs, out _, out failure);

        private bool TryReadCostList(
            object? costList,
            bool readAttributeMods,
            out GameResourceCost[] costs,
            out BigDouble[] attributeModPercents,
            out string failure)
        {
            costs = Array.Empty<GameResourceCost>();
            attributeModPercents = Array.Empty<BigDouble>();

            if (costList is null)
            {
                failure = "A cost list was null.";
                return false;
            }

            if (_costEntries.GetValue(costList) is not IList entries)
            {
                failure = "A cost list did not expose its entries as a list.";
                return false;
            }

            costs = new GameResourceCost[entries.Count];
            attributeModPercents = readAttributeMods ? new BigDouble[entries.Count] : Array.Empty<BigDouble>();

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry is null)
                {
                    failure = $"Cost entry {index} was null.";
                    return false;
                }

                var resource = _tupleResource.GetValue(entry);
                if (resource is null)
                {
                    failure = $"Cost entry {index} had no resource.";
                    return false;
                }

                var resourceId = _resourceGetGuid.Invoke(resource, null) is Guid guid ? guid : Guid.Empty;
                var value = ToBigDouble(_tupleGetValue.Invoke(entry, null));
                costs[index] = new GameResourceCost(resourceId, value);

                if (readAttributeMods)
                {
                    var mod = _resourceAttributeCostMod.Invoke(resource, null);
                    attributeModPercents[index] = ToBigDouble(_asPercent.Invoke(mod, null));
                }
            }

            failure = string.Empty;
            return true;
        }

        private static Type? ExtractElementType(Type listType) =>
            listType.IsGenericType ? listType.GetGenericArguments()[0] : null;

        private static MethodInfo? FindNoArg(Type type, string name) =>
            type.GetMethod(name, Instance, null, Type.EmptyTypes, null);

        private static MethodInfo? FindStatic(Type type, string name) =>
            type.GetMethod(
                name,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                Type.EmptyTypes,
                null);

        private static BigDouble ToBigDouble(object? value) =>
            value is BigDouble big ? big : BigDouble.NaN;
    }
}

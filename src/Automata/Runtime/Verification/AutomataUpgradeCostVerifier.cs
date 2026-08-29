using System;
using System.Collections;
using System.Reflection;
using OrbModding.Common.Runtime.GameMath;

namespace OrbAutomata;

/// <summary>
/// Compares the ported upgrade cost curve against the game's own, at several levels rather than
/// only at the one the upgrade is standing on.
/// </summary>
/// <remarks>
/// <para>
/// The world-collection check already compares every published upgrade price against
/// <c>GetPurchaseCost()</c>, which is the curve evaluated at the current level. That proves one
/// point. It cannot tell a correct curve from one whose per-level modifiers and exponents are scaled
/// by the wrong term, because at the level the entity happens to be on the two agree — and the
/// scalar is <c>level - 1</c>, so an off-by-one in it is invisible for every upgrade sitting at
/// level 1. Asking the same question a few levels out is what separates them.
/// </para>
/// <para>
/// <b>Only unclamped levels are compared.</b> The game stops advancing the priced level at
/// <c>maxLevel - 1</c> for a finite upgrade, and reproducing that ceiling here to decide what to
/// expect would put the same transcription on both sides of the comparison. An offset that would
/// clamp is skipped instead, leaving the ceiling itself as a gap this pass does not claim to close.
/// </para>
/// <para>
/// <b>The native cache is restored.</b> <c>GetLeveledCostList()</c> memoises its result against the
/// level it was asked for, so probing ahead leaves the upgrade quoting a price for a level nobody
/// asked about. The last call this verifier makes is always the offset the game itself would ask
/// for, which puts the cache back where the next render expects it.
/// </para>
/// </remarks>
internal sealed class AutomataUpgradeCostVerifier
{
    /// <summary>
    /// Where the curve is sampled. Zero is the point the published table already covers and is kept
    /// so a disagreement can be attributed to the level rather than to the pass; one and four are
    /// spaced so that a scalar off by one and a scalar off by a factor cannot both survive.
    /// </summary>
    private static readonly int[] ProbeOffsets = { 0, 1, 4 };

    private readonly UpgradeCostContract? _contract;

    internal AutomataUpgradeCostVerifier(Type upgradeType) =>
        _contract = UpgradeCostContract.TryResolve(upgradeType);

    internal bool IsAvailable => _contract is not null;

    /// <summary>
    /// Verifies one upgrade at every offset that does not clamp. An upgrade with no comparable
    /// offset is recorded as an expected skip rather than as agreement.
    /// </summary>
    internal bool TryVerify(
        object upgrade,
        DifferentialRun run,
        DifferentialVerificationSession session,
        out string failure)
    {
        if (upgrade is null) throw new ArgumentNullException(nameof(upgrade));
        if (run is null) throw new ArgumentNullException(nameof(run));
        if (session is null) throw new ArgumentNullException(nameof(session));

        if (_contract is null)
        {
            failure = "The UpgradeSO cost contract is unavailable on this build.";
            return false;
        }

        try
        {
            return TryVerifyCore(_contract, upgrade, run, session, out failure);
        }
        catch (Exception ex)
        {
            failure = $"Reading an upgrade's cost inputs threw: {ex.GetBaseException().Message}";
            return false;
        }
        finally
        {
            // Whatever happened above, the upgrade goes back to quoting the level the game asks for.
            _contract?.RestoreCache(upgrade);
        }
    }

    private static bool TryVerifyCore(
        UpgradeCostContract contract,
        object upgrade,
        DifferentialRun run,
        DifferentialVerificationSession session,
        out string failure)
    {
        var entityId = contract.ReadGuid(upgrade);
        if (!contract.TryReadAuthoredCost(upgrade, out var authored, out failure)) return false;
        if (!contract.TryReadPerLevelModifiers(upgrade, out var modifiers, out var exponents, out failure))
            return false;

        var committed = contract.ReadCommittedLevel(upgrade);
        var ceiling = contract.ReadPricedCeiling(upgrade);

        var scratch = new GameResourceCost[authored.Length];
        var scaled = new GameValueModifier[modifiers.Length];
        var scaledExponents = new GameValueModifier[exponents.Length];
        var combineScratch = new GameValueModifier[modifiers.Length];

        var compared = 0;
        foreach (var offset in ProbeOffsets)
        {
            if (committed + offset > ceiling) continue;

            Array.Copy(authored, scratch, authored.Length);
            GameCostMath.ComputeLeveledCost(
                scratch,
                modifiers,
                exponents,
                committed + offset + 1,
                scaled,
                scaledExponents,
                combineScratch);

            var native = contract.InvokeLeveledCost(upgrade, offset);
            if (!contract.TryDecodeCostList(native, out var theirs, out failure)) return false;

            if (theirs.Length != scratch.Length)
            {
                failure =
                    $"Cost entry count differs at level +{offset}: " +
                    $"ours={scratch.Length} theirs={theirs.Length}.";
                return false;
            }

            for (var index = 0; index < scratch.Length; index++)
            {
                // Positional, because both sides walk the same authored list in the same order. A
                // resource mismatch means that assumption broke and is not a price disagreement.
                if (scratch[index].ResourceId != theirs[index].ResourceId)
                {
                    failure = $"Cost entry {index} is a different resource on each side at level +{offset}.";
                    return false;
                }

                run.Compare(
                    entityId,
                    $"level+{offset} {scratch[index].ResourceId}",
                    scratch[index].Value,
                    theirs[index].Value);
                compared++;
            }
        }

        // A maxed-out upgrade has no level the game would price without clamping, and an upgrade
        // with no authored cost has nothing to price at all. Neither is agreement.
        if (compared == 0) session.RecordExpectedSkip();

        failure = string.Empty;
        return true;
    }

    /// <summary>
    /// The reflected members needed to read an upgrade's authored cost, its per-level modifier list
    /// and its own priced curve. Resolved once; a missing member makes the verifier unavailable.
    /// </summary>
    private sealed class UpgradeCostContract
    {
        private const BindingFlags Instance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        private readonly MethodInfo _getGuid;
        private readonly FieldInfo _resourceCost;
        private readonly FieldInfo _costModPerLevel;
        private readonly MethodInfo _resolveModifierList;
        private readonly FieldInfo _listModifiers;
        private readonly FieldInfo _listExponents;
        private readonly FieldInfo _modifierType;
        private readonly FieldInfo _modifierAmount;
        private readonly FieldInfo _modifierOrder;
        private readonly FieldInfo _level;
        private readonly FieldInfo _queuedLevels;
        private readonly FieldInfo _maxLevel;
        private readonly MethodInfo _hasFiniteLevels;
        private readonly MethodInfo _getLeveledCostList;
        private readonly FieldInfo _costEntries;
        private readonly FieldInfo _tupleResource;
        private readonly MethodInfo _tupleGetValue;
        private readonly MethodInfo _resourceGetGuid;

        private UpgradeCostContract(
            MethodInfo getGuid,
            FieldInfo resourceCost,
            FieldInfo costModPerLevel,
            MethodInfo resolveModifierList,
            FieldInfo listModifiers,
            FieldInfo listExponents,
            FieldInfo modifierType,
            FieldInfo modifierAmount,
            FieldInfo modifierOrder,
            FieldInfo level,
            FieldInfo queuedLevels,
            FieldInfo maxLevel,
            MethodInfo hasFiniteLevels,
            MethodInfo getLeveledCostList,
            FieldInfo costEntries,
            FieldInfo tupleResource,
            MethodInfo tupleGetValue,
            MethodInfo resourceGetGuid)
        {
            _getGuid = getGuid;
            _resourceCost = resourceCost;
            _costModPerLevel = costModPerLevel;
            _resolveModifierList = resolveModifierList;
            _listModifiers = listModifiers;
            _listExponents = listExponents;
            _modifierType = modifierType;
            _modifierAmount = modifierAmount;
            _modifierOrder = modifierOrder;
            _level = level;
            _queuedLevels = queuedLevels;
            _maxLevel = maxLevel;
            _hasFiniteLevels = hasFiniteLevels;
            _getLeveledCostList = getLeveledCostList;
            _costEntries = costEntries;
            _tupleResource = tupleResource;
            _tupleGetValue = tupleGetValue;
            _resourceGetGuid = resourceGetGuid;
        }

        internal static UpgradeCostContract? TryResolve(Type? upgradeType)
        {
            if (upgradeType is null) return null;

            var getGuid = FindNoArg(upgradeType, "GetGuid");
            var resourceCost = upgradeType.GetField("resourceCost", Instance);
            var costModPerLevel = upgradeType.GetField("resourceCostModPerLevel", Instance);
            var level = upgradeType.GetField("level", Instance);
            var queuedLevels = upgradeType.GetField("queuedLevels", Instance);
            var maxLevel = upgradeType.GetField("maxLevel", Instance);
            var hasFiniteLevels = FindNoArg(upgradeType, "HasFiniteLevels");
            var getLeveledCostList = upgradeType.GetMethod(
                "GetLeveledCostList", Instance, null, new[] { typeof(int) }, null);
            if (getGuid is null || resourceCost is null || costModPerLevel is null || level is null ||
                queuedLevels is null || maxLevel is null || hasFiniteLevels is null ||
                getLeveledCostList is null)
            {
                return null;
            }

            var costEntries = resourceCost.FieldType.GetField("costs", Instance);
            var tupleType = costEntries?.FieldType is { IsGenericType: true } list
                ? list.GetGenericArguments()[0]
                : null;
            var tupleResource = tupleType?.GetField("resource", Instance);
            var tupleGetValue = tupleType is null ? null : FindNoArg(tupleType, "GetValue");
            var resourceGetGuid = tupleResource is null
                ? null
                : FindNoArg(tupleResource.FieldType, "GetGuid");
            if (costEntries is null || tupleResource is null || tupleGetValue is null ||
                resourceGetGuid is null)
            {
                return null;
            }

            var resolveModifierList = FindNoArg(costModPerLevel.FieldType, "GetValue");
            var listType = resolveModifierList?.ReturnType;
            var listModifiers = listType?.GetField("modifiers", Instance);
            var listExponents = listType?.GetField("exponents", Instance);
            var modifierType = listModifiers?.FieldType is { IsGenericType: true } modifiers
                ? modifiers.GetGenericArguments()[0]
                : null;
            var typeField = modifierType?.GetField("type", Instance);
            var amountField = modifierType?.GetField("adjustReal", Instance);
            var orderField = modifierType?.GetField("order", Instance);
            if (resolveModifierList is null || listModifiers is null || listExponents is null ||
                typeField is null || amountField is null || orderField is null)
            {
                return null;
            }

            return new UpgradeCostContract(
                getGuid, resourceCost, costModPerLevel, resolveModifierList, listModifiers,
                listExponents, typeField, amountField, orderField, level, queuedLevels, maxLevel,
                hasFiniteLevels, getLeveledCostList, costEntries, tupleResource, tupleGetValue,
                resourceGetGuid);
        }

        internal Guid ReadGuid(object upgrade) =>
            _getGuid.Invoke(upgrade, null) is Guid guid ? guid : Guid.Empty;

        internal int ReadCommittedLevel(object upgrade) =>
            Convert.ToInt32(_level.GetValue(upgrade)) +
            Convert.ToInt32(_queuedLevels.GetValue(upgrade));

        /// <summary>
        /// The highest level the game will price without clamping. Infinite upgrades have none, which
        /// is expressed as the largest level any probe could reach rather than as a special case.
        /// </summary>
        internal int ReadPricedCeiling(object upgrade) =>
            (bool)_hasFiniteLevels.Invoke(upgrade, null)!
                ? Convert.ToInt32(_maxLevel.GetValue(upgrade)) - 1
                : int.MaxValue;

        internal object? InvokeLeveledCost(object upgrade, int offset) =>
            _getLeveledCostList.Invoke(upgrade, new object[] { offset });

        /// <summary>Puts the memoised level back to the one the game's own callers ask for.</summary>
        internal void RestoreCache(object upgrade)
        {
            try
            {
                _getLeveledCostList.Invoke(upgrade, new object[] { 0 });
            }
            catch (Exception)
            {
                // Nothing useful to do: the restore is a courtesy to the next render, and a build
                // that throws here has already failed the verification that got us this far.
            }
        }

        internal bool TryReadAuthoredCost(
            object upgrade, out GameResourceCost[] costs, out string failure) =>
            TryDecodeCostList(_resourceCost.GetValue(upgrade), out costs, out failure);

        internal bool TryDecodeCostList(
            object? costList, out GameResourceCost[] costs, out string failure)
        {
            costs = Array.Empty<GameResourceCost>();
            if (costList is null)
            {
                failure = "An upgrade cost list was null.";
                return false;
            }

            if (_costEntries.GetValue(costList) is not IList entries)
            {
                failure = "An upgrade cost list did not expose its entries as a list.";
                return false;
            }

            costs = new GameResourceCost[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var resource = entry is null ? null : _tupleResource.GetValue(entry);
                if (resource is null)
                {
                    failure = $"Upgrade cost entry {index} had no resource.";
                    return false;
                }

                costs[index] = new GameResourceCost(
                    _resourceGetGuid.Invoke(resource, null) is Guid guid ? guid : Guid.Empty,
                    _tupleGetValue.Invoke(entry, null) is BigDouble value ? value : BigDouble.NaN);
            }

            failure = string.Empty;
            return true;
        }

        internal bool TryReadPerLevelModifiers(
            object upgrade,
            out GameValueModifier[] modifiers,
            out GameValueModifier[] exponents,
            out string failure)
        {
            modifiers = Array.Empty<GameValueModifier>();
            exponents = Array.Empty<GameValueModifier>();

            var reference = _costModPerLevel.GetValue(upgrade);
            var list = reference is null ? null : _resolveModifierList.Invoke(reference, null);
            if (list is null)
            {
                // An upgrade naming no modifier list is a flat-cost upgrade, which the game prices
                // by skipping the scaling branch entirely. Nothing is missing.
                failure = string.Empty;
                return true;
            }

            return TryReadModifiers(_listModifiers.GetValue(list), out modifiers, out failure) &&
                TryReadModifiers(_listExponents.GetValue(list), out exponents, out failure);
        }

        private bool TryReadModifiers(
            object? source, out GameValueModifier[] modifiers, out string failure)
        {
            modifiers = Array.Empty<GameValueModifier>();
            if (source is null)
            {
                failure = string.Empty;
                return true;
            }

            if (source is not IList entries)
            {
                failure = "A per-level modifier list was not a list.";
                return false;
            }

            modifiers = new GameValueModifier[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                var rawType = entry is null ? null : _modifierType.GetValue(entry);
                if (rawType is null)
                {
                    failure = $"Per-level modifier {index} had no type.";
                    return false;
                }

                // The ported enum was transcribed from the game's ordering, so the ordinal maps
                // across. An unknown member fails the entity rather than defaulting to Raw, which
                // would silently change the arithmetic.
                var ordinal = Convert.ToInt32(rawType);
                if (!Enum.IsDefined(typeof(GameValueModifierType), ordinal))
                {
                    failure = $"Per-level modifier {index} has an unported type {ordinal}.";
                    return false;
                }

                modifiers[index] = new GameValueModifier(
                    (GameValueModifierType)ordinal,
                    _modifierAmount.GetValue(entry) is BigDouble amount ? amount : BigDouble.NaN,
                    Convert.ToInt32(_modifierOrder.GetValue(entry)));
            }

            failure = string.Empty;
            return true;
        }

        private static MethodInfo? FindNoArg(Type type, string name) =>
            type.GetMethod(name, Instance, null, Type.EmptyTypes, null);
    }
}

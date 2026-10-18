using System;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

/// <summary>
/// What the mastery hooks record, read through one binding set bound at install.
/// </summary>
/// <remarks>
/// A hook runs inside the game's own call, so nothing it reads may throw back into the game. An
/// expected native failure drops that one observation; anything else drops it too and goes to the
/// failure log, once per install, because every later one has the same cause.
/// </remarks>
internal static class MentorMasteryPatchBridge
{
    private static MentorMasteryEventJournal? _journal;
    private static Func<long>? _readLifecycleEpoch;
    private static MentorMasteryObservationBindings? _bindings;
    private static Action<string>? _failureLog;
    private static bool _failureReported;
    private static int _suppressionDepth;
    private static object? _artifact;
    private static object? _artifactContainer;
    private static MentorAmount _artifactAmount;

    internal static void Install(
        MentorMasteryEventJournal journal,
        Func<long> readLifecycleEpoch,
        Action<string> failureLog,
        Func<string, Type?>? resolveType = null,
        Func<string, bool>? includeContract = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _readLifecycleEpoch = readLifecycleEpoch ??
                              throw new ArgumentNullException(nameof(readLifecycleEpoch));
        _failureLog = failureLog ?? throw new ArgumentNullException(nameof(failureLog));
        _failureReported = false;
        if (!MentorMasteryObservationBindings.TryCreate(out _bindings, out var reason, resolveType, includeContract))
            failureLog("Mentor records no earned mastery XP: " + reason);
        _journal.Reset(Math.Max(0, _readLifecycleEpoch()));
    }

    internal static void ResetLifecycle(long epoch)
    {
        _artifact = null;
        _artifactContainer = null;
        _artifactAmount = default;
        _journal?.Reset(epoch);
    }

    internal static IDisposable Suppress()
    {
        _suppressionDepth++;
        return new Suppression();
    }

    internal static void Observe(
        object source,
        BigDouble amount,
        MasteryExperienceDomain domain)
    {
        if (_suppressionDepth != 0 || !IsGameplayScene() ||
            !TryReadSource(source, domain, out var id, out var mastery, out var eligible) ||
            !TryAmount(amount, out var observed))
            return;
        _journal?.Publish(ReadEpoch(), domain, id, mastery, eligible, observed);
    }

    internal static void BeginArtifact(object source)
    {
        _artifact = null;
        _artifactContainer = null;
        _artifactAmount = default;
        if (_suppressionDepth != 0 || !IsGameplayScene() || _bindings is not { } native) return;
        try
        {
            var container = native.ExperienceElement(source);
            if (container is null) return;
            _artifact = source;
            _artifactContainer = container;
        }
        catch (Exception exception)
        {
            Contain(exception, "an artifact's experience container");
            _artifact = null;
            _artifactContainer = null;
        }
    }

    internal static void ObserveContainer(object container, BigDouble amount)
    {
        if (_suppressionDepth != 0 || _artifact is null ||
            !ReferenceEquals(container, _artifactContainer) ||
            !TryAmount(amount, out var observed))
            return;
        _artifactAmount = _artifactAmount.Add(observed);
    }

    internal static void EndArtifact(bool succeeded)
    {
        var source = _artifact;
        var amount = _artifactAmount;
        _artifact = null;
        _artifactContainer = null;
        _artifactAmount = default;
        if (succeeded && source is not null && amount.IsValidPositive &&
            TryReadSource(
                source,
                MasteryExperienceDomain.Artifact,
                out var id,
                out var mastery,
                out var eligible))
        {
            _journal?.Publish(
                ReadEpoch(),
                MasteryExperienceDomain.Artifact,
                id,
                mastery,
                eligible,
                amount);
        }
    }

    private static bool TryReadSource(
        object source,
        MasteryExperienceDomain domain,
        out Guid id,
        out int mastery,
        out bool eligible)
    {
        id = Guid.Empty;
        mastery = 0;
        eligible = false;
        if (_bindings is not { } native) return false;
        try
        {
            switch (domain)
            {
                case MasteryExperienceDomain.Spell:
                    id = native.SpellId(source);
                    mastery = native.SpellMastery(source);
                    eligible = native.SpellDiscovered(source);
                    break;
                case MasteryExperienceDomain.Artifact:
                    id = native.EquipmentId(source);
                    mastery = native.EquipmentMastery(source);
                    eligible = native.EquipmentCreated(source);
                    break;
                default:
                    id = native.AlchemyId(source);
                    mastery = native.AlchemyMastery(source);
                    eligible = native.AlchemyAvailable(source);
                    break;
            }
            return true;
        }
        catch (Exception exception)
        {
            Contain(exception, "a " + domain + " mastery source");
            return false;
        }
    }

    private static bool TryAmount(BigDouble value, out MentorAmount amount)
    {
        amount = new MentorAmount(value.Mantissa, value.Exponent);
        return amount.IsValidPositive;
    }

    private static long ReadEpoch()
    {
        try { return _readLifecycleEpoch?.Invoke() ?? 0; }
        catch (Exception exception)
        {
            Contain(exception, "the lifecycle epoch");
            return 0;
        }
    }

    private static void Contain(Exception exception, string what)
    {
        if (NativeFailureClassifier.IsExpected(exception) ||
            _failureReported || _failureLog is not { } log) return;
        _failureReported = true;
        log("Mentor's mastery hook could not read " + what + ", so that XP is not shared: " +
            ExceptionDetail.Describe(exception));
    }

    private static bool IsGameplayScene() => MentorGameplayScene.IsActive();

    private sealed class Suppression : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _suppressionDepth = Math.Max(0, _suppressionDepth - 1);
        }
    }
}

[HarmonyPatch]
internal static class MentorSpellMasteryPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("SpellRecipeSO")?.GetMethod(
            "GainMasteryExp",
            ReflectionUtil.InstanceFlags,
            null,
            new[] { typeof(BigDouble) },
            null);

    internal static void Postfix(object __instance, BigDouble exp) =>
        MentorMasteryPatchBridge.Observe(
            __instance, exp, MasteryExperienceDomain.Spell);
}

[HarmonyPatch]
internal static class MentorAlchemyMasteryPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("AlchemyRecipeSO")?.GetMethod(
            "GainMasteryXp",
            ReflectionUtil.InstanceFlags,
            null,
            new[] { typeof(BigDouble) },
            null);

    internal static void Postfix(object __instance, BigDouble exp) =>
        MentorMasteryPatchBridge.Observe(
            __instance, exp, MasteryExperienceDomain.Alchemy);
}

[HarmonyPatch]
internal static class MentorArtifactTickPatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("EquipmentSO")?.GetMethod(
            "IncrementActive",
            ReflectionUtil.InstanceFlags,
            null,
            new[] { typeof(double) },
            null);

    internal static void Prefix(object __instance) =>
        MentorMasteryPatchBridge.BeginArtifact(__instance);

    internal static Exception? Finalizer(Exception? __exception)
    {
        MentorMasteryPatchBridge.EndArtifact(__exception is null);
        return __exception;
    }
}

[HarmonyPatch]
internal static class MentorArtifactExperiencePatch
{
    internal static MethodBase? TargetMethod() =>
        ReflectionUtil.FindLoadedType("ExperienceContainer")?.GetMethod(
            "GainExperience",
            ReflectionUtil.InstanceFlags,
            null,
            new[] { typeof(BigDouble) },
            null);

    internal static void Prefix(object __instance, BigDouble __0) =>
        MentorMasteryPatchBridge.ObserveContainer(__instance, __0);
}

using System;
using System.Reflection;
using HarmonyLib;
using OrbModding.Common;
using OrbModding.Common.Runtime.World;

namespace OrbMentor;

internal static class MentorMasteryPatchBridge
{
    private static MentorMasteryEventJournal? _journal;
    private static Func<long>? _readLifecycleEpoch;
    private static int _suppressionDepth;
    private static object? _artifact;
    private static object? _artifactContainer;
    private static MentorAmount _artifactAmount;

    internal static void Install(
        MentorMasteryEventJournal journal,
        Func<long> readLifecycleEpoch)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _readLifecycleEpoch = readLifecycleEpoch ??
                              throw new ArgumentNullException(nameof(readLifecycleEpoch));
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
        if (_suppressionDepth != 0 || !IsGameplayScene()) return;
        try
        {
            var method = FindMethod(source.GetType(), "GetExperienceElement", Type.EmptyTypes);
            var container = method?.Invoke(source, null);
            if (container is null) return;
            _artifact = source;
            _artifactContainer = container;
        }
        catch
        {
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
        try
        {
            if (RuntimeIdentityRegistryBinding.Shared.ReadStableUuid(source) is not { } stable) return false;
            id = stable;
            var masteryField = FindField(source.GetType(), "masteryLevel");
            if (masteryField is null) return false;
            mastery = Convert.ToInt32(masteryField.GetValue(source) ?? 0);
            if (domain == MasteryExperienceDomain.Artifact)
            {
                eligible = FindField(source.GetType(), "isCreated")?.GetValue(source) is true;
            }
            else
            {
                var availability = FindMethod(
                    source.GetType(),
                    domain == MasteryExperienceDomain.Spell ? "IsDiscovered" : "IsAvailable",
                    Type.EmptyTypes);
                eligible = availability?.Invoke(source, null) is true;
            }
            return true;
        }
        catch
        {
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
        catch { return 0; }
    }

    private static bool IsGameplayScene() => MentorGameplayScene.IsActive();

    private static FieldInfo? FindField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly);
            if (field is not null) return field;
        }
        return null;
    }

    private static MethodInfo? FindMethod(Type type, string name, Type[] parameters)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var method = current.GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.DeclaredOnly,
                null,
                parameters,
                null);
            if (method is not null) return method;
        }
        return null;
    }

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

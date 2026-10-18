using System;
using System.Collections.Generic;
using System.Linq;

namespace OrbModConfig;

internal enum NativeUiStartupReadinessKind
{
    Ready,
    NotYetPresent,
    Mismatch,
}

internal readonly record struct NativeTopBarCandidateFact(
    string ItemName,
    int MatchCount,
    bool HasIcon);

internal readonly record struct NativeUiStartupReadinessObservation(
    NativeUiStartupReadinessKind Kind,
    string Reason);

internal static class NativeTopBarReadinessPolicy
{
    internal static readonly IReadOnlyList<string> RequiredItemNames =
        new[]
        {
            "ScreenTime",
            "ScreenMagic",
            "ScreenScholar",
            "ScreenAlchemy",
            "ScreenWorld",
            "ScreenWorkshop",
        };

    internal static NativeUiStartupReadinessKind Classify(
        IReadOnlyList<NativeTopBarCandidateFact> candidates)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));
        if (candidates.Count != RequiredItemNames.Count ||
            !RequiredItemNames.SequenceEqual(
                candidates.Select(candidate => candidate.ItemName),
                StringComparer.Ordinal))
            return NativeUiStartupReadinessKind.Mismatch;
        if (candidates.Any(candidate =>
                candidate.MatchCount < 0 ||
                candidate.MatchCount > 1 ||
                candidate.MatchCount == 1 && !candidate.HasIcon))
            return NativeUiStartupReadinessKind.Mismatch;
        if (candidates.Any(candidate => candidate.MatchCount == 0))
            return NativeUiStartupReadinessKind.NotYetPresent;
        return NativeUiStartupReadinessKind.Ready;
    }
}

internal readonly record struct UiSurfaceAdmission(
    bool QuickControls,
    bool ModsRail)
{
    internal static UiSurfaceAdmission Waiting => new(false, false);
    internal static UiSurfaceAdmission Both => new(true, true);
}

internal sealed class UiStartupReadinessGate
{
    internal const float FastRetryIntervalSeconds = 0.1f;
    internal const float StartupWindowSeconds = 30.0f;

    private bool _active;
    private float _elapsedSeconds;
    private float _untilNextInspection;
    private UiSurfaceAdmission _admission = UiSurfaceAdmission.Waiting;

    internal UiSurfaceAdmission Admission => _admission;

    internal void Begin()
    {
        _active = true;
        _elapsedSeconds = 0f;
        _untilNextInspection = 0f;
        _admission = UiSurfaceAdmission.Waiting;
    }

    internal bool ShouldInspect(float unscaledDeltaTime)
    {
        if (!_active || _admission.QuickControls || _admission.ModsRail) return false;
        var elapsed = Math.Max(0f, unscaledDeltaTime);
        _elapsedSeconds += elapsed;
        _untilNextInspection -= elapsed;
        return _untilNextInspection <= 0f;
    }

    internal UiSurfaceAdmission Observe(NativeUiStartupReadinessKind readiness)
    {
        if (readiness == NativeUiStartupReadinessKind.Ready)
            _admission = UiSurfaceAdmission.Both;
        else if (readiness == NativeUiStartupReadinessKind.Mismatch ||
                 _elapsedSeconds >= StartupWindowSeconds)
            _admission = UiSurfaceAdmission.Both;
        else
        {
            _untilNextInspection = FastRetryIntervalSeconds;
            _admission = UiSurfaceAdmission.Waiting;
        }
        return _admission;
    }

    internal void Reset()
    {
        _active = false;
        _elapsedSeconds = 0f;
        _untilNextInspection = 0f;
        _admission = UiSurfaceAdmission.Waiting;
    }
}

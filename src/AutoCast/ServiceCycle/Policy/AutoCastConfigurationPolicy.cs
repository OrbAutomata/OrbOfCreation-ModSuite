using System;
using OrbModding.Common.Runtime.Configuration;
using OrbModding.Common.Runtime;

namespace OrbAutomata;

/// <summary>
/// Pure predicates over the immutable configuration that gate whether the Auto Cast service runs and
/// define the gameplay thresholds its admission ladder applies.
/// </summary>
internal static class AutoCastConfigurationPolicy
{
    /// <summary>
    /// The ceiling the legacy engine applied to the manual pause on top of the configured range.
    /// </summary>
    internal const float MaximumManualPauseSeconds = 60.0f;

    internal static bool IsOperational(SuiteRuntimeConfiguration configuration) =>
        configuration.General.Enabled &&
        configuration.CanStartAutoCastActively;

    /// <summary>How long a manual cast silences the service for.</summary>
    internal static MonotonicDuration ManualPause(SuiteRuntimeConfiguration configuration)
    {
        var seconds = Math.Max(
            0f, Math.Min(MaximumManualPauseSeconds, configuration.AutoCast.ManualPauseSeconds));
        return MonotonicDuration.FromTimeSpan(TimeSpan.FromSeconds(seconds));
    }

    /// <summary>
    /// The share of its ceiling a resource must hold before a spell touching it may be cast, as a
    /// fraction. Clamped to <c>[0, 1]</c>, so a nonsense percentage cannot block everything forever.
    /// </summary>
    internal static double StartResourceFraction(SuiteRuntimeConfiguration configuration) =>
        Math.Max(0.0, Math.Min(100.0, configuration.AutoCast.StartResourcePercent)) / 100.0;

    internal static bool HoldsFullCharge(SuiteRuntimeConfiguration configuration) =>
        configuration.AutoCast.FullCharge;

    internal static bool IsRitualAware(SuiteRuntimeConfiguration configuration) =>
        configuration.AutoCast.RitualAware;
}

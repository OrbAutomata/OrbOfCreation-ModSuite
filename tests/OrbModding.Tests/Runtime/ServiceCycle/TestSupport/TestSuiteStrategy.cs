using System;
using OrbModding.Common.Runtime.Strategy;

namespace OrbModding.Tests.Runtime.ServiceCycle.TestSupport;

/// <summary>
/// Strategy bulletins for tests, built the way a strategist would build them.
/// </summary>
/// <remarks>
/// The suite has one bulletin type and every registry starts on the neutral one, so a test that does
/// not care about strategy publishes nothing at all. These are for the tests that do: which bulletin
/// a cycle was pinned to, and how far the one generation has advanced.
/// </remarks>
internal static class TestSuiteStrategy
{
    /// <summary>The bulletin a registry starts on. Constrains nothing.</summary>
    internal static SuiteStrategy Neutral => SuiteStrategyDefaults.Neutral;

    private static readonly Guid MarkerResource = new("9f2a6c14-0b3d-4c8e-9a51-6f0f9b2d7c33");

    /// <summary>A bulletin a test can tell apart from another by the number it carries.</summary>
    /// <remarks>
    /// It rides on a real stance — an absolute floor on a resource no test service spends — so the
    /// number travels through the same table a strategist would publish, and a service recording it
    /// says which bulletin reached it and nothing else.
    /// </remarks>
    internal static SuiteStrategy WithSetting(int setting) => new SuiteStrategyBuilder()
        .With(SuiteResourceStance.FloorOf(MarkerResource, new BigDouble(setting)))
        .Build(SuiteStrategyProvenance.Goal, Guid.Empty);

    /// <summary>Reads back what <see cref="WithSetting"/> put in.</summary>
    internal static int SettingOf(SuiteStrategy bulletin) =>
        (int)bulletin.StanceFor(MarkerResource).FloorAbsolute.ToDouble();
}

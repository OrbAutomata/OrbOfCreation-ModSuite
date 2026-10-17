using OrbAutomata;
using OrbMentor;

namespace OrbModding.Common.Runtime.Configuration;

/// <summary>
/// The immutable reading of everything the suite is configured to do, published as one suite-wide
/// slot with one generation and handed to every service.
/// </summary>
/// <remarks>
/// <para>
/// Not to be confused with <c>SuiteConfiguration</c>, which binds the BepInEx file: that type owns
/// the live mutable entries and the transaction that creates them, this one is the deeply immutable
/// snapshot of their values that crosses to worker threads.
/// </para>
/// <para>
/// Public with internal members, the same bargain <c>GameWorldState</c> makes: the runtime's public
/// registration surface names this type, so it cannot be internal, and nothing outside the suite has
/// any business reading a setting off it.
/// </para>
/// </remarks>
public sealed record SuiteRuntimeConfiguration
{
    internal SuiteGeneralConfiguration General { get; init; } = new();
    internal AutoBuyConfiguration AutoBuy { get; init; } = new();
    internal AutoCastConfiguration AutoCast { get; init; } = new();
    internal AutoConceptConfiguration AutoConcept { get; init; } = new();
    internal AutoHarvestConfiguration AutoHarvest { get; init; } = new();
    internal AutoItemsConfiguration AutoItems { get; init; } = new();
    internal AutoScribeConfiguration AutoScribe { get; init; } = new();
    internal MentorConfiguration Mentor { get; init; } = new();
    internal SuiteSafetyConfiguration Safety { get; init; } = new();
    internal AutomataReserveConfiguration Reserves { get; init; } = new();

    internal bool CanStartAutoBuyActively =>
        AutoBuy.Mode == AutoBuyOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoCastActively =>
        AutoCast.Mode == AutoCastOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoConceptActively =>
        AutoConcept.Mode == AutoConceptOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoHarvestActively =>
        AutoHarvest.Mode == AutoHarvestOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoItemsActively =>
        AutoItems.Mode == AutoItemsOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartAutoScribeActively =>
        AutoScribe.Mode == AutoScribeOperationMode.Active && !Safety.EmergencyDisable;

    internal bool CanStartMentorActively =>
        Mentor.Mode == MentorOperationMode.Active && !Safety.EmergencyDisable;
}

/// <summary>
/// The all-defaults snapshot, held outside <see cref="SuiteRuntimeConfiguration"/> because a
/// published shape may not own non-constant static storage.
/// </summary>
internal static class SuiteRuntimeConfigurationDefaults
{
    /// <summary>
    /// What every service reads before the plugin has bound the configuration file. Everything is
    /// off, which is the safe reading of "nothing known yet" for a suite that acts on the game.
    /// </summary>
    internal static readonly SuiteRuntimeConfiguration Empty = new();
}

/// <summary>Whether the suite runs at all, and how often it reads the game.</summary>
internal sealed record SuiteGeneralConfiguration
{
    internal bool Enabled { get; init; }
    internal AutomationPace Pace { get; init; }
}

/// <summary>The stop the operator reaches for when the suite is doing something they did not want.</summary>
internal sealed record SuiteSafetyConfiguration
{
    internal bool EmergencyDisable { get; init; }
}

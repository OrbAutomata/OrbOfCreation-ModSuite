namespace OrbAutomata;

/// <summary>The native facts and saved preference used by passive-notification muting.</summary>
internal static class QualityOfLifeNativeContracts
{
    internal const string PassiveType = "quality-of-life.passive.type-action";
    internal const string StableGuid = "quality-of-life.passive-guid-action";
    internal const string Muted = "quality-of-life.passive-muted-action";
    internal const string Touched = "quality-of-life.passive-touched-action";
    internal const string Hidden = "quality-of-life.passive-hidden-action";
    internal const string Silent = "quality-of-life.passive-silent-action";

    internal static readonly string[] ContractIds =
    {
        PassiveType,
        StableGuid,
        Muted,
        Touched,
        Hidden,
        Silent,
    };
}
